using UnityEngine;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    // Area debuffs are scored by the enemies they hit (WTW-30 phase c).
    public class DebuffAreaTests {
        // Pinned per branch: patched appends other ConditionProperty members first (local-fixups.sh rewrites these).
        [Fact]
        public void OptionEnemiesInRadius_property_index_is_stable() {
            Assert.Equal(38, (int)ConditionProperty.OptionEnemiesInRadius);
        }

        static readonly Vector3[] Cluster = {
            new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(0, 0, 3), new Vector3(30, 0, 30),
        };

        [Fact]
        public void Area_option_counts_the_enemies_around_the_pick() {
            Assert.Equal(3, DebuffMath.HitsFor(DebuffAreaKind.AroundEnemy, 5f, Cluster, Vector3.zero));
        }

        [Fact]
        public void Area_option_counts_the_pick_itself_at_least() {
            Assert.Equal(1, DebuffMath.HitsFor(DebuffAreaKind.AroundEnemy, 5f, new Vector3[0], Vector3.zero));
            Assert.Equal(1, DebuffMath.HitsFor(DebuffAreaKind.AroundEnemy, 5f, Cluster, new Vector3(100, 0, 100)));
        }

        [Fact]
        public void Single_and_chain_options_hit_one() {
            Assert.Equal(1, DebuffMath.HitsFor(DebuffAreaKind.Single, 5f, Cluster, Vector3.zero));
            Assert.Equal(1, DebuffMath.HitsFor(DebuffAreaKind.ChainEnemy, 5f, Cluster, Vector3.zero));
        }

        [Fact]
        public void Area_option_without_a_radius_hits_one() {
            Assert.Equal(1, DebuffMath.HitsFor(DebuffAreaKind.AroundEnemy, 0f, Cluster, Vector3.zero));
        }

        [Fact]
        public void Expected_value_scales_with_hits_so_an_area_option_beats_a_stronger_single_one() {
            float single = DebuffMath.ExpectedValue(4f, 1f, 1f, 1);
            float area = DebuffMath.ExpectedValue(2f, 1f, 1f, 3);
            Assert.True(area > single);
            var a = new DebuffMath.DebuffScore { Ev = area, SpellLevel = 3 };
            var s = new DebuffMath.DebuffScore { Ev = single, SpellLevel = 3 };
            Assert.True(DebuffMath.Compare(a, s, DebuffGroup.ArmorClass) < 0);
        }

        [Fact]
        public void Single_option_wins_when_the_area_hits_only_one() {
            float single = DebuffMath.ExpectedValue(4f, 1f, 1f, 1);
            float area = DebuffMath.ExpectedValue(2f, 1f, 1f, 1);
            var a = new DebuffMath.DebuffScore { Ev = area, SpellLevel = 3 };
            var s = new DebuffMath.DebuffScore { Ev = single, SpellLevel = 3 };
            Assert.True(DebuffMath.Compare(s, a, DebuffGroup.ArmorClass) < 0);
        }

        [Theory]
        [InlineData(ConditionOperator.GreaterOrEqual, "3", 3, true)]
        [InlineData(ConditionOperator.GreaterOrEqual, "4", 3, false)]
        [InlineData(ConditionOperator.LessThan, "3", 3, false)]
        public void OptionEnemiesInRadius_row_compares_the_hit_count(ConditionOperator op, string value, int hits, bool expected) {
            var row = new Condition { Subject = ConditionSubject.GroupOption, Property = ConditionProperty.OptionEnemiesInRadius, Operator = op, Value = value };
            Assert.Equal(expected, OptionBundling.RowMatches(row, new OptionValues { Hits = hits }));
        }
    }
}
