using Kingmaker.Blueprints.Classes.Spells;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class AbilityGroupsTests {
        [Theory]
        [InlineData("group:Healing", true, AbilityGroup.Healing)]
        [InlineData("group:Control", true, AbilityGroup.Control)]
        [InlineData("group:Damage", true, AbilityGroup.Damage)]
        [InlineData("group:Nope", false, AbilityGroup.None)]
        [InlineData("aaaa0000000000000000000000000001", false, AbilityGroup.None)]
        [InlineData("", false, AbilityGroup.None)]
        [InlineData(null, false, AbilityGroup.None)]
        public void TryParse_reads_known_keys_only(string filter, bool ok, AbilityGroup expected) {
            Assert.Equal(ok, AbilityGroups.TryParse(filter, out var group));
            Assert.Equal(expected, group);
        }

        [Theory]
        [InlineData(AbilityGroup.Healing, "group:Healing")]
        [InlineData(AbilityGroup.Control, "group:Control")]
        [InlineData(AbilityGroup.Damage, "group:Damage")]
        public void ToFilter_round_trips(AbilityGroup group, string text) {
            Assert.Equal(text, AbilityGroups.ToFilter(group));
            Assert.True(AbilityGroups.TryParse(text, out var back));
            Assert.Equal(group, back);
        }

        [Fact]
        public void IsGroupFilter_requires_prefix() {
            Assert.True(AbilityGroups.IsGroupFilter("group:Healing"));
            Assert.False(AbilityGroups.IsGroupFilter("Healing"));
            Assert.False(AbilityGroups.IsGroupFilter(""));
            Assert.False(AbilityGroups.IsGroupFilter(null));
        }

        [Fact]
        public void Positive_heal_keyword_gives_Healing() {
            Assert.Equal(AbilityGroup.Healing, AbilityGroups.Classify(true, SpellDescriptor.None));
        }

        [Theory]
        [InlineData(SpellDescriptor.RestoreHP)]
        [InlineData(SpellDescriptor.ChannelPositiveHeal)]
        public void Heal_descriptors_give_Healing(SpellDescriptor d) {
            Assert.Equal(AbilityGroup.Healing, AbilityGroups.Classify(false, d));
        }

        [Theory]
        [InlineData(SpellDescriptor.Paralysis)]
        [InlineData(SpellDescriptor.Stun)]
        [InlineData(SpellDescriptor.Sleep)]
        [InlineData(SpellDescriptor.Daze)]
        [InlineData(SpellDescriptor.Fear)]
        [InlineData(SpellDescriptor.Compulsion)]
        [InlineData(SpellDescriptor.Charm)]
        [InlineData(SpellDescriptor.MovementImpairing)]
        public void Control_descriptors_give_Control(SpellDescriptor d) {
            Assert.Equal(AbilityGroup.Control, AbilityGroups.Classify(false, d));
        }

        [Theory]
        [InlineData(SpellDescriptor.Fire)]
        [InlineData(SpellDescriptor.Cold)]
        [InlineData(SpellDescriptor.Electricity)]
        [InlineData(SpellDescriptor.Acid)]
        [InlineData(SpellDescriptor.Sonic)]
        public void Energy_descriptors_give_Damage(SpellDescriptor d) {
            Assert.Equal(AbilityGroup.Damage, AbilityGroups.Classify(false, d));
        }

        [Fact]
        public void Unrelated_descriptor_gives_no_group() {
            Assert.Equal(AbilityGroup.None, AbilityGroups.Classify(false, SpellDescriptor.Poison | SpellDescriptor.Evil));
            Assert.Equal(AbilityGroup.None, AbilityGroups.Classify(false, SpellDescriptor.None));
        }

        [Fact]
        public void Spell_can_be_in_several_groups() {
            Assert.Equal(AbilityGroup.Control | AbilityGroup.Damage,
                AbilityGroups.Classify(false, SpellDescriptor.Cold | SpellDescriptor.MovementImpairing));
        }

        [Theory]
        [InlineData("CureLightWounds", "Cure Light Wounds", HealEnergyType.Positive)]
        [InlineData("LayOnHands", "Lay on Hands", HealEnergyType.Positive)]
        [InlineData("", "Wunden heilen", HealEnergyType.Positive)]
        [InlineData("InflictLightWounds", "", HealEnergyType.Negative)]
        [InlineData("Fireball", "Feuerball", HealEnergyType.None)]
        [InlineData(null, null, HealEnergyType.None)]
        public void ClassifyHealByName_uses_both_names(string internalName, string displayName, HealEnergyType expected) {
            Assert.Equal(expected, ActionValidator.ClassifyHealByName(internalName, displayName));
        }
    }
}
