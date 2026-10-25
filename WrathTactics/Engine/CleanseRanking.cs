using System.Collections.Generic;

namespace WrathTactics.Engine {
    /// <summary>One remedy as the ranking sees it (WTW-30 cleanse).</summary>
    internal struct CleanseCandidate {
        /// <summary>0 normal remedy, 1 suppressor, 2 heals hit points as well (last resort).</summary>
        public int Tier;
        /// <summary>Expected removals over the covered ally afflictions, in 10% steps (CleanseRanking.ExpectedSteps).</summary>
        public int ExpectedSteps;
        /// <summary>Spell grade; a class ability's free use counts as 0.</summary>
        public int Grade;
        /// <summary>True for a spell slot, false for a consumable (potion, wand charge).</summary>
        public bool IsSlot;
        /// <summary>Option GUID, final deterministic tie-break.</summary>
        public string Key;
    }

    /// <summary>Pure ordering of cleanse remedies (WTW-30 cleanse). Ascending: the best candidate sorts first.</summary>
    internal static class CleanseRanking {
        internal const int TierNormal = 0, TierSuppressor = 1, TierHeal = 2;

        /// <summary>Sum of the per-affliction removal chances, each bucketed upward to 10% steps.</summary>
        internal static int ExpectedSteps(IEnumerable<double> probabilities) {
            int sum = 0;
            foreach (var p in probabilities) sum += CleanseChance.Steps(p);
            return sum;
        }

        internal static int Compare(CleanseCandidate a, CleanseCandidate b) {
            if (a.Tier != b.Tier) return a.Tier < b.Tier ? -1 : 1;
            if (a.ExpectedSteps != b.ExpectedSteps) return a.ExpectedSteps > b.ExpectedSteps ? -1 : 1;
            if (a.Grade != b.Grade) return a.Grade < b.Grade ? -1 : 1;
            if (a.IsSlot != b.IsSlot) return a.IsSlot ? -1 : 1;
            return string.CompareOrdinal(a.Key, b.Key);
        }

        /// <summary>Index of the best candidate, -1 when the list is empty.</summary>
        internal static int Best(IReadOnlyList<CleanseCandidate> candidates) {
            int best = -1;
            for (int i = 0; i < candidates.Count; i++)
                if (best < 0 || Compare(candidates[i], candidates[best]) < 0) best = i;
            return best;
        }
    }
}
