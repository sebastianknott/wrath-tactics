using System;
using System.Collections.Generic;
using System.Globalization;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// The GroupOption rows of one condition group, folded into a single evaluation unit (WTW-30 phase b).
    /// Built per evaluation by <see cref="OptionBundling.Bundle"/>, never serialised: it only lives between
    /// EvaluateEnemyBucket and EvaluateOptionBundle. All rows must hold for ONE and the same option, which is
    /// why they cannot be evaluated one by one like ordinary enemy rows.
    /// </summary>
    internal sealed class OptionRowsBundle : Condition {
        public List<Condition> Rows = new List<Condition>();
        public DebuffGroup Group;

        public OptionRowsBundle() {
            Subject = ConditionSubject.GroupOption;
        }
    }

    /// <summary>The numbers one option shows to the GroupOption rows.</summary>
    internal struct OptionValues {
        public float Dcms;
        public float Srm;
        public float Effect;
        public DebuffSourceKind Kind;
    }

    /// <summary>Pure helpers of the GroupOption binding: bundling, row matching, pair cap and resume cursor.</summary>
    internal static class OptionBundling {
        /// <summary>Upper bound of (enemy, option) pairs one rule evaluation may measure; the rest resumes next tick.</summary>
        internal const int MaxOptionPairsPerRuleTick = 48;

        /// <summary>The effect group a GroupOption row names in Value2; None when missing or unknown.</summary>
        internal static DebuffGroup GroupOf(Condition row) {
            if (row == null || string.IsNullOrEmpty(row.Value2)) return DebuffGroup.None;
            return Enum.TryParse(row.Value2, true, out DebuffGroup g) && Enum.IsDefined(typeof(DebuffGroup), g) ? g : DebuffGroup.None;
        }

        /// <summary>
        /// Replaces every GroupOption row by one bundle at the position of the first such row. The group is taken
        /// from that first row; rows naming a different group are dropped and counted in <paramref name="droppedRows"/>
        /// (one effect group per condition group).
        /// </summary>
        internal static List<Condition> Bundle(List<Condition> conds, out int droppedRows) {
            droppedRows = 0;
            var result = new List<Condition>(conds.Count);
            OptionRowsBundle bundle = null;
            foreach (var c in conds) {
                if (c.Subject != ConditionSubject.GroupOption) {
                    result.Add(c);
                    continue;
                }
                if (bundle == null) {
                    bundle = new OptionRowsBundle { Group = GroupOf(c) };
                    result.Add(bundle);
                }
                if (GroupOf(c) == bundle.Group) bundle.Rows.Add(c);
                else droppedRows++;
            }
            return result;
        }

        internal static OptionRowsBundle FindBundle(List<Condition> conds) {
            foreach (var c in conds)
                if (c is OptionRowsBundle b) return b;
            return null;
        }

        /// <summary>
        /// Whether one row holds for the option's numbers. NaN measurements (no computable save / SR) never match,
        /// like their enemy-scope counterparts. A property that is not an option property never matches.
        /// </summary>
        internal static bool RowMatches(Condition row, OptionValues v) {
            float.TryParse(row.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out float threshold);
            switch (row.Property) {
                case ConditionProperty.SpellDCMinusSave:
                    return !float.IsNaN(v.Dcms) && ConditionEvaluator.CompareFloat(v.Dcms, row.Operator, threshold);
                case ConditionProperty.SpellResistMargin:
                    return !float.IsNaN(v.Srm) && ConditionEvaluator.CompareFloat(v.Srm, row.Operator, threshold);
                case ConditionProperty.OptionEffect:
                    return ConditionEvaluator.CompareFloat(v.Effect, row.Operator, threshold);
                case ConditionProperty.OptionSource: {
                    bool same = string.Equals(v.Kind.ToString(), row.Value, StringComparison.OrdinalIgnoreCase);
                    return row.Operator == ConditionOperator.NotEqual ? !same : same;
                }
                default:
                    return false;
            }
        }

        internal static bool AllRowsMatch(IList<Condition> rows, OptionValues v) {
            foreach (var r in rows)
                if (!RowMatches(r, v)) return false;
            return true;
        }

        /// <summary>True once the pair budget of the current rule evaluation is spent.</summary>
        internal static bool CapReached(int pairsUsed) {
            return pairsUsed >= MaxOptionPairsPerRuleTick;
        }

        /// <summary>
        /// Moves the list so it starts at the first item the predicate accepts (the resume point); the items
        /// before it follow at the end. Without a hit the order is unchanged.
        /// </summary>
        internal static List<T> RotateTo<T>(IEnumerable<T> items, Predicate<T> startAt) {
            var list = new List<T>(items);
            if (startAt == null) return list;
            int i = list.FindIndex(startAt);
            if (i <= 0) return list;
            var rotated = list.GetRange(i, list.Count - i);
            rotated.AddRange(list.GetRange(0, i));
            return rotated;
        }
    }

    /// <summary>
    /// Resume point per (owner, rule action): the enemy the last capped evaluation did not get to. Plain keys so
    /// the store is testable without game types.
    /// </summary>
    internal static class OptionCursor {
        static readonly Dictionary<string, string> s_cursors = new Dictionary<string, string>();

        internal static string KeyFor(string ownerId, int actionRef) {
            return ownerId + "|" + actionRef;
        }

        internal static void Set(string key, string enemyId) {
            s_cursors[key] = enemyId;
        }

        internal static string Get(string key) {
            return s_cursors.TryGetValue(key, out var id) ? id : null;
        }

        internal static void Clear(string key) {
            s_cursors.Remove(key);
        }

        internal static void ClearAll() {
            s_cursors.Clear();
        }
    }
}
