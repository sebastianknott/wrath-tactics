using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Newtonsoft.Json;
using Xunit;

namespace WrathTactics.Tests {
    /// <summary>[Fact] that skips cleanly when the game's blueprints.zip is not reachable (CI-less, other machines).</summary>
    public sealed class RealBlueprintFactAttribute : FactAttribute {
        public RealBlueprintFactAttribute() {
            if (!RealBlueprints.Available) Skip = "GameInstall/blueprints.zip not found";
        }
    }

    public sealed class RealBlueprintTheoryAttribute : TheoryAttribute {
        public RealBlueprintTheoryAttribute() {
            if (!RealBlueprints.Available) Skip = "GameInstall/blueprints.zip not found";
        }
    }

    /// <summary>
    /// Test-time access to the game's own blueprint export (<c>&lt;GameInstall&gt;/blueprints.zip</c>, *.jbp JSON).
    /// Blueprints are deserialized into the REAL game types with the game's JSON system (Json.Serializer + the
    /// GuidClassBinder type cache, which StartGameLoader.PrepareTypeCache would fill but needs Unity natives for).
    /// Blueprint references are resolved eagerly through BlueprintReferenceBase.Cached, because there is no
    /// BlueprintsCache/pack file in the test host. Nothing from the zip is ever written to disk or committed.
    ///
    /// Requires the test output to hold the ORIGINAL Assembly-CSharp.dll: the publicized overlay carries BepInEx
    /// attributes that crash mono's native GetCustomAttributes inside Newtonsoft (SIGSEGV).
    ///
    /// The zip is Zip64 with 250k entries, which mono's ZipArchive rejects ("local file header is corrupt"), so the
    /// central directory is read here and entries are inflated with DeflateStream.
    /// </summary>
    public static class RealBlueprints {
        struct Entry { public long LocalOffset; public long CompressedSize; public int Method; }

        static readonly object s_lock = new object();
        static string s_zipPath;
        static bool s_pathResolved;
        static Dictionary<string, Entry> s_entries;
        static Dictionary<string, string> s_pathByGuid;
        static Dictionary<string, string> s_typeByPath;
        static bool s_binderReady;
        static readonly Dictionary<string, SimpleBlueprint> s_loaded = new Dictionary<string, SimpleBlueprint>();
        static readonly Dictionary<string, int> s_budget = new Dictionary<string, int>();

        /// <summary>How deep buff/ability references are followed from the blueprint under test (ability -> buff -> buff list ...).</summary>
        const int ReferenceDepth = 3;

        public static bool Available { get { return ZipPath != null; } }

        static string ZipPath {
            get {
                lock (s_lock) {
                    if (s_pathResolved) return s_zipPath;
                    s_pathResolved = true;
                    var env = Environment.GetEnvironmentVariable("WRATH_BLUEPRINTS_ZIP");
                    if (!string.IsNullOrEmpty(env) && File.Exists(env)) return s_zipPath = env;
                    var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                    for (; dir != null; dir = dir.Parent) {
                        var candidate = Path.Combine(dir.FullName, "GameInstall", "blueprints.zip");
                        if (File.Exists(candidate)) return s_zipPath = candidate;
                    }
                    return null;
                }
            }
        }

        // ---------- zip64 central directory ----------

