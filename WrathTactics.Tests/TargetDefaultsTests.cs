using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    // Fresh rules start on Self; offensive actions flip that to a threat-based selector.
    // An explicit non-Self choice is never touched.
    public class TargetDefaultsTests {
        [Theory]
        [InlineData(ActionType.AttackTarget, TargetType.EnemyHighestThreat)]
        [InlineData(ActionType.ThrowSplash,  TargetType.EnemyHighestThreat)]
        [InlineData(ActionType.CastDebuffGroup, TargetType.EnemyHighestThreat)]
        [InlineData(ActionType.CastConditionOption, TargetType.ConditionTarget)]
        [InlineData(ActionType.RemoveCondition, TargetType.ConditionTarget)]
        [InlineData(ActionType.MoveToTarget, TargetType.EnemyNearest)]
        [InlineData(ActionType.MoveAwayFromTarget, TargetType.EnemyNearest)]
        public void ForAction_replaces_Self_for_offensive_actions(ActionType action, TargetType expected) {
            Assert.Equal(expected, TargetDefaults.ForAction(action, TargetType.Self));
        }

        [Theory]
        [InlineData(ActionType.CastSpell)]
        [InlineData(ActionType.CastAbility)]
        [InlineData(ActionType.UseItem)]
        [InlineData(ActionType.ToggleActivatable)]
        [InlineData(ActionType.Heal)]
        [InlineData(ActionType.DoNothing)]
        [InlineData(ActionType.SwitchWeaponSet)]
        public void ForAction_keeps_Self_for_non_offensive_actions(ActionType action) {
            Assert.Null(TargetDefaults.ForAction(action, TargetType.Self));
        }

        [Theory]
        [InlineData(TargetType.EnemyNearest)]
        [InlineData(TargetType.AllyLowestHp)]
        [InlineData(TargetType.ConditionTarget)]
        public void ForAction_never_overrides_an_explicit_choice(TargetType current) {
            Assert.Null(TargetDefaults.ForAction(ActionType.AttackTarget, current));
        }

        [Fact]
        public void MoveBracket_flee_starts_on_Short_approach_keeps_Melee() {
            Assert.Equal(RangeBracket.Short, TargetDefaults.MoveBracketForAction(ActionType.MoveAwayFromTarget, RangeBracket.Melee));
            Assert.Null(TargetDefaults.MoveBracketForAction(ActionType.MoveToTarget, RangeBracket.Melee));
            Assert.Null(TargetDefaults.MoveBracketForAction(ActionType.MoveAwayFromTarget, RangeBracket.Long));
        }

        [Fact]
        public void ForAbility_enemy_only_ability_gets_threat_selector() {
            Assert.Equal(TargetType.EnemyHighestThreat,
                TargetDefaults.ForAbility(TargetType.Self, canTargetEnemies: true, canTargetFriends: false, canTargetSelf: false));
        }

        [Theory]
        [InlineData(true,  true,  false)] // hits both sides (e.g. Fireball) — ambiguous
        [InlineData(true,  false, true)]  // enemies or self
        [InlineData(false, true,  true)]  // buff
        [InlineData(false, false, true)]  // self-only
        public void ForAbility_keeps_Self_unless_enemy_only(bool enemies, bool friends, bool self) {
            Assert.Null(TargetDefaults.ForAbility(TargetType.Self, enemies, friends, self));
        }

        [Fact]
        public void ForAbility_never_overrides_an_explicit_choice() {
            Assert.Null(TargetDefaults.ForAbility(TargetType.AllyLowestHp, canTargetEnemies: true, canTargetFriends: false, canTargetSelf: false));
        }
    
        [Theory]
        [InlineData(TargetType.EnemyHighestThreat)]
        [InlineData(TargetType.EnemyNearest)]
        [InlineData(TargetType.EnemyMostEnemyNeighbors)]
        public void ForHealFromDebuff_replaces_enemy_selectors_with_Self(TargetType current) {
            Assert.Equal(TargetType.Self, TargetDefaults.ForHealFromDebuff(current));
        }

        [Theory]
        [InlineData(TargetType.Self)]
        [InlineData(TargetType.AllyLowestHp)]
        [InlineData(TargetType.ConditionTarget)]
        [InlineData(TargetType.SpecificAlly)]
        public void ForHealFromDebuff_keeps_ally_and_condition_targets(TargetType current) {
            Assert.Null(TargetDefaults.ForHealFromDebuff(current));
        }
    }
}
