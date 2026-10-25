using System.Linq;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    // Cleanse core (WTW-30): descriptor/condition table, profile aggregation, editor default.
    public class CleanseTests {
        static CleanseEffect E(AfflictionKinds kinds, bool suppress = false, params string[] gate) {
            return new CleanseEffect { Kinds = kinds, Suppress = suppress, GateFacts = gate };
        }

        // ---- table ----

        [Theory]
        [InlineData(SpellDescriptor.Paralysis, AfflictionKinds.Paralysis)]
        [InlineData(SpellDescriptor.Confusion, AfflictionKinds.Confusion)]
        [InlineData(SpellDescriptor.Stun, AfflictionKinds.StunDaze)]
        [InlineData(SpellDescriptor.Daze, AfflictionKinds.StunDaze)]
        [InlineData(SpellDescriptor.Staggered, AfflictionKinds.StunDaze)]
        [InlineData(SpellDescriptor.Fear, AfflictionKinds.FearShaken)]
        [InlineData(SpellDescriptor.Shaken, AfflictionKinds.FearShaken)]
        [InlineData(SpellDescriptor.Frightened, AfflictionKinds.FearShaken)]
        [InlineData(SpellDescriptor.Nauseated, AfflictionKinds.NauseaSickened)]
        [InlineData(SpellDescriptor.Sickened, AfflictionKinds.NauseaSickened)]
        [InlineData(SpellDescriptor.Blindness, AfflictionKinds.Blindness)]
        [InlineData(SpellDescriptor.Poison, AfflictionKinds.Poison)]
        [InlineData(SpellDescriptor.Disease, AfflictionKinds.Disease)]
        [InlineData(SpellDescriptor.Curse, AfflictionKinds.Curse)]
        [InlineData(SpellDescriptor.Fatigue, AfflictionKinds.FatigueExhausted)]
        [InlineData(SpellDescriptor.Exhausted, AfflictionKinds.FatigueExhausted)]
        [InlineData(SpellDescriptor.StatDebuff, AfflictionKinds.StatDebuffAbilityDamage)]
        [InlineData(SpellDescriptor.NegativeLevel, AfflictionKinds.NegativeLevel)]
        [InlineData(SpellDescriptor.Petrified, AfflictionKinds.Petrified)]
        [InlineData(SpellDescriptor.MovementImpairing, AfflictionKinds.SlowedEntangled)]
        public void Descriptor_maps_to_its_kind(SpellDescriptor d, AfflictionKinds expected) {
            Assert.Equal(expected, CleanseTable.KindsFromDescriptor(d));
        }

        [Fact]
        public void Polymorph_buff_is_not_harmful() {
            Assert.Equal(AfflictionKinds.None, CleanseTable.KindsFromDescriptor(SpellDescriptor.Polymorph));
            Assert.Equal(AfflictionKinds.None,
                CleanseTable.KindsFromDescriptor(SpellDescriptor.Polymorph | SpellDescriptor.Arcane | SpellDescriptor.Charm | SpellDescriptor.Sleep));
        }

        [Fact]
        public void Mixed_descriptor_mask_gives_the_union() {
            Assert.Equal(AfflictionKinds.Poison | AfflictionKinds.Curse,
                CleanseTable.KindsFromDescriptor(SpellDescriptor.Poison | SpellDescriptor.Curse | SpellDescriptor.Fire));
        }

        [Theory]
        [InlineData(UnitCondition.Paralyzed, AfflictionKinds.Paralysis)]
        [InlineData(UnitCondition.Confusion, AfflictionKinds.Confusion)]
        [InlineData(UnitCondition.Stunned, AfflictionKinds.StunDaze)]
        [InlineData(UnitCondition.Dazed, AfflictionKinds.StunDaze)]
        [InlineData(UnitCondition.Shaken, AfflictionKinds.FearShaken)]
        [InlineData(UnitCondition.Frightened, AfflictionKinds.FearShaken)]
        [InlineData(UnitCondition.Nauseated, AfflictionKinds.NauseaSickened)]
        [InlineData(UnitCondition.Blindness, AfflictionKinds.Blindness)]
        [InlineData(UnitCondition.Fatigued, AfflictionKinds.FatigueExhausted)]
        [InlineData(UnitCondition.Petrified, AfflictionKinds.Petrified)]
        [InlineData(UnitCondition.Slowed, AfflictionKinds.SlowedEntangled)]
        [InlineData(UnitCondition.Entangled, AfflictionKinds.SlowedEntangled)]
        [InlineData(UnitCondition.Invisible, AfflictionKinds.None)]
        [InlineData(UnitCondition.BarbarianRage, AfflictionKinds.None)]
        [InlineData(UnitCondition.Prone, AfflictionKinds.None)]
        public void Condition_maps_to_its_kind(UnitCondition c, AfflictionKinds expected) {
            Assert.Equal(expected, CleanseTable.KindsFromCondition(c));
        }

        [Fact]
        public void Bits_lists_single_flags_in_order() {
            var bits = CleanseTable.Bits(AfflictionKinds.Curse | AfflictionKinds.Paralysis).Select(b => (int)b).ToList();
            Assert.Equal(new[] { (int)AfflictionKinds.Paralysis, (int)AfflictionKinds.Curse }, bits);
            Assert.Equal(2, CleanseTable.Count(AfflictionKinds.Curse | AfflictionKinds.Paralysis));
            Assert.Empty(CleanseTable.Bits(AfflictionKinds.None));
        }

        // ---- profile (synthetic effects stand in for blueprints) ----

        [Fact]
        public void Heal_covers_ten_kinds() {
            var p = new CleanseProfile { HealsHp = true };
            foreach (var d in new[] {
                SpellDescriptor.Blindness, SpellDescriptor.Disease, SpellDescriptor.Poison, SpellDescriptor.Fatigue,
                SpellDescriptor.Nauseated, SpellDescriptor.Sickened, SpellDescriptor.Stun, SpellDescriptor.Confusion,
                SpellDescriptor.StatDebuff, SpellDescriptor.NegativeLevel, SpellDescriptor.Paralysis })
                p.Add(E(CleanseTable.KindsFromDescriptor(d)));
            Assert.Equal(10, CleanseTable.Count(p.Covers(null)));
        }

        [Fact]
        public void Greater_restoration_covers_four_kinds() {
            var p = new CleanseProfile();
            p.Add(E(AfflictionKinds.FatigueExhausted));
            p.Add(E(AfflictionKinds.StatDebuffAbilityDamage));
            p.Add(E(AfflictionKinds.NegativeLevel));
            p.Add(E(AfflictionKinds.Curse));
            Assert.Equal(4, CleanseTable.Count(p.Covers(null)));
            Assert.Equal(CleanseRanking.TierNormal, p.Tier);
        }

        [Fact]
        public void Remove_paralysis_covers_one_kind() {
            var p = new CleanseProfile();
            p.Add(E(AfflictionKinds.Paralysis));
            Assert.Equal(AfflictionKinds.Paralysis, p.Covers(null));
            Assert.True(p.IsCleanse);
        }

        [Fact]
        public void Effect_without_kinds_is_ignored() {
            var p = new CleanseProfile();
            p.Add(E(AfflictionKinds.None));
            Assert.False(p.IsCleanse);
        }

        [Fact]
        public void Suppressor_only_profile_is_tier_one() {
            var p = new CleanseProfile();
            p.Add(E(AfflictionKinds.SlowedEntangled, suppress: true));
            Assert.Equal(CleanseRanking.TierSuppressor, p.Tier);
            Assert.Equal(AfflictionKinds.None, p.Removes(null));
            Assert.Equal(AfflictionKinds.SlowedEntangled, p.Covers(null));
        }

        [Fact]
        public void A_removal_next_to_a_suppressor_keeps_tier_zero() {
            var p = new CleanseProfile();
            p.Add(E(AfflictionKinds.Poison, suppress: true));
            p.Add(E(AfflictionKinds.Poison));
            Assert.Equal(CleanseRanking.TierNormal, p.Tier);
        }

        [Fact]
        public void Hp_heal_is_tier_two_even_with_removals() {
            var p = new CleanseProfile { HealsHp = true };
            p.Add(E(AfflictionKinds.Poison));
            Assert.Equal(CleanseRanking.TierHeal, p.Tier);
        }

        [Theory]
        [InlineData(AbilityType.Spell, true)]
        [InlineData(AbilityType.Supernatural, false)]
        [InlineData(AbilityType.Extraordinary, false)]
        [InlineData(AbilityType.SpellLike, false)]
        [InlineData(AbilityType.Special, false)]
        public void Only_spell_hp_heals_are_the_last_resort(AbilityType type, bool expected) {
            Assert.Equal(expected, CleanseClassifier.IsHealSpellType(type));
        }

        [Fact]
        public void Gated_effect_needs_the_caster_fact() {
            var p = new CleanseProfile();
            p.Add(E(AfflictionKinds.Poison));
            p.Add(E(AfflictionKinds.Curse, false, "mercy-guid"));
            Assert.Equal(AfflictionKinds.Poison, p.Covers(null));
            Assert.Equal(AfflictionKinds.Poison, p.Covers(f => false));
            Assert.Equal(AfflictionKinds.Poison | AfflictionKinds.Curse, p.Covers(f => f == "mercy-guid"));
        }

        [Fact]
        public void Gate_with_several_facts_needs_all() {
            var p = new CleanseProfile();
            p.Add(E(AfflictionKinds.Curse, false, "a", "b"));
            Assert.Equal(AfflictionKinds.None, p.Covers(f => f == "a"));
            Assert.Equal(AfflictionKinds.Curse, p.Covers(f => f == "a" || f == "b"));
        }

        [Fact]
        public void EffectFor_prefers_removal_then_the_sure_check() {
            var p = new CleanseProfile();
            var suppress = E(AfflictionKinds.Poison, suppress: true);
            var dice = new CleanseEffect { Kinds = AfflictionKinds.Poison, Check = CleanseCheckType.DC };
            var sure = new CleanseEffect { Kinds = AfflictionKinds.Poison, Check = CleanseCheckType.None };
            p.Add(suppress); p.Add(dice);
            Assert.Same(dice, p.EffectFor(AfflictionKinds.Poison, null));
            p.Add(sure);
            Assert.Same(sure, p.EffectFor(AfflictionKinds.Poison, null));
            Assert.Null(p.EffectFor(AfflictionKinds.Curse, null));
        }

        [Fact]
        public void NotACleanse_is_empty() {
            Assert.False(CleanseProfile.NotACleanse.IsCleanse);
            Assert.Equal(AfflictionKinds.None, CleanseProfile.NotACleanse.Covers(null));
        }

        [Fact]
        public void Null_blueprint_classifies_as_not_a_cleanse() {
            Assert.Same(CleanseProfile.NotACleanse, CleanseClassifier.Get(null));
        }

        // ---- editor default ----

        [Fact]
        public void Kinds_none_becomes_all_on_switch() {
            Assert.Equal(CleanseDefaults.All, CleanseDefaults.KindsFor(ActionType.RemoveCondition, AfflictionKinds.None));
            Assert.Equal(14, CleanseTable.Count(CleanseDefaults.All));
        }

        [Fact]
        public void Explicit_kinds_are_kept() {
            var chosen = AfflictionKinds.Poison | AfflictionKinds.Curse;
            Assert.Equal(chosen, CleanseDefaults.KindsFor(ActionType.RemoveCondition, chosen));
        }

        [Theory]
        [InlineData(ActionType.CastSpell)]
        [InlineData(ActionType.Heal)]
        [InlineData(ActionType.CastDebuffGroup)]
        public void Other_action_leaves_kinds(ActionType action) {
            Assert.Equal(AfflictionKinds.None, CleanseDefaults.KindsFor(action, AfflictionKinds.None));
            Assert.Equal(AfflictionKinds.Poison, CleanseDefaults.KindsFor(action, AfflictionKinds.Poison));
        }

        // ---- ally affliction mask (HasAffliction) ----

        static BuffReading Buff(AfflictionKinds k, bool self = false, bool ally = false, bool poly = false, bool prot = false) {
            return new BuffReading { Kinds = k, SelfCast = self, AllySourced = ally, Polymorph = poly, Protective = prot };
        }

        [Fact]
        public void Ally_mask_reads_paralysis() {
            var mask = CleanseAfflictionReader.Mask(new[] { Buff(AfflictionKinds.Paralysis), Buff(AfflictionKinds.Poison) },
                AfflictionKinds.Blindness);
            Assert.Equal(AfflictionKinds.Paralysis | AfflictionKinds.Poison | AfflictionKinds.Blindness, mask);
            Assert.True(CleanseAfflictionReader.Matches(mask, "Paralysis", ConditionOperator.Equal));
            Assert.False(CleanseAfflictionReader.Matches(mask, "Paralysis", ConditionOperator.NotEqual));
            Assert.True(CleanseAfflictionReader.Matches(mask, "Curse", ConditionOperator.NotEqual));
            Assert.True(CleanseAfflictionReader.Matches(mask, "Any", ConditionOperator.Equal));
        }

        [Fact]
        public void Self_cast_buff_is_ignored() {
            var mask = CleanseAfflictionReader.Mask(new[] { Buff(AfflictionKinds.SlowedEntangled, self: true) }, AfflictionKinds.None);
            Assert.Equal(AfflictionKinds.None, mask);
            Assert.False(CleanseAfflictionReader.Matches(mask, "Any", ConditionOperator.Equal));
        }

        [Fact]
        public void Ally_sourced_polymorph_and_protective_buffs_are_ignored() {
            var mask = CleanseAfflictionReader.Mask(new[] {
                Buff(AfflictionKinds.Curse, ally: true),
                Buff(AfflictionKinds.StunDaze, poly: true),
                Buff(AfflictionKinds.SlowedEntangled, prot: true),
            }, AfflictionKinds.None);
            Assert.Equal(AfflictionKinds.None, mask);
        }

        [Fact]
        public void Unknown_value_never_matches_under_equal() {
            Assert.False(CleanseAfflictionReader.Matches(AfflictionKinds.Poison, "Bogus", ConditionOperator.Equal));
            Assert.True(CleanseAfflictionReader.Matches(AfflictionKinds.Poison, "None", ConditionOperator.NotEqual));
        }

        [Fact]
        public void All_kinds_covers_every_defined_bit() {
            Assert.Equal(AfflictionKinds.SlowedEntangled | (AfflictionKinds)((1 << 13) - 1), CleanseAfflictionReader.AllKinds);
        }

        [Fact]
        public void Pin_index_39() {
            // pinned per branch: 39 on the topic branches, 44 on patched (local-fixups.sh rewrites this).
            Assert.Equal(40, (int)ConditionProperty.HasAffliction);
        }

        [Fact]
        public void Enum_indices_are_pinned() {
            Assert.Equal(13, (int)ActionType.RemoveCondition);
            Assert.Equal(1, (int)AfflictionKinds.Paralysis);
            Assert.Equal(1 << 13, (int)AfflictionKinds.SlowedEntangled);
        }
    }
}
