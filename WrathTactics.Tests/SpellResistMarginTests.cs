using System.Collections.Generic;
using Kingmaker.Enums;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    // SpellResistMargin = caster level + penetration bonuses − target spell resistance.
    // The engine pieces (UnitPartSpellResistance, the caster's penetration facts) cannot
    // run here; these tests pin the pure arithmetic, the two sentinels and the stacking.
    public class SpellResistMarginTests {
        // Persisted JSON stores the numeric enum index — appending must never shift it.
        [Fact]
        public void Enum_index_is_pinned() {
            Assert.Equal(35, (int)ConditionProperty.SpellResistMargin);
        }

        [Theory]
        [InlineData(13, 6, 24, -5)]    // Nenio vs Tarry Demodand (SR 24): CL 13 + Spell Penetration/mythic 6
        [InlineData(13, 6, 35, -16)]   // same caster vs SR 35
        [InlineData(20, 0, 20, 0)]
        [InlineData(10, 4, 1, 13)]
        public void Margin_is_penetration_minus_resistance(int cl, int bonus, int sr, int expected) {
            Assert.Equal(expected, ConditionEvaluator.SpellResistMargin(cl, bonus, sr));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-3)]
        public void Target_without_resistance_passes_with_high_sentinel(int sr) {
            Assert.Equal(ConditionEvaluator.SpellResistMarginPasses, ConditionEvaluator.SpellResistMargin(13, 6, sr));
            Assert.Equal(99, ConditionEvaluator.SpellResistMarginPasses);
        }

        [Fact]
        public void Ability_without_resistance_check_passes_even_against_high_sr() {
            Assert.Equal(99, ConditionEvaluator.SpellResistMargin(
                abilityChecksSr: false, targetImmune: false, cl: 5, bonus: 0, sr: 35));
        }

        [Fact]
        public void Immune_target_fails_with_low_sentinel() {
            Assert.Equal(-99, ConditionEvaluator.SpellResistMarginFails);
            Assert.Equal(-99, ConditionEvaluator.SpellResistMargin(
                abilityChecksSr: true, targetImmune: true, cl: 20, bonus: 10, sr: 0));
        }

        [Fact]
        public void Immunity_wins_over_a_missing_resistance_check() {
            // RuleSpellResistanceCheck.get_IsSpellResisted tests TargetIsImmune before HasResistanceRoll.
            Assert.Equal(-99, ConditionEvaluator.SpellResistMargin(
                abilityChecksSr: false, targetImmune: true, cl: 20, bonus: 10, sr: 30));
        }

        [Fact]
        public void Checked_ability_against_resisting_target_uses_the_arithmetic() {
            Assert.Equal(-5, ConditionEvaluator.SpellResistMargin(
                abilityChecksSr: true, targetImmune: false, cl: 13, bonus: 6, sr: 24));
        }

        // Row thresholds from the wish: >= -9 is a 60 % chance (d20 + penetration > SR).
        [Theory]
        [InlineData(-5,  true)]
        [InlineData(-9,  true)]
        [InlineData(-16, false)]
        [InlineData(99,  true)]
        [InlineData(-99, false)]
        public void Margin_compares_against_threshold_minus_nine(int margin, bool expected) {
            Assert.Equal(expected,
                ConditionEvaluator.CompareFloat(margin, ConditionOperator.GreaterOrEqual, -9f));
        }

        // UnitPartSpellResistance.GetValue(context) = Max(0, base − AllSRPenalty − (Piercing ? 5 : 0) + ΣBonuses)
        [Theory]
        [InlineData(24, 0, false, 0, 24)]
        [InlineData(24, 2, false, 0, 22)]
        [InlineData(24, 0, true,  0, 19)]
        [InlineData(24, 0, false, 4, 28)]
        [InlineData(24, 2, true,  4, 21)]
        [InlineData(3,  9, false, 0, 0)]    // never below zero
        [InlineData(0,  0, false, 0, 0)]
        public void Effective_resistance_applies_penalty_piercing_and_bonuses(
            int baseSr, int penalty, bool piercing, int bonusSum, int expected) {
            Assert.Equal(expected,
                ConditionEvaluator.EffectiveSpellResistance(baseSr, penalty, piercing, bonusSum));
        }

        // SpellPenetrationMythicBonus: mythic level, halved (integer division) without the Greater fact.
        [Theory]
        [InlineData(0, false, 0)]
        [InlineData(1, false, 0)]
        [InlineData(5, false, 2)]
        [InlineData(10, false, 5)]
        [InlineData(0, true, 0)]
        [InlineData(5, true, 5)]
        [InlineData(10, true, 10)]
        public void Mythic_penetration_is_halved_without_greater(int mythicLevel, bool greater, int expected) {
            Assert.Equal(expected, ConditionEvaluator.MythicPenetration(mythicLevel, greater));
        }

        // The game adds each source to a ModifiableBonus with its ModifierDescriptor.
        // Feat and UntypedStackable stack (Spell Penetration + Greater + mythic add up).
        [Fact]
        public void Stackable_descriptors_add_up() {
            var sources = new List<KeyValuePair<ModifierDescriptor, int>> {
                new KeyValuePair<ModifierDescriptor, int>(ModifierDescriptor.Feat, 2),
                new KeyValuePair<ModifierDescriptor, int>(ModifierDescriptor.Feat, 2),
                new KeyValuePair<ModifierDescriptor, int>(ModifierDescriptor.UntypedStackable, 3),
            };
            Assert.Equal(7, ConditionEvaluator.SumPenetration(sources));
        }

        [Fact]
        public void Same_non_stackable_descriptor_keeps_only_the_highest() {
            var sources = new List<KeyValuePair<ModifierDescriptor, int>> {
                new KeyValuePair<ModifierDescriptor, int>(ModifierDescriptor.Enhancement, 2),
                new KeyValuePair<ModifierDescriptor, int>(ModifierDescriptor.Enhancement, 3),
            };
            Assert.Equal(3, ConditionEvaluator.SumPenetration(sources));
        }

        [Fact]
        public void Different_descriptors_add_up() {
            var sources = new List<KeyValuePair<ModifierDescriptor, int>> {
                new KeyValuePair<ModifierDescriptor, int>(ModifierDescriptor.Enhancement, 2),
                new KeyValuePair<ModifierDescriptor, int>(ModifierDescriptor.Insight, 3),
            };
            Assert.Equal(5, ConditionEvaluator.SumPenetration(sources));
        }

        [Fact]
        public void No_penetration_sources_sum_to_zero() {
            Assert.Equal(0, ConditionEvaluator.SumPenetration(new List<KeyValuePair<ModifierDescriptor, int>>()));
        }
    }
}
