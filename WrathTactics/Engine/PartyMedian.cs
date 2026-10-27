using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WrathTactics.Engine {
    /// <summary>
    /// Pure helpers behind the EnemyHDMinusPartyMedian condition (WTW-33): median of the active
    /// party's effective levels, margin math and the trace text. No engine state, unit-testable.
    /// </summary>
    internal static class PartyMedian {
        /// <summary>
        /// Median of the given levels. Even count: mean of the two middle values (24 and 25 give 24.5).
        /// Empty input gives 0, the same "no party" value the maximum helper uses; the caller turns it into NaN.
        /// </summary>
        internal static float Median(IReadOnlyList<int> levels) {
            if (levels == null || levels.Count == 0) return 0f;
            var sorted = new int[levels.Count];
            for (int i = 0; i < sorted.Length; i++) sorted[i] = levels[i];
            Array.Sort(sorted);
            int mid = sorted.Length / 2;
            if (sorted.Length % 2 == 1) return sorted[mid];
            return (sorted[mid - 1] + sorted[mid]) / 2f;
        }

        /// <summary>enemyHD − median; NaN when the party is empty (median 0) so the row fails closed.</summary>
        internal static float Margin(int enemyHD, float median) {
            if (median <= 0f) return float.NaN;
            return enemyHD - median;
        }

        /// <summary>Levels sorted descending, e.g. "[37,26,26,26,26,26,26,25]", for the trace line.</summary>
        internal static string Describe(IReadOnlyList<int> levels) {
            if (levels == null || levels.Count == 0) return "[]";
            var sorted = new List<int>(levels);
            sorted.Sort((a, b) => b.CompareTo(a));
            var sb = new StringBuilder("[");
            for (int i = 0; i < sorted.Count; i++) {
                if (i > 0) sb.Append(',');
                sb.Append(sorted[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.Append(']').ToString();
        }

        internal static string Format(float value) => value.ToString("0.0#", CultureInfo.InvariantCulture);
    }
}