        static void EnsureIndex() {
            if (s_entries != null) return;
            var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            using (var fs = new FileStream(ZipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs)) {
                long cdOffset, cdSize, count;
                ReadEndOfCentralDirectory(fs, br, out cdOffset, out cdSize, out count);
                fs.Position = cdOffset;
                var cd = br.ReadBytes(checked((int)cdSize));
                int p = 0;
                for (long i = 0; i < count; i++) {
                    if (BitConverter.ToUInt32(cd, p) != 0x02014b50) throw new InvalidDataException("bad central directory entry " + i);
                    int method = BitConverter.ToUInt16(cd, p + 10);
                    long csize = BitConverter.ToUInt32(cd, p + 20);
                    long usize = BitConverter.ToUInt32(cd, p + 24);
                    int nameLen = BitConverter.ToUInt16(cd, p + 28);
                    int extraLen = BitConverter.ToUInt16(cd, p + 30);
                    int commentLen = BitConverter.ToUInt16(cd, p + 32);
                    long local = BitConverter.ToUInt32(cd, p + 42);
                    var name = Encoding.UTF8.GetString(cd, p + 46, nameLen);
                    int ep = p + 46 + nameLen, eEnd = ep + extraLen;
                    while (ep + 4 <= eEnd) {
                        int id = BitConverter.ToUInt16(cd, ep), len = BitConverter.ToUInt16(cd, ep + 2);
                        if (id == 1) {
                            int q = ep + 4;
                            if (usize == 0xFFFFFFFFL) { usize = BitConverter.ToInt64(cd, q); q += 8; }
                            if (csize == 0xFFFFFFFFL) { csize = BitConverter.ToInt64(cd, q); q += 8; }
                            if (local == 0xFFFFFFFFL) { local = BitConverter.ToInt64(cd, q); }
                        }
                        ep += 4 + len;
                    }
                    if (!name.EndsWith("/"))
                        entries[name] = new Entry { LocalOffset = local, CompressedSize = csize, Method = method };
                    p += 46 + nameLen + extraLen + commentLen;
                }
            }
            s_entries = entries;
        }

        static void ReadEndOfCentralDirectory(FileStream fs, BinaryReader br, out long cdOffset, out long cdSize, out long count) {
            int tail = (int)Math.Min(fs.Length, 70000);
            fs.Position = fs.Length - tail;
            var buf = br.ReadBytes(tail);
            int e = -1;
            for (int i = tail - 22; i >= 0; i--)
                if (BitConverter.ToUInt32(buf, i) == 0x06054b50) { e = i; break; }
            if (e < 0) throw new InvalidDataException("no end of central directory");
            count = BitConverter.ToUInt16(buf, e + 10);
            cdSize = BitConverter.ToUInt32(buf, e + 12);
            cdOffset = BitConverter.ToUInt32(buf, e + 16);
            if (count != 0xFFFF && cdSize != 0xFFFFFFFFL && cdOffset != 0xFFFFFFFFL) return;
            if (e < 20 || BitConverter.ToUInt32(buf, e - 20) != 0x07064b50) throw new InvalidDataException("no zip64 locator");
            long eocd64 = BitConverter.ToInt64(buf, e - 20 + 8);
            fs.Position = eocd64;
            if (br.ReadUInt32() != 0x06064b50) throw new InvalidDataException("bad zip64 end record");
            br.ReadInt64(); br.ReadUInt16(); br.ReadUInt16(); br.ReadUInt32(); br.ReadUInt32();
            br.ReadInt64();
            count = br.ReadInt64();
            cdSize = br.ReadInt64();
            cdOffset = br.ReadInt64();
        }

        static byte[] ReadBytes(Entry e, int maxBytes) {
            using (var fs = new FileStream(ZipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs)) {
                fs.Position = e.LocalOffset;
                if (br.ReadUInt32() != 0x04034b50) throw new InvalidDataException("bad local header");
                fs.Position = e.LocalOffset + 26;
                int nameLen = br.ReadUInt16(), extraLen = br.ReadUInt16();
                fs.Position = e.LocalOffset + 30 + nameLen + extraLen;
                var raw = new byte[e.CompressedSize];
                int got = 0;
                while (got < raw.Length) {
                    int n = fs.Read(raw, got, raw.Length - got);
                    if (n <= 0) throw new EndOfStreamException();
                    got += n;
                }
                if (e.Method == 0) {
                    if (maxBytes >= raw.Length) return raw;
                    var cut = new byte[maxBytes];
                    Array.Copy(raw, cut, maxBytes);
                    return cut;
                }
                using (var ds = new DeflateStream(new MemoryStream(raw), CompressionMode.Decompress))
                using (var ms = new MemoryStream()) {
                    var chunk = new byte[16384];
                    int n;
                    while ((n = ds.Read(chunk, 0, chunk.Length)) > 0) {
                        ms.Write(chunk, 0, n);
                        if (ms.Length >= maxBytes) break;
                    }
                    return ms.ToArray();
                }
            }
        }

        // ---------- public lookups ----------

        /// <summary>Every .jbp path in the zip.</summary>
        public static List<string> Paths(string prefix = "") {
            lock (s_lock) {
                EnsureIndex();
                var r = new List<string>();
                foreach (var k in s_entries.Keys)
                    if (k.EndsWith(".jbp", StringComparison.Ordinal) && k.StartsWith(prefix, StringComparison.Ordinal)) r.Add(k);
                r.Sort(StringComparer.Ordinal);
                return r;
            }
        }

