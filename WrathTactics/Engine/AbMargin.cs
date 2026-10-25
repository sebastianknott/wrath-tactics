using System;
using System.Collections.Generic;

namespace WrathTactics.Engine {
    /// <summary>
    /// Pure helpers behind the ABMinusAC condition: best-AB selection, margin math and the
    /// per-(attacker, enemy) cache. No engine state, unit-testable. The AB itself comes from
    /// RuleCalculateAttackBonus (with target), so it differs per enemy (shoot-into-melee,
    /// flanking, concealment); the party-best AB can therefore only be picked per enemy.
    /// </summary>
    internal static class AbMargin {
        /// <summary>Sentinel for "no usable AB" (dead, not in game, no weapon).</summary>
        internal const int NoAb = int.MinValue;

        /// <summary>Highest AB among the given values, ignoring <see cref="NoAb"/>; NoAb when none is usable.</summary>
        internal static int BestAb(IEnumerable<int> abs) {
            int best = NoAb;
            foreach (var ab in abs) {
                if (ab > best) best = ab;
            }
            return best;
        }

        /// <summary>ab - ac, or NaN when ab is <see cref="NoAb"/>.</summary>
        internal static float Margin(int ab, int ac) {
            return ab == NoAb ? float.NaN : (float)(ab - ac);
        }

        /// <summary>
        /// Returns the cached AB for (attacker, enemy) or computes and stores it. The cache is
        /// owned by the caller and must be cleared at the end of each rule evaluation.
        /// </summary>
        internal static int GetOrCompute<T>(Dictionary<(T, T), int> cache, T attacker, T enemy, Func<T, T, int> compute) {
            var key = (attacker, enemy);
            if (cache.TryGetValue(key, out var cached)) return cached;
            int ab = compute(attacker, enemy);
            cache[key] = ab;
            return ab;
        }
    }
}
