using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class DebuffRankingTests {
        static DebuffMath.DebuffScore S(float ev, bool ends = false, LockTier tier = LockTier.None,
                                        int level = 3, DebuffSourceKind kind = DebuffSourceKind.Spell) {
            return new DebuffMath.DebuffScore { Ev = ev, EndsOnDamage = ends, Tier = tier, SpellLevel = level, Kind = kind };
        }

        [Fact]
        public void Higher_expected_value_wins() {
            Assert.True(DebuffMath.Compare(S(6), S(2), DebuffGroup.ArmorClass) < 0);
            Assert.True(DebuffMath.Compare(S(2), S(6), DebuffGroup.ArmorClass) > 0);
        }

        [Fact]
        public void Ends_on_damage_loses_even_with_higher_value_in_stat_groups() {
            Assert.True(DebuffMath.Compare(S(10, ends: true), S(1), DebuffGroup.ArmorClass) > 0);
            Assert.True(DebuffMath.Compare(S(1), S(10, ends: true), DebuffGroup.ArmorClass) < 0);
        }

        [Fact]
        public void Ends_on_damage_is_ranked_by_value_for_incapacitated() {
            Assert.True(DebuffMath.Compare(S(10, ends: true), S(1), DebuffGroup.Incapacitated) < 0);
        }

        [Fact]
        public void Equal_value_in_incapacitated_prefers_the_stronger_lock() {
            Assert.True(DebuffMath.Compare(S(4, tier: LockTier.All), S(4, tier: LockTier.MoveOnly), DebuffGroup.Incapacitated) < 0);
            Assert.True(DebuffMath.Compare(S(4, tier: LockTier.NoFullRound), S(4, tier: LockTier.Random), DebuffGroup.Incapacitated) > 0);
        }

        [Fact]
        public void Lock_tier_is_ignored_outside_incapacitated() {
            Assert.True(DebuffMath.Compare(S(4, tier: LockTier.All, level: 5), S(4, tier: LockTier.None, level: 2), DebuffGroup.Attack) > 0);
        }

        [Fact]
        public void Equal_value_prefers_the_lower_spell_level() {
            Assert.True(DebuffMath.Compare(S(4, level: 2), S(4, level: 5), DebuffGroup.Attack) < 0);
        }

        [Fact]
        public void Equal_value_and_level_prefers_slot_over_scroll() {
            Assert.True(DebuffMath.Compare(S(4, kind: DebuffSourceKind.Spell), S(4, kind: DebuffSourceKind.Scroll), DebuffGroup.Attack) < 0);
            Assert.True(DebuffMath.Compare(S(4, kind: DebuffSourceKind.Scroll), S(4, kind: DebuffSourceKind.Spell), DebuffGroup.Attack) > 0);
        }

        [Fact]
        public void Identical_scores_compare_equal() {
            Assert.Equal(0, DebuffMath.Compare(S(4), S(4), DebuffGroup.Attack));
        }

        [Fact]
        public void Values_within_epsilon_count_as_tied() {
            Assert.True(DebuffMath.Compare(S(4.00005f, level: 5), S(4f, level: 2), DebuffGroup.Attack) > 0);
        }

        [Fact]
        public void Helpless_beats_slow_for_armor_class() {
            var t = new DebuffMath.TargetStats { DexMod = 2 };
            var helpless = DebuffConditionTable.ConditionEntries(Kingmaker.UnitLogic.UnitCondition.Helpless)
                .Find(e => e.Group == DebuffGroup.ArmorClass);
            var slow = new DebuffEntry { Group = DebuffGroup.ArmorClass, Fixed = 1f };
            Assert.True(DebuffMath.Magnitude(helpless, 1, t) > DebuffMath.Magnitude(slow, 1, t));
        }
    }
}
