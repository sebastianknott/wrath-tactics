using Kingmaker.UnitLogic.Commands.Base;
using Newtonsoft.Json;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class InterruptProtectionTests {
        [Fact]
        public void unflagged_rule_is_never_protected() {
            Assert.False(InterruptProtection.IsActive(false, 100f, 101f));
        }

        [Fact]
        public void flagged_rule_is_protected_right_after_issue() {
            Assert.True(InterruptProtection.IsActive(true, 100f, 100f));
            Assert.True(InterruptProtection.IsActive(true, 100f, 103f));
        }

        [Fact]
        public void protection_ends_at_the_time_cap() {
            Assert.True(InterruptProtection.IsActive(true, 100f, 100f + InterruptProtection.MaxProtectedSeconds));
            Assert.False(InterruptProtection.IsActive(true, 100f, 100.1f + InterruptProtection.MaxProtectedSeconds));
        }

        [Fact]
        public void move_rules_and_toggles_are_blocked() {
            Assert.True(InterruptProtection.Blocks(ActionType.MoveToTarget, UnitCommand.CommandType.Move));
            Assert.True(InterruptProtection.Blocks(ActionType.CastAbility, UnitCommand.CommandType.Move));
            Assert.True(InterruptProtection.Blocks(ActionType.ToggleActivatable, null));
        }

        [Fact]
        public void swift_free_and_standard_rules_are_not_blocked_here() {
            // Standard is held back by the tracker gate, Swift/Free cannot remove a Standard command.
            Assert.False(InterruptProtection.Blocks(ActionType.CastSpell, UnitCommand.CommandType.Swift));
            Assert.False(InterruptProtection.Blocks(ActionType.SwitchWeaponSet, UnitCommand.CommandType.Free));
            Assert.False(InterruptProtection.Blocks(ActionType.CastSpell, UnitCommand.CommandType.Standard));
        }

        [Fact]
        public void resolve_reports_the_active_index() {
            var globals = new System.Collections.Generic.List<TacticsRule> { new TacticsRule { Id = "a" }, new TacticsRule { Id = "b" } };
            var chars = new System.Collections.Generic.List<TacticsRule>();
            Assert.Equal(1, ActiveRuleTracker.Resolve(RuleListSource.Global, "b", globals, chars).ActiveIndex);
            Assert.Equal(-1, ActiveRuleTracker.Resolve(RuleListSource.Global, "zz", globals, chars).ActiveIndex);
        }

        [Fact]
        public void json_field_defaults_to_false_and_round_trips() {
            Assert.False(JsonConvert.DeserializeObject<TacticsRule>("{}").Uninterruptible);
            var rule = JsonConvert.DeserializeObject<TacticsRule>("{\"Uninterruptible\":true}");
            Assert.True(rule.Uninterruptible);
            Assert.Contains("\"Uninterruptible\":true", JsonConvert.SerializeObject(rule));
        }
    }
}