        /// <summary>Raw JSON text of a .jbp.</summary>
        public static string Text(string path) {
            lock (s_lock) {
                EnsureIndex();
                return Encoding.UTF8.GetString(ReadBytes(s_entries[path], int.MaxValue));
            }
        }

        /// <summary>Paths of every blueprint whose root type is <paramref name="typeName"/> (e.g. "BlueprintAbility"), sorted.</summary>
        public static List<string> PathsOfType(string typeName) {
            lock (s_lock) {
                EnsureGuidIndex();
                var r = new List<string>();
                foreach (var kv in s_typeByPath) if (kv.Value == typeName) r.Add(kv.Key);
                r.Sort(StringComparer.Ordinal);
                return r;
            }
        }

        /// <summary>Root blueprint type name of a path ("BlueprintBuff"), null when unknown.</summary>
        public static string TypeOf(string path) {
            lock (s_lock) { EnsureGuidIndex(); string t; return s_typeByPath.TryGetValue(path, out t) ? t : null; }
        }

        /// <summary>AssetId of a path.</summary>
        public static string GuidOf(string path) {
            lock (s_lock) {
                EnsureGuidIndex();
                foreach (var kv in s_pathByGuid) if (kv.Value == path) return kv.Key;
                return null;
            }
        }

        // The AssetId is the first property of every .jbp; 120 inflated bytes are enough to read it.
        static void EnsureGuidIndex() {
            if (s_pathByGuid != null) return;
            EnsureIndex();
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var types = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in s_entries) {
                if (!kv.Key.EndsWith(".jbp", StringComparison.Ordinal)) continue;
                var head = Encoding.UTF8.GetString(ReadBytes(kv.Value, 200));
                int i = head.IndexOf("\"AssetId\": \"", StringComparison.Ordinal);
                if (i < 0) continue;
                map[head.Substring(i + 12, 32)] = kv.Key;
                int t = head.IndexOf("\"$type\": \"", StringComparison.Ordinal);
                int comma = t < 0 ? -1 : head.IndexOf(", ", t, StringComparison.Ordinal);
                int end = comma < 0 ? -1 : head.IndexOf('"', comma);
                if (end > 0) types[kv.Key] = head.Substring(comma + 2, end - comma - 2);
            }
            s_typeByPath = types;
            s_pathByGuid = map;
        }

        // ---------- deserialization into real game types ----------

        static void EnsureBinder() {
            if (s_binderReady) return;
            // SharedStringConverter builds a ScriptableObject per localized string (Unity native, absent here).
            // Localization text is irrelevant to the classifier: such fields come back null.
            for (int i = Json.Serializer.Converters.Count - 1; i >= 0; i--)
                if (Json.Serializer.Converters[i].GetType().Name == "SharedStringConverter") Json.Serializer.Converters.RemoveAt(i);
            Json.Serializer.Converters.Add(new NullSharedStringConverter());
            var binder = (GuidClassBinder)(object)Json.Serializer.Binder;
            foreach (var t in typeof(Json).Assembly.GetTypes()) {
                var attrs = t.GetCustomAttributes(typeof(TypeIdAttribute), false);
                if (attrs.Length > 0) binder.AddToCache(t, ((TypeIdAttribute)attrs[0]).GuidString);
            }
            s_binderReady = true;
        }

