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
        /// <summary>Enemies the option hits when cast at the enemy under test (1 for single/chain).</summary>
        public int Hits;
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
        /// Replaces every GroupOption row by one bundle, placed LAST so the cheap ordinary rows short-circuit
        /// before the expensive option search. One effect group per condition group: the first row naming a valid
        /// group decides, every row binds to it (the editor shows the group picker on the first row only). Without
        /// any GroupOption row the original list is returned untouched (no allocation).
        /// </summary>
        internal static List<Condition> Bundle(List<Condition> conds) {
            if (!HasOptionRows(conds)) return conds;
            var result = new List<Condition>(conds.Count);
            var bundle = new OptionRowsBundle();
            foreach (var c in conds) {
                if (c.Subject != ConditionSubject.GroupOption) {
                    result.Add(c);
                    continue;
                }
                if (bundle.Group == DebuffGroup.None) bundle.Group = GroupOf(c);
                bundle.Rows.Add(c);
            }
            result.Add(bundle);
            return result;
        }

        internal static bool HasOptionRows(List<Condition> conds) {
            foreach (var c in conds)
                if (c.Subject == ConditionSubject.GroupOption) return true;
            return false;
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
            // OptionSource compares a name, every other property a number; an unparsable number never matches
            // (a silent 0 would turn "garbage" into a real threshold).
            float threshold = 0f;
            if (row.Property != ConditionProperty.OptionSource
                && !float.TryParse(row.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out threshold))
                return false;
            switch (row.Property) {
                case ConditionProperty.SpellDCMinusSave:
                    return !float.IsNaN(v.Dcms) && ConditionEvaluator.CompareFloat(v.Dcms, row.Operator, threshold);
                case ConditionProperty.SpellResistMargin:
                    return !float.IsNaN(v.Srm) && ConditionEvaluator.CompareFloat(v.Srm, row.Operator, threshold);
                case ConditionProperty.OptionEffect:
                    return ConditionEvaluator.CompareFloat(v.Effect, row.Operator, threshold);
                case ConditionProperty.OptionEnemiesInRadius:
                    return ConditionEvaluator.CompareFloat(v.Hits, row.Operator, threshold);
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

        /// <summary>
        /// True once the pair budget of the current rule evaluation is spent. The count path (EnemyCount rows)
        /// must see every enemy, so it passes enforce = false and is never capped.
        /// </summary>
        internal static bool CapReached(int pairsUsed, bool enforce = true) {
            return enforce && pairsUsed >= MaxOptionPairsPerRuleTick;
        }

        /// <summary>
        /// Whether the end of an evaluation retires the resume cursor: only when the bundle was actually evaluated
        /// and the pass was not cut short. A tick that never reached the bundle (cheap rows failed first) keeps it.
        /// </summary>
        internal static bool ShouldClearCursor(bool bundleEvaluated, bool capHit) {
            return bundleEvaluated && !capHit;
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
    /// Resume point per (owner, rule id): the enemy the last capped evaluation did not get to. Plain keys so
    /// the store is testable without game types.
    /// </summary>
    internal static class OptionCursor {
        static readonly Dictionary<string, string> s_cursors = new Dictionary<string, string>();

        internal static string KeyFor(string ownerId, string ruleId) {
            return ownerId + "|" + ruleId;
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
