using System;
using System.Collections.Generic;
using System.Linq;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// One independent count of a bucket: its own rows, threshold and comparison.
    /// A bucket's AllyCount/EnemyCount rows form ONE shared set (legacy: a unit counts only
    /// if it passes every shared row; threshold = max Value2, operator = first row's) plus
    /// one extra set per row flagged <see cref="Condition.CountSeparately"/>.
    /// Pure (no game types) so the grouping and tally are unit-testable with plain ints.
    /// </summary>
    internal sealed class CountSet {
        public List<Condition> Rows { get; }
        public float Threshold { get; }
        public ConditionOperator Operator { get; }

        public CountSet(List<Condition> rows) {
            Rows = rows;
            Threshold = ConditionEvaluator.ResolveCountThreshold(rows);
            Operator = rows[0].CountOperator;
        }

        /// <summary>
        /// Unflagged rows first as one shared set (identical to the pre-flag behavior when
        /// no row is flagged), then one set per flagged row in rule order.
        /// </summary>
        public static List<CountSet> Build(List<Condition> countConds) {
            var sets = new List<CountSet>();
            var shared = countConds.Where(c => !c.CountSeparately).ToList();
            if (shared.Count > 0) sets.Add(new CountSet(shared));
            foreach (var c in countConds.Where(c => c.CountSeparately))
                sets.Add(new CountSet(new List<Condition> { c }));
            return sets;
        }

        public bool Passes(int count) => ConditionEvaluator.CompareCount(count, Threshold, Operator);
    }

    internal sealed class CountTally<T> {
        public int[] Counts;
        public T[] FirstCounted;
        /// <summary>Per unit (input order): per-set pass flags; null when the unit failed the base filter.</summary>
        public List<bool[]> PerUnit = new List<bool[]>();
        public int FirstCountedIndex;
    }

    internal static class CountSets {
        /// <summary>
        /// Counts, per set, the units that pass <paramref name="basePass"/> AND every row of that set.
        /// </summary>
        public static CountTally<T> Tally<T>(IEnumerable<T> units, IReadOnlyList<CountSet> sets,
            Func<T, bool> basePass, Func<Condition, T, bool> rowPass) {
            var tally = new CountTally<T> {
                Counts = new int[sets.Count],
                FirstCounted = new T[sets.Count],
            };
            var hasFirst = new bool[sets.Count];
            foreach (var unit in units) {
                if (!basePass(unit)) { tally.PerUnit.Add(null); continue; }
                var passed = new bool[sets.Count];
                for (int i = 0; i < sets.Count; i++) {
                    bool all = true;
                    foreach (var row in sets[i].Rows) {
                        if (!rowPass(row, unit)) { all = false; break; }
                    }
                    if (!all) continue;
                    passed[i] = true;
                    tally.Counts[i]++;
                    if (!hasFirst[i]) { tally.FirstCounted[i] = unit; hasFirst[i] = true; }
                }
                tally.PerUnit.Add(passed);
            }
            return tally;
        }

        /// <summary>Every set's count must satisfy its own threshold and comparison.</summary>
        public static bool AllPass(IReadOnlyList<CountSet> sets, int[] counts) {
            for (int i = 0; i < sets.Count; i++)
                if (!sets[i].Passes(counts[i])) return false;
            return true;
        }

        /// <summary>First counted unit in set order (latch fallback), or default when no set counted anyone.</summary>
        public static T FirstCountedAny<T>(CountTally<T> tally) {
            for (int i = 0; i < tally.Counts.Length; i++)
                if (tally.Counts[i] > 0) return tally.FirstCounted[i];
            return default(T);
        }
    }
}