        sealed class NullSharedStringConverter : JsonConverter {
            public override bool CanConvert(Type objectType) { return objectType == typeof(Kingmaker.Localization.SharedStringAsset); }
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
                reader.Skip();
                return null;
            }
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) { throw new NotSupportedException(); }
        }

        /// <summary>Blueprint by file path, with buff/ability/fact references resolved.</summary>
        public static T ByPath<T>(string path) where T : SimpleBlueprint {
            lock (s_lock) {
                EnsureGuidIndex();
                string guid = null;
                foreach (var kv in s_pathByGuid) if (kv.Value == path) { guid = kv.Key; break; }
                if (guid == null) throw new FileNotFoundException(path);
                return (T)Resolve(guid, ReferenceDepth);
            }
        }

        /// <summary>Blueprint by AssetId, with buff/ability/fact references resolved.</summary>
        public static T ByGuid<T>(string guid) where T : SimpleBlueprint {
            lock (s_lock) { EnsureGuidIndex(); return (T)Resolve(guid, ReferenceDepth); }
        }

        /// <summary>The path a guid lives at (null when unknown).</summary>
        public static string PathOf(string guid) {
            lock (s_lock) { EnsureGuidIndex(); string p; return s_pathByGuid.TryGetValue(guid, out p) ? p : null; }
        }

        static SimpleBlueprint Resolve(string guid, int budget) {
            SimpleBlueprint bp;
            if (!s_loaded.TryGetValue(guid, out bp)) {
                string path;
                if (!s_pathByGuid.TryGetValue(guid, out path)) return null;
                bp = Parse(path, guid);
                s_loaded[guid] = bp;
                s_budget[guid] = -1;
            }
            // Facts (features, buffs as gate facts) are leaves: only their own data is read. Buffs and abilities
            // are followed, since the classifier reads suppressor buffs and the touch delivery ability.
            if (budget > s_budget[guid] && (bp is BlueprintBuff || bp is BlueprintAbility || budget == ReferenceDepth)) {
                s_budget[guid] = budget;
                if (budget > 0) Wire(bp, budget - 1, new HashSet<object>(ReferenceEqualityComparer.Instance));
            }
            return bp;
        }

        static SimpleBlueprint Parse(string path, string guid) {
            EnsureBinder();
            var text = Encoding.UTF8.GetString(ReadBytes(s_entries[path], int.MaxValue));
            using (var jr = new JsonTextReader(new StringReader(text))) {
                var wrapper = Json.Serializer.Deserialize<BlueprintJsonWrapper>(jr);
                var bp = wrapper.Data;
                bp.AssetGuid = BlueprintGuid.Parse(guid);
                bp.name = Path.GetFileNameWithoutExtension(path);
                return bp;
            }
        }

        static readonly PropertyInfo CachedProperty = typeof(BlueprintReferenceBase).GetProperty(
            "Cached", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        sealed class ReferenceEqualityComparer : IEqualityComparer<object> {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object x, object y) { return ReferenceEquals(x, y); }
            public int GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); }
        }

        // Walks the deserialized object graph of a blueprint and sets Cached on each blueprint reference.
        static void Wire(object node, int budget, HashSet<object> seen) {
            if (node == null) return;
            var type = node.GetType();
            if (type.IsPrimitive || type.IsEnum || node is string) return;
            if (!type.IsValueType && !seen.Add(node)) return;
            var reference = node as BlueprintReferenceBase;
            if (reference != null) {
                // IsEmpty() looks at the raw guid string, which deserialization leaves unset: test the parsed Guid.
                var guid = reference.Guid.ToString().Replace("-", "").ToLowerInvariant();
                if (guid.Trim('0').Length > 0 && CachedProperty.GetValue(reference, null) == null && ReferenceWorthFollowing(type)) {
                    var target = Resolve(guid, budget);
                    if (target != null) CachedProperty.SetValue(reference, target, null);
                }
                return;
            }
            if (node is SimpleBlueprint && budget < 0) return;
            var list = node as IEnumerable;
            if (list != null) {
                foreach (var item in list) Wire(item, budget, seen);
                return;
            }
            if (type.Namespace == null || !type.Namespace.StartsWith("Kingmaker", StringComparison.Ordinal)) return;
            for (var t = type; t != null && t != typeof(object); t = t.BaseType) {
                if (t.Namespace == null || !t.Namespace.StartsWith("Kingmaker", StringComparison.Ordinal)) break;
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
                    if (f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string)) continue;
                    Wire(f.GetValue(node), budget, seen);
                }
            }
        }

        // BlueprintReference<T> with T a buff, an ability or any unit fact (features are gate facts).
        static bool ReferenceWorthFollowing(Type referenceType) {
            for (var t = referenceType; t != null; t = t.BaseType) {
                if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(BlueprintReference<>)) continue;
                var arg = t.GetGenericArguments()[0];
                return typeof(BlueprintFact).IsAssignableFrom(arg) || typeof(BlueprintAbility).IsAssignableFrom(arg);
            }
            return false;
        }
    }
}
