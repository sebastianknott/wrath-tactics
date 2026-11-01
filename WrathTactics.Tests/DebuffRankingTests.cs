using System.Collections.Generic;
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
            // Helpless: 4 + (dexMod 2 + 5) = 11 against a flat 1.
            Assert.Equal(11f, DebuffMath.Magnitude(helpless, 1, t));
            Assert.Equal(1f, DebuffMath.Magnitude(slow, 1, t));
            var byValue = S(DebuffMath.Magnitude(helpless, 1, t));
            var bySlow = S(DebuffMath.Magnitude(slow, 1, t));
            Assert.True(DebuffMath.Compare(byValue, bySlow, DebuffGroup.ArmorClass) < 0);
        }

        // ---- the search over upper bounds (DebuffMath.Pick) ----

        // Bounds are the upper values; measured holds the final score per index (null = rejected).
        static int Run(DebuffMath.DebuffScore[] bounds, DebuffMath.DebuffScore?[] measured, DebuffGroup group,
                       out int pruned, List<int> visited = null) {
            return DebuffMath.Pick(bounds, i => { visited?.Add(i); return measured[i]; }, group, out pruned);
        }

        [Fact]
        public void CanLand_rejects_zero_and_noise_but_not_small_positive_values() {
            Assert.False(DebuffMath.CanLand(0f));
            Assert.False(DebuffMath.CanLand(-1f));
            Assert.False(DebuffMath.CanLand(1e-4f));
            Assert.True(DebuffMath.CanLand(0.01f));
        }

        [Fact]
        public void Option_that_cannot_land_never_beats_a_real_one_even_when_it_does_not_end_on_damage() {
            // 0: bound 9, plain, but its measured EV is 0 (rejected). 1: ends on damage, EV 3.
            var bounds = new[] { S(9), S(5, ends: true) };
            var measured = new DebuffMath.DebuffScore?[] { null, S(3, ends: true) };
            Assert.Equal(1, Run(bounds, measured, DebuffGroup.ArmorClass, out _));
        }

        [Fact]
        public void Pick_returns_minus_one_when_every_candidate_is_rejected() {
            var bounds = new[] { S(4), S(2) };
            Assert.Equal(-1, Run(bounds, new DebuffMath.DebuffScore?[] { null, null }, DebuffGroup.Attack, out int pruned));
            Assert.Equal(0, pruned);
        }

        [Fact]
        public void Ends_on_damage_candidates_are_visited_last_in_stat_groups() {
            var bounds = new[] { S(10, ends: true), S(2), S(1) };
            var measured = new DebuffMath.DebuffScore?[] { S(10, ends: true), S(2), S(1) };
            var visited = new List<int>();
            // The plain 2 wins; the ends-on-damage 10 is class 1 and is pruned unvisited.
            Assert.Equal(1, Run(bounds, measured, DebuffGroup.Attack, out int pruned, visited));
            Assert.Equal(new[] { 1 }, visited);
            Assert.Equal(2, pruned);
        }

        [Fact]
        public void Ends_on_damage_candidate_is_picked_when_nothing_else_lands() {
            var bounds = new[] { S(10, ends: true), S(2) };
            var measured = new DebuffMath.DebuffScore?[] { S(10, ends: true), null };
            Assert.Equal(0, Run(bounds, measured, DebuffGroup.Attack, out _));
        }

        [Fact]
        public void Ends_on_damage_is_an_ordinary_candidate_for_incapacitated() {
            var bounds = new[] { S(10, ends: true), S(2) };
            var measured = new DebuffMath.DebuffScore?[] { S(10, ends: true), S(2) };
            Assert.Equal(0, Run(bounds, measured, DebuffGroup.Incapacitated, out _));
        }

        [Fact]
        public void Candidates_whose_bound_cannot_beat_the_best_are_pruned_unmeasured() {
            var bounds = new[] { S(8), S(6), S(4), S(3) };
            // First measures to 7, so the bounds 6, 4 and 3 cannot beat it.
            var measured = new DebuffMath.DebuffScore?[] { S(7), S(6), S(4), S(3) };
            var visited = new List<int>();
            Assert.Equal(0, Run(bounds, measured, DebuffGroup.Attack, out int pruned, visited));
            Assert.Equal(new[] { 0 }, visited);
            Assert.Equal(3, pruned);
        }

        [Fact]
        public void A_bound_that_still_beats_the_best_is_measured() {
            var bounds = new[] { S(8), S(6) };
            // The 8 measures down to 2 (high save), the 6 then measures to 5 and wins.
            var measured = new DebuffMath.DebuffScore?[] { S(2), S(5) };
            var visited = new List<int>();
            Assert.Equal(1, Run(bounds, measured, DebuffGroup.Attack, out int pruned, visited));
            Assert.Equal(new[] { 0, 1 }, visited);
            Assert.Equal(0, pruned);
        }

        [Fact]
        public void A_bound_tied_with_the_best_is_measured_so_the_spell_level_tiebreak_can_decide() {
            var bounds = new[] { S(5, level: 4), S(5, level: 1) };
            var measured = new DebuffMath.DebuffScore?[] { S(5, level: 4), S(5, level: 1) };
            Assert.Equal(1, Run(bounds, measured, DebuffGroup.Attack, out int pruned));
            Assert.Equal(0, pruned);
        }

        [Fact]
        public void Equal_bounds_keep_input_order() {
            var bounds = new[] { S(5), S(5), S(5) };
            Assert.Equal(new[] { 0, 1, 2 }, DebuffMath.OrderByBound(bounds, DebuffGroup.Attack));
        }
    }
}
