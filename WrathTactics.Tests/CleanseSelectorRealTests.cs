using System;
using System.Linq;
using Kingmaker.Designers.EventConditionActionSystem.Conditions;
using Kingmaker.ElementsSystem;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Conditions;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    // PM decisions a and b of WTW-30 cleanse WP3c, checked against the game's real blueprints (skipped when absent).
    public class CleanseSelectorRealTests {
        const string SlowBuff = "0bc608c3f2b548b44b7146b7530613ac";
        const string BaneBuff = "9277a66b27a849145a0ff5e8513c7921";

        static BlueprintAbility Ability(string path) { return RealBlueprints.ByPath<BlueprintAbility>(path + ".jbp"); }

        static CleanseOptionInput Input(string name, BlueprintAbility bp) {
            return new CleanseOptionInput { Key = name, Name = name, Profile = CleanseClassifier.Get(bp), Grade = 3, IsSlot = true };
        }

        static CleanseSituation Sit(AfflictionKinds mask, params string[] allyBuffs) {
            return new CleanseSituation {
                AllyId = Guid.NewGuid().ToString(), AllyMask = mask, RuleKinds = ConditionAll, NowSeconds = 50, InCombat = true,
                AllyHasBuff = g => allyBuffs.Contains(g),
            };
        }

        const AfflictionKinds ConditionAll = (AfflictionKinds)((1 << 14) - 1);

        // ---- decision a: counter-removal over-coverage ----

        [RealBlueprintFact]
        public void Haste_records_the_exact_slow_buff_it_removes() {
            var p = CleanseClassifier.Get(Ability("Spells/Level3/Haste"));
            Assert.Contains(p.Effects, e => e.RemovedBuffGuids.Contains(SlowBuff));
        }

        [RealBlueprintFact]
        public void Haste_is_not_chosen_for_a_dazed_ally_without_slow() {
            var haste = Input("haste", Ability("Spells/Level3/Haste"));
            var dazed = CleanseSelector.Select(new[] { haste }, Sit(AfflictionKinds.StunDaze));
            Assert.Equal(-1, dazed.Index);
            Assert.Contains(dazed.Rejected, r => r.Contains("haste") && r.Contains("no-match"));
        }

        [RealBlueprintFact]
        public void Haste_is_chosen_for_an_ally_that_really_has_slow() {
            var haste = Input("haste", Ability("Spells/Level3/Haste"));
            var slowed = CleanseSelector.Select(new[] { haste }, Sit(AfflictionKinds.StunDaze | AfflictionKinds.SlowedEntangled, SlowBuff));
            Assert.Equal(0, slowed.Index);
        }

        [RealBlueprintFact]
        public void Bless_only_counts_against_an_ally_that_has_bane() {
            var bless = Input("bless", Ability("Spells/Level1/Bless"));
            var p = bless.Profile;
            Assert.Contains(p.Effects, e => e.RemovedBuffGuids.Contains(BaneBuff));
            Assert.Equal(-1, CleanseSelector.Select(new[] { bless }, Sit(AfflictionKinds.FearShaken)).Index);
            Assert.Equal(0, CleanseSelector.Select(new[] { bless }, Sit(AfflictionKinds.FearShaken, BaneBuff)).Index);
        }

        // ---- decision b: dual-purpose damage + cure abilities ----

        [RealBlueprintTheory]
        [InlineData("Mythic/Angel/AngelSpells/AngelCleansingFlames")]
        [InlineData("Mythic/Azata/AzataSpells/WaterTorrent")]
        public void Dual_purpose_ability_with_an_ally_branch_cure_that_can_target_friends_is_usable(string path) {
            var bp = Ability(path);
            Assert.True(CleanseTargeting.AllyTargetableByFlags(bp, allyIsOwner: false));
            Assert.True(CleanseClassifier.Get(bp).IsCleanse, "the cure sits on the branch an ally takes");
        }

        [RealBlueprintFact]
        public void Golden_dragon_breath_cannot_target_a_friendly_unit() {
            var bp = Ability("Mythic/GoldenDragon/GoldenDragonBreathAbility");
            Assert.False(CleanseTargeting.AllyTargetableByFlags(bp, allyIsOwner: false));
            Assert.False(CleanseTargeting.AllyTargetableByFlags(bp, allyIsOwner: true));
        }

        [RealBlueprintFact]
        public void Ordinary_touch_cure_targets_friends_and_self() {
            var bp = Ability("Spells/Level4/Restoration");
            Assert.True(CleanseTargeting.AllyTargetableByFlags(bp, false));
            Assert.True(CleanseTargeting.AllyTargetableByFlags(bp, true));
        }

        // IL: AbilityData.IsAvailable -> IsAvailableForCast -> CanBeCastByCaster rejects a player-faction caster
        // without the material (RequireMaterialComponent && !HasEnoughMaterialComponent). The blueprint carries it.
        [RealBlueprintFact]
        public void Restoration_declares_its_diamond_dust_material_component() {
            var bp = Ability("Spells/Level4/Restoration");
            Assert.NotNull(bp.MaterialComponent);
            Assert.NotNull(bp.MaterialComponent.Item);
            Assert.True(bp.MaterialComponent.Count > 0);
        }
    }

    // Which branch of a Conditional an ally can take (decision b: the cure branch must apply to allies).
    public class CleanseAllySideTests {
        static ConditionsChecker Checker(Operation op, params Kingmaker.ElementsSystem.Condition[] conditions) {
            return new ConditionsChecker { Operation = op, Conditions = conditions };
        }

        static Kingmaker.ElementsSystem.Condition IsAlly(bool not) { return new ContextConditionIsAlly { Not = not }; }
        static Kingmaker.ElementsSystem.Condition IsEnemy(bool not) { return new ContextConditionIsEnemy { Not = not }; }

        [Fact]
        public void Lone_ally_check_sends_allies_into_the_true_branch_only() {
            Assert.Equal(CleanseClassifier.AllySide.TrueOnly, CleanseClassifier.AllySideOf(Checker(Operation.And, IsAlly(false))));
            Assert.Equal(CleanseClassifier.AllySide.TrueOnly, CleanseClassifier.AllySideOf(Checker(Operation.And, IsEnemy(true))));
        }

        [Fact]
        public void Lone_enemy_check_sends_allies_into_the_false_branch_only() {
            Assert.Equal(CleanseClassifier.AllySide.FalseOnly, CleanseClassifier.AllySideOf(Checker(Operation.And, IsEnemy(false))));
            Assert.Equal(CleanseClassifier.AllySide.FalseOnly, CleanseClassifier.AllySideOf(Checker(Operation.And, IsAlly(true))));
        }

        [Fact]
        public void An_enemy_only_member_of_an_AND_checker_blocks_the_true_branch() {
            var c = Checker(Operation.And, IsAlly(true), new ContextConditionCasterHasFact());
            Assert.Equal(CleanseClassifier.AllySide.FalseOnly, CleanseClassifier.AllySideOf(c));
        }

        [Fact]
        public void An_ally_check_beside_other_conditions_leaves_both_branches_open() {
            var c = Checker(Operation.And, IsAlly(false), new ContextConditionCasterHasFact());
            Assert.Equal(CleanseClassifier.AllySide.Both, CleanseClassifier.AllySideOf(c));
        }

        [Fact]
        public void Unrelated_or_OR_checkers_leave_both_branches_open() {
            Assert.Equal(CleanseClassifier.AllySide.Both, CleanseClassifier.AllySideOf(Checker(Operation.And, new ContextConditionCasterHasFact())));
            Assert.Equal(CleanseClassifier.AllySide.Both, CleanseClassifier.AllySideOf(Checker(Operation.Or, IsAlly(false), IsEnemy(false))));
            Assert.Equal(CleanseClassifier.AllySide.Both, CleanseClassifier.AllySideOf(null));
        }
    }
}
