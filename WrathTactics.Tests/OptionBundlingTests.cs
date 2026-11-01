using System.Collections.Generic;
using System.Linq;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    // GroupOption rows bind to one option: bundling, row matching, pair cap and resume cursor (WTW-30 phase b).
    public class OptionBundlingTests {
        static Condition Row(ConditionProperty p, ConditionOperator op, string value, string group = "ArmorClass") =>
            new Condition { Subject = ConditionSubject.GroupOption, Property = p, Operator = op, Value = value, Value2 = group };

        static Condition Plain(ConditionProperty p) =>
            new Condition { Subject = ConditionSubject.Enemy, Property = p };

        [Theory]
        [InlineData("ArmorClass", DebuffGroup.ArmorClass)]
        [InlineData("Incapacitated", DebuffGroup.Incapacitated)]
        [InlineData("saveWill", DebuffGroup.SaveWill)]
        [InlineData("None", DebuffGroup.None)]
        [InlineData("", DebuffGroup.None)]
        [InlineData("Nonsense", DebuffGroup.None)]
        [InlineData("99", DebuffGroup.None)]
        public void GroupOf_parses_the_group_name(string value2, DebuffGroup expected) {
            Assert.Equal(expected, OptionBundling.GroupOf(new Condition { Value2 = value2 }));
        }

        [Fact]
        public void GroupOf_null_row_is_None() {
            Assert.Equal(DebuffGroup.None, OptionBundling.GroupOf(null));
        }

        [Fact]
        public void Bundle_without_option_rows_returns_the_same_list_instance() {
            var conds = new List<Condition> { Plain(ConditionProperty.HpPercent), Plain(ConditionProperty.AC) };
            Assert.Same(conds, OptionBundling.Bundle(conds));
        }

        [Fact]
        public void Bundle_is_placed_last_so_cheap_rows_short_circuit_first() {
            var a = Plain(ConditionProperty.HpPercent);
            var r1 = Row(ConditionProperty.SpellDCMinusSave, ConditionOperator.GreaterOrEqual, "5");
            var b = Plain(ConditionProperty.AC);
            var r2 = Row(ConditionProperty.OptionEffect, ConditionOperator.GreaterOrEqual, "4");
            var result = OptionBundling.Bundle(new List<Condition> { a, r1, b, r2 });

            Assert.Equal(3, result.Count);
            Assert.Same(a, result[0]);
            Assert.Same(b, result[1]);
            var bundle = Assert.IsType<OptionRowsBundle>(result[2]);
            Assert.Equal(new[] { r1, r2 }, bundle.Rows);
            Assert.Equal(DebuffGroup.ArmorClass, bundle.Group);
            Assert.Equal(ConditionSubject.GroupOption, bundle.Subject);
        }

        [Fact]
        public void Bundle_applies_the_first_group_to_all_rows_and_drops_none() {
            var r1 = Row(ConditionProperty.OptionEffect, ConditionOperator.GreaterOrEqual, "4", "ArmorClass");
            var r2 = Row(ConditionProperty.OptionEffect, ConditionOperator.GreaterOrEqual, "2", "Attack");
            var r3 = Row(ConditionProperty.SpellDCMinusSave, ConditionOperator.GreaterOrEqual, "0", "");
            var bundle = Assert.IsType<OptionRowsBundle>(Assert.Single(OptionBundling.Bundle(new List<Condition> { r1, r2, r3 })));
            Assert.Equal(new[] { r1, r2, r3 }, bundle.Rows);
            Assert.Equal(DebuffGroup.ArmorClass, bundle.Group);
        }

        [Fact]
        public void Bundle_group_comes_from_the_first_row_with_a_valid_group() {
            var r1 = Row(ConditionProperty.OptionEffect, ConditionOperator.GreaterOrEqual, "4", "");
            var r2 = Row(ConditionProperty.OptionEffect, ConditionOperator.GreaterOrEqual, "4", "Speed");
            var bundle = (OptionRowsBundle)OptionBundling.Bundle(new List<Condition> { r1, r2 })[0];
            Assert.Equal(DebuffGroup.Speed, bundle.Group);
        }

        [Fact]
        public void Bundle_takes_the_group_from_the_first_row() {
            var r1 = Row(ConditionProperty.OptionEffect, ConditionOperator.GreaterOrEqual, "4", "Speed");
            var result = OptionBundling.Bundle(new List<Condition> { r1 });
            Assert.Equal(DebuffGroup.Speed, ((OptionRowsBundle)result[0]).Group);
        }

        [Fact]
        public void Unparsable_threshold_never_matches() {
            var row = Row(ConditionProperty.OptionEffect, ConditionOperator.GreaterOrEqual, "abc");
            Assert.False(OptionBundling.RowMatches(row, Values(effect: 5f)));
            var lt = Row(ConditionProperty.OptionEffect, ConditionOperator.LessThan, "");
            Assert.False(OptionBundling.RowMatches(lt, Values(effect: -1f)));
        }

        [Fact]
        public void Count_path_is_not_capped() {
            Assert.True(OptionBundling.CapReached(1000));
            Assert.False(OptionBundling.CapReached(1000, enforce: false));
        }

        [Theory]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        public void Cursor_is_cleared_only_when_the_bundle_ran_uncapped(bool evaluated, bool capHit, bool expected) {
            Assert.Equal(expected, OptionBundling.ShouldClearCursor(evaluated, capHit));
        }

        [Fact]
        public void FindBundle_returns_the_bundle_or_null() {
            var result = OptionBundling.Bundle(new List<Condition> { Row(ConditionProperty.OptionEffect, ConditionOperator.Equal, "1") });
            Assert.NotNull(OptionBundling.FindBundle(result));
            Assert.Null(OptionBundling.FindBundle(new List<Condition> { Plain(ConditionProperty.AC) }));
        }

        static OptionValues Values(float dcms = 0, float srm = 0, float effect = 0, DebuffSourceKind kind = DebuffSourceKind.Spell) =>
            new OptionValues { Dcms = dcms, Srm = srm, Effect = effect, Kind = kind };

        [Theory]
        [InlineData(ConditionOperator.GreaterOrEqual, "5", 5f, true)]
        [InlineData(ConditionOperator.GreaterOrEqual, "5", 4f, false)]
        [InlineData(ConditionOperator.LessThan, "5", 4f, true)]
        [InlineData(ConditionOperator.LessThan, "5", 5f, false)]
        public void SpellDCMinusSave_row_compares_the_measured_margin(ConditionOperator op, string value, float dcms, bool expected) {
            var row = Row(ConditionProperty.SpellDCMinusSave, op, value);
            Assert.Equal(expected, OptionBundling.RowMatches(row, Values(dcms: dcms)));
        }

        [Fact]
        public void SpellDCMinusSave_row_with_nan_never_matches() {
            var row = Row(ConditionProperty.SpellDCMinusSave, ConditionOperator.LessThan, "100");
            Assert.False(OptionBundling.RowMatches(row, Values(dcms: float.NaN)));
        }

        [Fact]
        public void SpellResistMargin_row_compares_the_measured_margin() {
            var row = Row(ConditionProperty.SpellResistMargin, ConditionOperator.GreaterOrEqual, "3");
            Assert.True(OptionBundling.RowMatches(row, Values(srm: 3)));
            Assert.False(OptionBundling.RowMatches(row, Values(srm: 2)));
            Assert.False(OptionBundling.RowMatches(row, Values(srm: float.NaN)));
        }

        [Fact]
        public void SpellResistMargin_row_accepts_the_no_sr_sentinel() {
            var row = Row(ConditionProperty.SpellResistMargin, ConditionOperator.GreaterOrEqual, "0");
            Assert.True(OptionBundling.RowMatches(row, Values(srm: ConditionEvaluator.SpellResistMarginPasses)));
        }

        [Fact]
        public void OptionEffect_row_compares_the_magnitude() {
            var row = Row(ConditionProperty.OptionEffect, ConditionOperator.GreaterOrEqual, "4");
            Assert.True(OptionBundling.RowMatches(row, Values(effect: 4f)));
            Assert.False(OptionBundling.RowMatches(row, Values(effect: 3.5f)));
        }

        [Theory]
        [InlineData(ConditionOperator.Equal, "Scroll", DebuffSourceKind.Scroll, true)]
        [InlineData(ConditionOperator.Equal, "Scroll", DebuffSourceKind.Spell, false)]
        [InlineData(ConditionOperator.NotEqual, "Scroll", DebuffSourceKind.Spell, true)]
        [InlineData(ConditionOperator.NotEqual, "Scroll", DebuffSourceKind.Scroll, false)]
        [InlineData(ConditionOperator.Equal, "thrownitem", DebuffSourceKind.ThrownItem, true)]
        public void OptionSource_row_compares_the_source_kind_name(ConditionOperator op, string value, DebuffSourceKind kind, bool expected) {
            var row = Row(ConditionProperty.OptionSource, op, value);
            Assert.Equal(expected, OptionBundling.RowMatches(row, Values(kind: kind)));
        }

        [Fact]
        public void A_non_option_property_never_matches() {
            var row = Row(ConditionProperty.HpPercent, ConditionOperator.GreaterOrEqual, "0");
            Assert.False(OptionBundling.RowMatches(row, Values()));
        }

        [Fact]
        public void AllRowsMatch_requires_every_row_for_the_same_option() {
            var rows = new List<Condition> {
                Row(ConditionProperty.OptionEffect, ConditionOperator.GreaterOrEqual, "4"),
                Row(ConditionProperty.OptionSource, ConditionOperator.Equal, "Spell"),
            };
            Assert.True(OptionBundling.AllRowsMatch(rows, Values(effect: 4f, kind: DebuffSourceKind.Spell)));
            Assert.False(OptionBundling.AllRowsMatch(rows, Values(effect: 4f, kind: DebuffSourceKind.Scroll)));
            Assert.False(OptionBundling.AllRowsMatch(rows, Values(effect: 2f, kind: DebuffSourceKind.Spell)));
        }

        [Fact]
        public void AllRowsMatch_with_no_rows_holds() {
            Assert.True(OptionBundling.AllRowsMatch(new List<Condition>(), Values()));
        }

        [Fact]
        public void Pair_cap_is_48() {
            Assert.Equal(48, OptionBundling.MaxOptionPairsPerRuleTick);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(47, false)]
        [InlineData(48, true)]
        [InlineData(100, true)]
        public void CapReached_trips_at_the_limit(int used, bool expected) {
            Assert.Equal(expected, OptionBundling.CapReached(used));
        }

        [Fact]
        public void RotateTo_starts_at_the_matching_item_and_wraps() {
            var result = OptionBundling.RotateTo(new[] { 1, 2, 3, 4, 5 }, x => x == 3);
            Assert.Equal(new[] { 3, 4, 5, 1, 2 }, result);
        }

        [Fact]
        public void RotateTo_without_a_hit_keeps_the_order() {
            Assert.Equal(new[] { 1, 2, 3 }, OptionBundling.RotateTo(new[] { 1, 2, 3 }, x => x == 9));
        }

        [Fact]
        public void RotateTo_hit_on_the_first_item_keeps_the_order() {
            Assert.Equal(new[] { 1, 2, 3 }, OptionBundling.RotateTo(new[] { 1, 2, 3 }, x => x == 1));
        }

        [Fact]
        public void RotateTo_null_predicate_keeps_the_order() {
            Assert.Equal(new[] { 1, 2 }, OptionBundling.RotateTo(new[] { 1, 2 }, null));
        }

        [Fact]
        public void Cursor_set_get_clear_roundtrip() {
            OptionCursor.ClearAll();
            var key = OptionCursor.KeyFor("owner", "rule-7");
            Assert.Null(OptionCursor.Get(key));
            OptionCursor.Set(key, "enemy-3");
            Assert.Equal("enemy-3", OptionCursor.Get(key));
            OptionCursor.Set(key, "enemy-5");
            Assert.Equal("enemy-5", OptionCursor.Get(key));
            OptionCursor.Clear(key);
            Assert.Null(OptionCursor.Get(key));
        }

        [Fact]
        public void Cursor_keys_separate_owner_and_rule() {
            OptionCursor.ClearAll();
            OptionCursor.Set(OptionCursor.KeyFor("a", "r1"), "x");
            Assert.Null(OptionCursor.Get(OptionCursor.KeyFor("b", "r1")));
            Assert.Null(OptionCursor.Get(OptionCursor.KeyFor("a", "r2")));
            Assert.NotEqual(OptionCursor.KeyFor("a", "12"), OptionCursor.KeyFor("a1", "2"));
        }
    }
}
