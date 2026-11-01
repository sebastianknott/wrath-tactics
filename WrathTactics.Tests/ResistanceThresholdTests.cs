using System.Collections.Generic;
using Kingmaker.Enums.Damage;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class ResistanceThresholdTests {
        private static int Highest(DamageEnergyType energy, params (DamageEnergyType, int)[] sources)
            => ConditionEvaluator.HighestEnergyResistance(sources, energy);

        [Fact]
        public void NoSources_ReturnsZero() {
            Assert.Equal(0, Highest(DamageEnergyType.Fire));
        }

        [Fact]
        public void SingleMatchingSource_ReturnsItsValue() {
            Assert.Equal(10, Highest(DamageEnergyType.Fire, (DamageEnergyType.Fire, 10)));
        }

        [Fact]
        public void OtherEnergy_IsIgnored() {
            Assert.Equal(0, Highest(DamageEnergyType.Fire, (DamageEnergyType.Cold, 20)));
        }

        [Fact]
        public void MultipleMatchingSources_ReturnMaxNotSum() {
            Assert.Equal(10, Highest(DamageEnergyType.Fire,
                (DamageEnergyType.Fire, 5), (DamageEnergyType.Fire, 10), (DamageEnergyType.Fire, 3)));
        }

        [Fact]
        public void ZeroValueSource_ReturnsZero() {
            Assert.Equal(0, Highest(DamageEnergyType.Fire, (DamageEnergyType.Fire, 0)));
        }

        static Condition Cond(ConditionOperator op, string value2) =>
            new Condition { Subject = ConditionSubject.Enemy, Property = ConditionProperty.HasEnergyResistance,
                            Operator = op, Value = "Fire", Value2 = value2 };

        [Theory]
        [InlineData(10, ConditionOperator.GreaterOrEqual, "10", true)]
        [InlineData(10, ConditionOperator.GreaterOrEqual, "20", false)]
        [InlineData(25, ConditionOperator.GreaterOrEqual, "20", true)]
        [InlineData(0, ConditionOperator.Equal, "0", true)]
        [InlineData(10, ConditionOperator.Equal, "0", false)]
        [InlineData(10, ConditionOperator.LessThan, "20", true)]
        [InlineData(5, ConditionOperator.GreaterOrEqual, "5", true)]
        public void Amount_in_value2_is_compared_with_operator(int highest, ConditionOperator op, string value2, bool expected) =>
            Assert.Equal(expected, ConditionEvaluator.MatchesEnergyResistance(highest, Cond(op, value2), useAmount: true));

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("abc")]
        [InlineData("-1")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        public void Unusable_value2_keeps_legacy_resistance_above_zero(string value2) {
            Assert.True(ConditionEvaluator.MatchesEnergyResistance(1, Cond(ConditionOperator.Equal, value2), true));
            Assert.False(ConditionEvaluator.MatchesEnergyResistance(0, Cond(ConditionOperator.Equal, value2), true));
            Assert.False(ConditionEvaluator.MatchesEnergyResistance(1, Cond(ConditionOperator.NotEqual, value2), true));
            Assert.True(ConditionEvaluator.MatchesEnergyResistance(0, Cond(ConditionOperator.NotEqual, value2), true));
        }

        // Count subjects: Value2 is the count threshold ("3 enemies"), not a resistance amount.
        [Fact]
        public void Count_subject_ignores_value2_amount() {
            Assert.True(ConditionEvaluator.MatchesEnergyResistance(5, Cond(ConditionOperator.Equal, "3"), useAmount: false));
            Assert.False(ConditionEvaluator.MatchesEnergyResistance(0, Cond(ConditionOperator.Equal, "3"), useAmount: false));
            Assert.False(ConditionEvaluator.MatchesEnergyResistance(5, Cond(ConditionOperator.NotEqual, "3"), useAmount: false));
        }

        // Preset/config JSON persists numeric enum indices — appending must not shift them.
        [Fact]
        public void ConditionProperty_IndicesStayAppendOnly() {
            Assert.Equal(34, (int)ConditionProperty.WieldsRangedWeapon);
            Assert.Equal(37, (int)ConditionProperty.HasEnergyResistance);
        }
    }
}
