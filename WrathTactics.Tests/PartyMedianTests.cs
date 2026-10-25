using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class PartyMedianTests {
        [Fact]
        public void odd_count_takes_the_middle_value() {
            Assert.Equal(25f, PartyMedian.Median(new[] { 30, 24, 25 }));
        }

        [Fact]
        public void even_count_takes_the_mean_of_the_two_middle_values() {
            Assert.Equal(24.5f, PartyMedian.Median(new[] { 36, 25, 25, 25, 24, 24, 24, 24 }));
            Assert.Equal(26f, PartyMedian.Median(new[] { 37, 26, 26, 26, 26, 26, 26, 25 }));
        }

        [Fact]
        public void one_outlier_does_not_move_the_median() {
            var withLegend = new[] { 36, 25, 25, 25, 24, 24, 24, 24 };
            var withoutLegend = new[] { 25, 25, 25, 25, 24, 24, 24, 24 };
            Assert.Equal(PartyMedian.Median(withoutLegend), PartyMedian.Median(withLegend));
        }

        [Fact]
        public void single_member_is_its_own_median() {
            Assert.Equal(13f, PartyMedian.Median(new[] { 13 }));
        }

        [Fact]
        public void two_members_give_their_mean() {
            Assert.Equal(20.5f, PartyMedian.Median(new[] { 24, 17 }));
        }

        [Fact]
        public void equal_levels_give_that_level() {
            Assert.Equal(13f, PartyMedian.Median(new[] { 13, 13, 13, 13, 13, 13 }));
        }

        [Fact]
        public void input_order_and_input_list_are_untouched() {
            var levels = new[] { 5, 1, 9, 3 };
            Assert.Equal(4f, PartyMedian.Median(levels));
            Assert.Equal(new[] { 5, 1, 9, 3 }, levels);
        }

        [Fact]
        public void empty_or_null_party_is_zero() {
            Assert.Equal(0f, PartyMedian.Median(new int[0]));
            Assert.Equal(0f, PartyMedian.Median(null));
        }

        [Fact]
        public void margin_is_enemy_hd_minus_median() {
            Assert.Equal(11.5f, PartyMedian.Margin(36, 24.5f));
            Assert.Equal(-0.5f, PartyMedian.Margin(24, 24.5f));
        }

        [Fact]
        public void margin_with_equal_levels_matches_the_maximum_margin() {
            // all members share one level: median == max, so the old and new margin agree
            Assert.Equal(7f, PartyMedian.Margin(20, PartyMedian.Median(new[] { 13, 13, 13, 13 })));
        }

        [Fact]
        public void margin_of_an_empty_party_is_nan() {
            Assert.True(float.IsNaN(PartyMedian.Margin(20, PartyMedian.Median(new int[0]))));
        }

        [Fact]
        public void half_margins_compare_against_whole_thresholds() {
            float margin = PartyMedian.Margin(36, 24.5f); // 11.5
            Assert.True(ConditionEvaluator.CompareFloat(margin, ConditionOperator.GreaterOrEqual, 11f));
            Assert.False(ConditionEvaluator.CompareFloat(margin, ConditionOperator.GreaterOrEqual, 12f));
            Assert.False(ConditionEvaluator.CompareFloat(margin, ConditionOperator.Equal, 11f));
        }

        [Fact]
        public void describe_lists_levels_descending() {
            Assert.Equal("[37,26,25]", PartyMedian.Describe(new[] { 25, 37, 26 }));
            Assert.Equal("[]", PartyMedian.Describe(new int[0]));
        }

        [Fact]
        public void format_keeps_one_or_two_decimals() {
            Assert.Equal("24.5", PartyMedian.Format(24.5f));
            Assert.Equal("26.0", PartyMedian.Format(26f));
            Assert.Equal("-0.5", PartyMedian.Format(-0.5f));
        }

        [Fact]
        public void property_is_appended_after_the_existing_ones() {
            // 35 on this branch (off upstream/master); local-fixups.sh pins the integration value.
            Assert.Equal(44, (int)ConditionProperty.EnemyHDMinusPartyMedian);
            Assert.Equal(25, (int)ConditionProperty.EnemyHDMinusPartyLevel); // stored rules keep their meaning
        }
    }
}
