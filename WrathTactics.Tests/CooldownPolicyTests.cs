using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class CooldownPolicyTests {
        [Fact]
        public void a_rule_that_has_not_fired_is_allowed() {
            Assert.False(CooldownPolicy.BlockedByOncePerCombat(true, true, false, 0f, 100f));
        }

        [Fact]
        public void a_rule_that_fired_in_this_combat_is_blocked() {
            Assert.True(CooldownPolicy.BlockedByOncePerCombat(true, true, true, 130f, 100f));
        }

        [Fact]
        public void firing_in_the_combat_start_tick_counts_as_this_combat() {
            Assert.True(CooldownPolicy.BlockedByOncePerCombat(true, true, true, 100f, 100f));
        }

        [Fact]
        public void a_rule_that_fired_in_the_previous_combat_is_allowed() {
            Assert.False(CooldownPolicy.BlockedByOncePerCombat(true, true, true, 60f, 100f));
        }

        [Fact]
        public void with_the_flag_off_nothing_is_blocked() {
            Assert.False(CooldownPolicy.BlockedByOncePerCombat(false, true, true, 130f, 100f));
        }

        [Fact]
        public void out_of_combat_the_flag_has_no_effect() {
            Assert.False(CooldownPolicy.BlockedByOncePerCombat(true, false, true, 130f, 100f));
        }
    }
}
