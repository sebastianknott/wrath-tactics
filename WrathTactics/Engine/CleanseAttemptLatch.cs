using System.Collections.Generic;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// Remembers a cleanse attempt per (ally, affliction kind, remedy) for a short game-time window (WTW-30 cleanse).
    /// A dice remedy can fail and the affliction stays, so without this the next tick would cast the same remedy
    /// again. Written when the command is issued, not when it lands; it also keeps a second caster from stacking
    /// the same remedy on the same ally. Expired entries are dropped when they are read. Main thread only.
    /// </summary>
    internal static class CleanseAttemptLatch {
        internal const double AttemptLatchCombatSec = 6.0;
        internal const double AttemptLatchIdleSec = 60.0;

        /// <summary>Short block on (ally, kind) for every remedy, so the tick after an issue does not pick another
        /// remedy for the same affliction while the first cast is still resolving.</summary>
        internal const double GraceSeconds = 2.0;

        static readonly Dictionary<string, double> s_expiry = new Dictionary<string, double>();
        static readonly Dictionary<string, double> s_grace = new Dictionary<string, double>();

        static string GraceKey(string allyId, AfflictionKinds kind) {
            return allyId + "|" + (int)kind;
        }

        static string Key(string allyId, AfflictionKinds kind, string remedyKey) {
            return allyId + "|" + (int)kind + "|" + remedyKey;
        }

        internal static double WindowSeconds(bool inCombat) {
            return inCombat ? AttemptLatchCombatSec : AttemptLatchIdleSec;
        }

        /// <summary>Latches every kind bit in <paramref name="kinds"/> until now + the window.</summary>
        internal static void Record(string allyId, AfflictionKinds kinds, string remedyKey, double nowSeconds, bool inCombat) {
            double until = nowSeconds + WindowSeconds(inCombat);
            foreach (var bit in CleanseTable.Bits(kinds)) {
                s_expiry[Key(allyId, bit, remedyKey)] = until;
                s_grace[GraceKey(allyId, bit)] = nowSeconds + GraceSeconds;
            }
        }

        /// <summary>True while an earlier attempt of this remedy on this ally for this single kind bit is still in its window.</summary>
        internal static bool IsBlocked(string allyId, AfflictionKinds kind, string remedyKey, double nowSeconds) {
            if (s_grace.TryGetValue(GraceKey(allyId, kind), out var graceUntil)) {
                if (graceUntil > nowSeconds) return true;
                s_grace.Remove(GraceKey(allyId, kind));
            }
            var key = Key(allyId, kind, remedyKey);
            if (!s_expiry.TryGetValue(key, out var until)) return false;
            if (until > nowSeconds) return true;
            s_expiry.Remove(key);
            return false;
        }

        /// <summary>The bits of <paramref name="kinds"/> that this remedy may still try on this ally.</summary>
        internal static AfflictionKinds Unblocked(string allyId, AfflictionKinds kinds, string remedyKey, double nowSeconds) {
            var free = AfflictionKinds.None;
            foreach (var bit in CleanseTable.Bits(kinds))
                if (!IsBlocked(allyId, bit, remedyKey, nowSeconds)) free |= bit;
            return free;
        }

        internal static void Clear() { s_expiry.Clear(); s_grace.Clear(); }
    }
}
