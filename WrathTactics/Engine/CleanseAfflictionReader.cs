using System;
using System.Collections.Generic;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>One active buff reduced to what the affliction mask needs (WTW-30 cleanse).</summary>
    internal struct BuffReading {
        public AfflictionKinds Kinds;
        /// <summary>Polymorph descriptor: a shape change is never an affliction.</summary>
        public bool Polymorph;
        /// <summary>The buff grants immunity or suppression (Freedom of Movement, Death Ward ...): it carries
        /// descriptor bits for what it prevents, it is not the harm itself.</summary>
        public bool Protective;
        /// <summary>Cast by the afflicted unit itself.</summary>
        public bool SelfCast;
        /// <summary>Cast by a friendly unit (buff from a party member, not an attack).</summary>
        public bool AllySourced;
        /// <summary>Casting level of the buff (the level a dispel check is rolled against).</summary>
        public int CasterLevel;
    }

    /// <summary>
    /// Which harmful afflictions an ally currently carries (WTW-30 cleanse): a pure mask over its buffs and unit
    /// conditions, with the exclusions CleanseTable does not apply (Polymorph, self-cast buffs, ally-sourced
    /// buffs, protective buffs such as Freedom of Movement). Exact descriptor/condition members only.
    /// </summary>
    internal static class CleanseAfflictionReader {
        /// <summary>Pure core: union of the harmful kinds of the counted buffs plus the condition kinds.</summary>
        internal static AfflictionKinds Mask(IEnumerable<BuffReading> buffs, AfflictionKinds conditionKinds) {
            var mask = conditionKinds;
            if (buffs != null)
                foreach (var b in buffs) if (Counts(b)) mask |= b.Kinds;
            return mask;
        }

        /// <summary>Highest casting level per affliction bit over the counted buffs only.</summary>
        internal static Dictionary<AfflictionKinds, int> CasterLevels(IEnumerable<BuffReading> buffs) {
            var result = new Dictionary<AfflictionKinds, int>();
            if (buffs == null) return result;
            foreach (var b in buffs) {
                if (!Counts(b)) continue;
                foreach (var bit in CleanseTable.Bits(b.Kinds))
                    if (!result.TryGetValue(bit, out var known) || b.CasterLevel > known) result[bit] = b.CasterLevel;
            }
            return result;
        }

        internal static bool Counts(BuffReading b) {
            return !b.Polymorph && !b.Protective && !b.SelfCast && !b.AllySourced;
        }

        // Unit conditions that map to a harmful kind, computed once.
        static readonly KeyValuePair<UnitCondition, AfflictionKinds>[] s_conditionKinds = BuildConditionKinds();

        static KeyValuePair<UnitCondition, AfflictionKinds>[] BuildConditionKinds() {
            var list = new List<KeyValuePair<UnitCondition, AfflictionKinds>>();
            foreach (UnitCondition c in Enum.GetValues(typeof(UnitCondition))) {
                var k = CleanseTable.KindsFromCondition(c);
                if (k != AfflictionKinds.None) list.Add(new KeyValuePair<UnitCondition, AfflictionKinds>(c, k));
            }
            return list.ToArray();
        }

        /// <summary>Mask of a unit right now (None for null).</summary>
        internal static AfflictionKinds Read(UnitEntityData unit) {
            return unit == null ? AfflictionKinds.None : Read(unit, ReadBuffs(unit));
        }

        /// <summary>Mask from already-read buff readings (avoids a second pass over the buffs).</summary>
        internal static AfflictionKinds Read(UnitEntityData unit, List<BuffReading> readings) {
            if (unit == null) return AfflictionKinds.None;
            var conditions = AfflictionKinds.None;
            foreach (var pair in s_conditionKinds)
                if (unit.State.HasCondition(pair.Key)) conditions |= pair.Value;
            var mask = Mask(readings, conditions);
            return ConfirmStagger(mask, unit.State.HasCondition(UnitCondition.Staggered));
        }

        /// <summary>
        /// Drops the Staggered bit when the unit is not staggered right now. A buff keeps its Staggered descriptor
        /// while condition immunity (Freedom of Movement) switches the condition off, so the buff alone is no proof.
        /// </summary>
        internal static AfflictionKinds ConfirmStagger(AfflictionKinds mask, bool staggeredNow) {
            return staggeredNow ? mask : mask & ~AfflictionKinds.Staggered;
        }

        internal static List<BuffReading> ReadBuffs(UnitEntityData unit) {
            var readings = new List<BuffReading>();
            foreach (var buff in unit.Buffs.RawFacts) {
                var bp = buff.Blueprint;
                if (bp == null) continue;
                var caster = buff.Context?.MaybeCaster;
                readings.Add(new BuffReading {
                    Kinds = CleanseTable.KindsOfBuff(bp),
                    Polymorph = (bp.SpellDescriptor & SpellDescriptor.Polymorph) != 0,
                    Protective = IsProtective(bp),
                    SelfCast = caster != null && caster == unit,
                    AllySourced = caster != null && caster != unit && !caster.IsPlayersEnemy,
                    CasterLevel = buff.Context?.Params?.CasterLevel ?? 0,
                });
            }
            return readings;
        }

        /// <summary>
        /// A buff is protective when it carries immunity/suppression components AND everything harmful it carries is
        /// what those components protect against (Freedom of Movement marks MovementImpairing and is immune to it).
        /// A buff whose harm is not covered by its own immunities is an affliction even with an immunity attached:
        /// the Blindness spell buff also grants an immunity to an unrelated descriptor, yet blinds.
        /// </summary>
        internal static bool IsProtective(BlueprintBuff bp) {
            var protectedKinds = CleanseTable.ProtectedKinds(bp);
            if (protectedKinds == AfflictionKinds.None) return false;
            return (CleanseTable.KindsOfBuff(bp) & ~protectedKinds) == AfflictionKinds.None;
        }

        /// <summary>
        /// Value of a HasAffliction row -> kinds to test. "Any" (or empty) = every kind; otherwise one
        /// AfflictionKinds member name (exact, case-sensitive). Unknown names test nothing (None).
        /// </summary>
        internal static AfflictionKinds ParseValue(string value) {
            if (string.IsNullOrEmpty(value) || value == "Any") return AllKinds;
            AfflictionKinds k;
            return Enum.TryParse(value, false, out k) && k != AfflictionKinds.None ? k : AfflictionKinds.None;
        }

        internal static AfflictionKinds AllKinds { get { return CleanseDefaults.All; } }

        /// <summary>
        /// Condition semantics: Equal = the ally carries at least one of the chosen kinds, NotEqual = none of them
        /// (same shape as HasDescriptorEffect / HasCondition). An unparsable value never matches under Equal.
        /// </summary>
        internal static bool Matches(AfflictionKinds mask, string value, ConditionOperator op) {
            var want = ParseValue(value);
            bool has = (mask & want) != 0;
            return op == ConditionOperator.NotEqual ? !has : has;
        }
    }
}
