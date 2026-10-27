using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class CleanseChanceTests {
        [Fact]
        public void None_is_one() {
            Assert.Equal(1.0, CleanseChance.Probability(CleanseCheckType.None, null, null, null, null));
        }

        [Fact]
        public void DC_check_formula() {
            // d20 + 11 >= 17 -> roll 6..20 -> 15/20
            Assert.Equal(0.75, CleanseChance.Probability(CleanseCheckType.DC, 17, 11, null, null), 6);
        }

        [Fact]
        public void Clamp_0_and_1() {
            Assert.Equal(0.0, CleanseChance.Probability(CleanseCheckType.DC, 40, 5, null, null));
            Assert.Equal(1.0, CleanseChance.Probability(CleanseCheckType.DC, 5, 20, null, null));
        }

        [Fact]
        public void Dc_zero_is_automatic() {
            Assert.Equal(1.0, CleanseChance.Probability(CleanseCheckType.DC, 0, -50, null, null));
        }

        [Fact]
        public void CasterLevel_check_uses_eleven_plus_affliction_level() {
            // DC = 11 + 9 = 20, bonus 11 -> roll 9..20 -> 12/20
            Assert.Equal(0.6, CleanseChance.Probability(CleanseCheckType.CasterLevel, null, 11, null, 9), 6);
        }

        [Fact]
        public void Max_caster_level_caps_the_bonus() {
            // cap 5: DC 17 - 5 = 12 -> roll 12..20 -> 9/20
            Assert.Equal(0.45, CleanseChance.Probability(CleanseCheckType.DC, 17, 11, null, null, maxCasterLevel: 5), 6);
        }

        [Fact]
        public void Skill_checks_use_the_skill_bonus() {
            Assert.Equal(0.75, CleanseChance.Probability(CleanseCheckType.SkillDC, 17, null, 11, null), 6);
            Assert.Equal(0.6, CleanseChance.Probability(CleanseCheckType.SkillCasterLevel, null, null, 11, 9), 6);
        }

        [Fact]
        public void Unknown_inputs_use_UnknownCheckChance() {
            Assert.Equal(CleanseChance.UnknownCheckChance, CleanseChance.Probability(CleanseCheckType.DC, null, 11, null, null));
            Assert.Equal(CleanseChance.UnknownCheckChance, CleanseChance.Probability(CleanseCheckType.DC, 17, null, null, null));
            Assert.Equal(CleanseChance.UnknownCheckChance, CleanseChance.Probability(CleanseCheckType.CasterLevel, null, 11, null, null));
            Assert.Equal(CleanseChance.UnknownCheckChance, CleanseChance.Probability(CleanseCheckType.CasterLevel, null, null, null, 9));
            Assert.Equal(CleanseChance.UnknownCheckChance, CleanseChance.Probability(CleanseCheckType.SkillDC, 17, 11, null, null));
            Assert.Equal(CleanseChance.UnknownCheckChance, CleanseChance.Probability(CleanseCheckType.SkillCasterLevel, null, 11, null, 9));
            Assert.Equal(CleanseChance.UnknownCheckChance, CleanseChance.Probability(CleanseCheckType.SkillCasterLevel, null, 11, 4, null));
        }

        [Theory]
        [InlineData(1.0, 10)]
        [InlineData(0.95, 10)]
        [InlineData(0.91, 10)]
        [InlineData(0.90, 9)]
        [InlineData(0.31, 4)]
        [InlineData(0.30, 3)]
        [InlineData(0.05, 1)]
        [InlineData(0.0, 0)]
        public void Steps_round_up_to_ten_percent(double p, int expected) {
            Assert.Equal(expected, CleanseChance.Steps(p));
        }

        [Fact]
        public void Check_type_values_mirror_the_game_enum() {
            Assert.Equal(0, (int)CleanseCheckType.None);
            Assert.Equal(1, (int)CleanseCheckType.CasterLevel);
            Assert.Equal(2, (int)CleanseCheckType.DC);
            Assert.Equal(3, (int)CleanseCheckType.SkillDC);
            Assert.Equal(4, (int)CleanseCheckType.SkillCasterLevel);
            Assert.Equal((int)Kingmaker.RuleSystem.Rules.RuleDispelMagic.CheckType.SkillCasterLevel, (int)CleanseCheckType.SkillCasterLevel);
        }
    }
}
