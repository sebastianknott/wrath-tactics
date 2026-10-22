using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    // Enums persist as numeric indices in preset/config JSON: pin every member.
    public class DebuffGroupTests {
        [Fact]
        public void ActionType_debuff_members_are_appended_after_the_existing_ones() {
            Assert.Equal(11, (int)ActionType.CastDebuffGroup);
            Assert.Equal(12, (int)ActionType.CastConditionOption);
        }

        [Fact]
        public void GroupOption_subject_is_appended_after_EnemyNearest() {
            Assert.Equal(22, (int)ConditionSubject.GroupOption);
        }

        // Pinned per branch: patched appends other ConditionProperty members first (local-fixups.sh rewrites these).
        [Fact]
        public void OptionEffect_property_index_is_stable() {
            Assert.Equal(37, (int)ConditionProperty.OptionEffect);
        }

        [Fact]
        public void OptionSource_property_index_is_stable() {
            Assert.Equal(38, (int)ConditionProperty.OptionSource);
        }

        [Theory]
        [InlineData(DebuffGroup.None, 0)]
        [InlineData(DebuffGroup.ArmorClass, 1)]
        [InlineData(DebuffGroup.Attack, 2)]
        [InlineData(DebuffGroup.Damage, 3)]
        [InlineData(DebuffGroup.SaveWill, 4)]
        [InlineData(DebuffGroup.SaveReflex, 5)]
        [InlineData(DebuffGroup.SaveFortitude, 6)]
        [InlineData(DebuffGroup.Dexterity, 7)]
        [InlineData(DebuffGroup.Strength, 8)]
        [InlineData(DebuffGroup.Constitution, 9)]
        [InlineData(DebuffGroup.Speed, 10)]
        [InlineData(DebuffGroup.Incapacitated, 11)]
        public void DebuffGroup_indices_are_stable(DebuffGroup g, int index) {
            Assert.Equal(index, (int)g);
        }

        [Theory]
        [InlineData(DebuffSourceKind.Spell, 0)]
        [InlineData(DebuffSourceKind.ClassAbility, 1)]
        [InlineData(DebuffSourceKind.ItemAbility, 2)]
        [InlineData(DebuffSourceKind.Scroll, 3)]
        [InlineData(DebuffSourceKind.ThrownItem, 4)]
        public void DebuffSourceKind_indices_are_stable(DebuffSourceKind k, int index) {
            Assert.Equal(index, (int)k);
        }

        [Fact]
        public void ActionDef_starts_without_a_group() {
            Assert.Equal(DebuffGroup.None, new ActionDef().DebuffGroup);
        }

        [Fact]
        public void DebuffDefaults_picks_armor_class_for_the_debuff_action() {
            Assert.Equal(DebuffGroup.ArmorClass, DebuffDefaults.GroupFor(ActionType.CastDebuffGroup, DebuffGroup.None));
        }

        [Fact]
        public void DebuffDefaults_keeps_an_explicit_choice() {
            Assert.Equal(DebuffGroup.Speed, DebuffDefaults.GroupFor(ActionType.CastDebuffGroup, DebuffGroup.Speed));
        }

        [Fact]
        public void DebuffDefaults_leaves_other_actions_alone() {
            Assert.Equal(DebuffGroup.None, DebuffDefaults.GroupFor(ActionType.CastSpell, DebuffGroup.None));
        }
    
        [Theory]
        [InlineData(SpellSourceMask.All)]
        [InlineData(SpellSourceMask.Spell)]
        [InlineData(SpellSourceMask.Scroll)]
        [InlineData(SpellSourceMask.Spell | SpellSourceMask.Scroll)]
        public void DebuffDefaults_keeps_a_source_mask_the_dropdown_offers(SpellSourceMask mask) {
            Assert.Equal(mask, DebuffDefaults.SourcesFor(ActionType.CastDebuffGroup, mask));
        }

        [Theory]
        [InlineData(SpellSourceMask.Potion)]
        [InlineData(SpellSourceMask.None)]
        [InlineData(SpellSourceMask.Potion | SpellSourceMask.Scroll)]
        public void DebuffDefaults_normalizes_an_unoffered_source_mask_to_All(SpellSourceMask mask) {
            Assert.Equal(SpellSourceMask.All, DebuffDefaults.SourcesFor(ActionType.CastDebuffGroup, mask));
        }

        [Fact]
        public void DebuffDefaults_leaves_the_source_mask_of_other_actions_alone() {
            Assert.Equal(SpellSourceMask.Potion, DebuffDefaults.SourcesFor(ActionType.CastSpell, SpellSourceMask.Potion));
        }
    }
}
