using System.Collections.Generic;
using System.Linq;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    // Units are plain ints; a row "passes" for a unit when unit >= int.Parse(row.Value).
    public class CountSetsTests {
        static Condition Row(string minUnit, string threshold, ConditionOperator op, bool separately = false) =>
            new Condition {
                Subject = ConditionSubject.AllyCount, Value = minUnit, Value2 = threshold,
                CountOperator = op, CountSeparately = separately,
            };

        static bool RowPass(Condition c, int unit) => unit >= int.Parse(c.Value);

        static (List<CountSet> sets, CountTally<int> tally) Run(int[] units, params Condition[] rows) {
            var sets = CountSet.Build(rows.ToList());
            return (sets, CountSets.Tally(units, sets, u => true, RowPass));
        }

        [Fact]
        public void Unflagged_rows_form_one_set_with_max_threshold_and_first_operator() {
            var (sets, tally) = Run(new[] { 1, 5, 6, 9 },
                Row("5", "1", ConditionOperator.GreaterOrEqual),
                Row("6", "3", ConditionOperator.LessThan));
            Assert.Single(sets);
            Assert.Equal(3f, sets[0].Threshold);
            Assert.Equal(ConditionOperator.GreaterOrEqual, sets[0].Operator);
            Assert.Equal(new[] { 2 }, tally.Counts);   // units 6 and 9 satisfy BOTH rows
            Assert.False(CountSets.AllPass(sets, tally.Counts));
        }

        [Fact]
        public void Flagged_rows_each_get_own_threshold_and_operator() {
            var (sets, tally) = Run(new[] { 1, 5, 6, 9 },
                Row("5", "3", ConditionOperator.GreaterOrEqual, true),
                Row("6", "2", ConditionOperator.LessThan, true));
            Assert.Equal(2, sets.Count);
            Assert.Equal(new[] { 3, 2 }, tally.Counts);
            Assert.Equal(new[] { 3f, 2f }, sets.Select(s => s.Threshold).ToArray());
            Assert.True(sets[0].Passes(3));
            Assert.False(sets[1].Passes(2));            // 2 < 2 is false
            Assert.False(CountSets.AllPass(sets, tally.Counts));
        }

        [Fact]
        public void Flagged_rows_all_holding_pass() {
            var (sets, tally) = Run(new[] { 1, 5, 6, 9 },
                Row("5", "3", ConditionOperator.GreaterOrEqual, true),
                Row("6", "3", ConditionOperator.LessThan, true));
            Assert.True(CountSets.AllPass(sets, tally.Counts));
        }

        [Fact]
        public void Mixed_flagged_and_unflagged_unflagged_set_comes_first() {
            var (sets, tally) = Run(new[] { 1, 5, 6, 9 },
                Row("9", "1", ConditionOperator.GreaterOrEqual, true),
                Row("1", "4", ConditionOperator.GreaterOrEqual),
                Row("5", "2", ConditionOperator.GreaterOrEqual));
            Assert.Equal(2, sets.Count);
            Assert.Equal(new[] { 3, 1 }, tally.Counts); // shared: units 5,6,9; flagged: unit 9
            Assert.Equal(4f, sets[0].Threshold);
        }

        [Fact]
        public void Zero_threshold_with_equal_stays_zero() {
            // Daeran case: "none below X" and "none adjacent" as two independent == 0 counts.
            var (sets, tally) = Run(new[] { 1, 2, 3 },
                Row("4", "0", ConditionOperator.Equal, true),
                Row("3", "0", ConditionOperator.Equal, true));
            Assert.Equal(new[] { 0f, 0f }, sets.Select(s => s.Threshold).ToArray());
            Assert.Equal(new[] { 0, 1 }, tally.Counts);
            Assert.False(CountSets.AllPass(sets, tally.Counts));
        }

        [Fact]
        public void Base_filter_excludes_units_from_every_set() {
            var sets = CountSet.Build(new List<Condition> {
                Row("0", "1", ConditionOperator.GreaterOrEqual, true),
                Row("0", "1", ConditionOperator.GreaterOrEqual, true) });
            var tally = CountSets.Tally(new[] { 1, 2, 3 }, sets, u => u != 2, RowPass);
            Assert.Equal(new[] { 2, 2 }, tally.Counts);
            Assert.Null(tally.PerUnit[1]);
        }

        [Fact]
        public void First_counted_follows_set_order_and_is_default_when_nobody_counted() {
            var (sets, tally) = Run(new[] { 1, 5, 6 }, Row("5", "1", ConditionOperator.GreaterOrEqual, true));
            Assert.Equal(5, CountSets.FirstCountedAny(tally));
            var (_, none) = Run(new[] { 1 }, Row("5", "0", ConditionOperator.Equal, true));
            Assert.Equal(0, CountSets.FirstCountedAny(none));
        }

        [Fact]
        public void CountSeparately_defaults_to_false() {
            Assert.False(new Condition().CountSeparately);
        }

        [Fact]
        public void Legacy_json_without_field_reads_false_and_roundtrips_true() {
            var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<Condition>("{\"Value2\":\"3\"}");
            Assert.False(legacy.CountSeparately);
            var on = Newtonsoft.Json.JsonConvert.DeserializeObject<Condition>("{\"CountSeparately\":true}");
            Assert.True(on.CountSeparately);
        }
    }
}
