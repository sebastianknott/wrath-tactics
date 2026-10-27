using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    [Collection("CleanseLatch")]
    public class CleanseLatchTests {
        public CleanseLatchTests() { CleanseAttemptLatch.Clear(); }

        [Fact]
        public void Blocked_within_window() {
            CleanseAttemptLatch.Record("ally", AfflictionKinds.Poison, "remedy", 100, inCombat: true);
            Assert.True(CleanseAttemptLatch.IsBlocked("ally", AfflictionKinds.Poison, "remedy", 100));
            Assert.True(CleanseAttemptLatch.IsBlocked("ally", AfflictionKinds.Poison, "remedy", 105.9));
        }

        [Fact]
        public void Free_after_expiry() {
            CleanseAttemptLatch.Record("ally", AfflictionKinds.Poison, "remedy", 100, inCombat: true);
            Assert.False(CleanseAttemptLatch.IsBlocked("ally", AfflictionKinds.Poison, "remedy", 106));
            // Pruned on read: still free afterwards, also for an earlier clock reading.
            Assert.False(CleanseAttemptLatch.IsBlocked("ally", AfflictionKinds.Poison, "remedy", 101));
        }

        [Fact]
        public void Idle_window_is_longer_than_combat_window() {
            CleanseAttemptLatch.Record("ally", AfflictionKinds.Curse, "remedy", 0, inCombat: false);
            Assert.True(CleanseAttemptLatch.IsBlocked("ally", AfflictionKinds.Curse, "remedy", 59));
            Assert.False(CleanseAttemptLatch.IsBlocked("ally", AfflictionKinds.Curse, "remedy", 60));
            Assert.Equal(6.0, CleanseAttemptLatch.WindowSeconds(true));
            Assert.Equal(60.0, CleanseAttemptLatch.WindowSeconds(false));
        }

        [Fact]
        public void Other_remedy_not_blocked() {
            CleanseAttemptLatch.Record("ally", AfflictionKinds.Poison, "remedy", 100, inCombat: true);
            Assert.False(CleanseAttemptLatch.IsBlocked("ally", AfflictionKinds.Poison, "other", 102));
        }

        [Fact]
        public void Other_remedy_blocked_during_grace() {
            CleanseAttemptLatch.Record("ally", AfflictionKinds.Poison, "remedy", 100, inCombat: true);
            Assert.True(CleanseAttemptLatch.IsBlocked("ally", AfflictionKinds.Poison, "other", 101.9));
            Assert.False(CleanseAttemptLatch.IsBlocked("ally", AfflictionKinds.Poison, "other", 102));
            Assert.Equal(2.0, CleanseAttemptLatch.GraceSeconds);
        }

        [Fact]
        public void Grace_is_per_ally_and_kind() {
            CleanseAttemptLatch.Record("ally", AfflictionKinds.Poison, "remedy", 100, inCombat: true);
            Assert.False(CleanseAttemptLatch.IsBlocked("buddy", AfflictionKinds.Poison, "other", 101));
            Assert.False(CleanseAttemptLatch.IsBlocked("ally", AfflictionKinds.Curse, "other", 101));
        }

        [Fact]
        public void Other_ally_not_blocked() {
            CleanseAttemptLatch.Record("ally", AfflictionKinds.Poison, "remedy", 100, inCombat: true);
            Assert.False(CleanseAttemptLatch.IsBlocked("buddy", AfflictionKinds.Poison, "remedy", 101));
        }

        [Fact]
        public void Other_kind_not_blocked() {
            CleanseAttemptLatch.Record("ally", AfflictionKinds.Poison, "remedy", 100, inCombat: true);
            Assert.False(CleanseAttemptLatch.IsBlocked("ally", AfflictionKinds.Curse, "remedy", 101));
        }

        [Fact]
        public void Record_latches_every_bit_of_the_mask() {
            CleanseAttemptLatch.Record("ally", AfflictionKinds.Poison | AfflictionKinds.Curse, "remedy", 100, inCombat: true);
            Assert.True(CleanseAttemptLatch.IsBlocked("ally", AfflictionKinds.Poison, "remedy", 101));
            Assert.True(CleanseAttemptLatch.IsBlocked("ally", AfflictionKinds.Curse, "remedy", 101));
        }

        [Fact]
        public void Unblocked_returns_the_bits_still_free() {
            CleanseAttemptLatch.Record("ally", AfflictionKinds.Poison, "remedy", 100, inCombat: true);
            var free = CleanseAttemptLatch.Unblocked("ally", AfflictionKinds.Poison | AfflictionKinds.Curse, "remedy", 101);
            Assert.Equal(AfflictionKinds.Curse, free);
        }

        [Fact]
        public void Ally_id_with_separator_chars_does_not_collide() {
            CleanseAttemptLatch.Record("a|1", AfflictionKinds.Poison, "r", 100, inCombat: true);
            Assert.False(CleanseAttemptLatch.IsBlocked("a", AfflictionKinds.Poison, "1|r", 101));
        }
    }
}
