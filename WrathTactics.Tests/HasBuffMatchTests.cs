using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class HasBuffMatchTests {
        const string FreedomOfMovementGuid = "1533e782fca42b84ea370fc1dcbf4fc1";

        [Fact]
        public void EqualGuid_Matches() {
            Assert.True(ConditionEvaluator.ContainsGuid(
                new[] { "00000000000000000000000000000000", FreedomOfMovementGuid }, FreedomOfMovementGuid));
        }

        [Fact]
        public void NullEntryInList_IsSkipped() {
            Assert.True(ConditionEvaluator.ContainsGuid(
                new string[] { null, FreedomOfMovementGuid }, FreedomOfMovementGuid));
        }

        [Fact]
        public void PartialGuid_DoesNotMatch() {
            Assert.False(ConditionEvaluator.ContainsGuid(
                new[] { FreedomOfMovementGuid }, FreedomOfMovementGuid.Substring(0, 8)));
        }

        [Fact]
        public void EmptyWanted_DoesNotMatch() {
            Assert.False(ConditionEvaluator.ContainsGuid(new[] { FreedomOfMovementGuid, "" }, ""));
        }

        [Fact]
        public void EmptyList_DoesNotMatch() {
            Assert.False(ConditionEvaluator.ContainsGuid(new string[0], FreedomOfMovementGuid));
        }
    }
}
