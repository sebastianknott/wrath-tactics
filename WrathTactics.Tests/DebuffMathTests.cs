using Kingmaker.UnitLogic.Mechanics.Components;
using UnityEngine;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class DebuffMathTests {
        [Theory]
        [InlineData(-5, 0f)]
        [InlineData(0, 0f)]
        [InlineData(1, 0f)]
        [InlineData(11, 0.5f)]
        [InlineData(21, 1f)]
        [InlineData(40, 1f)]
        public void FailChance_follows_the_d20_save(int dcMinusSave, float expected) {
            Assert.Equal(expected, DebuffMath.FailChance(dcMinusSave), 3);
        }

        [Theory]
        [InlineData(-11, 0.5f)]
        [InlineData(-20, 0.05f)]
        [InlineData(0, 1f)]
        [InlineData(99, 1f)]
        [InlineData(-99, 0f)]
        public void PenetrationChance_covers_margins_and_sentinels(int margin, float expected) {
            Assert.Equal(expected, DebuffMath.PenetrationChance(margin), 3);
        }

        [Fact]
        public void ExpectedLockRounds_without_repeat_save_is_the_duration() {
            Assert.Equal(5f, DebuffMath.ExpectedLockRounds(5f, 0.5f, false), 3);
        }

        [Fact]
        public void ExpectedLockRounds_with_repeat_save_is_the_geometric_sum() {
            // 1 + 0.5 + 0.25 + 0.125 = 1.875 (three rounds survive with p, p^2, p^3 ... series to n terms)
            Assert.Equal(1.875f, DebuffMath.ExpectedLockRounds(4f, 0.5f, true), 3);
        }

        [Fact]
        public void ExpectedLockRounds_with_certain_failure_is_the_duration() {
            Assert.Equal(6f, DebuffMath.ExpectedLockRounds(6f, 1f, true), 3);
        }

        [Fact]
        public void ExpectedValue_multiplies_all_factors() {
            Assert.Equal(3f, DebuffMath.ExpectedValue(2f, 0.5f, 3f, 1), 3);
            Assert.Equal(2f, DebuffMath.ExpectedValue(4f, 0.5f, 1f, 1), 3);
            Assert.Equal(4f, DebuffMath.ExpectedValue(2f, 1f, 1f, 2), 3);
        }

        [Theory]
        [InlineData((int)SaveGate.Always, 0.3f, 1f)]
        [InlineData((int)SaveGate.OnFail, 0.3f, 0.3f)]
        [InlineData((int)SaveGate.OnSuccess, 0.3f, 0.7f)]
        public void GateChance_respects_the_gate(int gate, float pFail, float expected) {
            Assert.Equal(expected, DebuffMath.GateChance((SaveGate)gate, pFail), 3);
        }

        [Fact]
        public void Progression_picks_the_first_item_reaching_the_value() {
            var b = new[] { 5, 10, 15 };
            var v = new[] { 1, 2, 3 };
            Assert.Equal(1, DebuffMath.Progression(b, v, 3));
            Assert.Equal(1, DebuffMath.Progression(b, v, 5));
            Assert.Equal(2, DebuffMath.Progression(b, v, 6));
            Assert.Equal(3, DebuffMath.Progression(b, v, 99));
        }

        [Fact]
        public void Progression_of_an_empty_table_is_zero() {
            Assert.Equal(0, DebuffMath.Progression(new int[0], new int[0], 7));
        }

        [Fact]
        public void ApplyRank_Div2_with_max_caps_the_value() {
            var spec = new RankSpec { Valid = true, Progression = ContextRankProgression.Div2, UseMax = true, Max = 5 };
            Assert.Equal(3, DebuffMath.ApplyRank(spec, 7));
            Assert.Equal(5, DebuffMath.ApplyRank(spec, 14));
            Assert.Equal(5, DebuffMath.ApplyRank(spec, 30));
        }

        [Fact]
        public void ApplyRank_min_lifts_the_value() {
            var spec = new RankSpec { Valid = true, Progression = ContextRankProgression.AsIs, UseMin = true, Min = 4 };
            Assert.Equal(4, DebuffMath.ApplyRank(spec, 1));
            Assert.Equal(9, DebuffMath.ApplyRank(spec, 9));
        }

        [Fact]
        public void ApplyRank_step_progressions() {
            var s = new RankSpec { Valid = true, Progression = ContextRankProgression.StartPlusDivStep, Start = 1, Step = 4 };
            Assert.Equal(1, DebuffMath.ApplyRank(s, 1));
            Assert.Equal(2, DebuffMath.ApplyRank(s, 5));
            s.Progression = ContextRankProgression.OnePlusDivStep;
            s.Step = 3;
            Assert.Equal(3, DebuffMath.ApplyRank(s, 7));
            s.Progression = ContextRankProgression.DivStep;
            Assert.Equal(2, DebuffMath.ApplyRank(s, 7));
            s.Progression = ContextRankProgression.OnePlusDiv2;
            Assert.Equal(4, DebuffMath.ApplyRank(s, 7));
        }

        [Fact]
        public void ApplyRank_zero_step_does_not_divide_by_zero() {
            var s = new RankSpec { Valid = true, Progression = ContextRankProgression.DivStep, Step = 0 };
            Assert.Equal(8, DebuffMath.ApplyRank(s, 8));
        }

        [Fact]
        public void Magnitude_RayOfEnfeeblement_is_dice_average_plus_half_level_capped() {
            // 1d6 average 3.5 plus min(5, CL/2)
            var e = new DebuffEntry {
                Fixed = 3.5f, RankMult = 1f,
                Rank = new RankSpec { Valid = true, Progression = ContextRankProgression.Div2, UseMax = true, Max = 5 },
            };
            Assert.Equal(6.5f, DebuffMath.Magnitude(e, 6, default), 3);
            Assert.Equal(8.5f, DebuffMath.Magnitude(e, 14, default), 3);
        }

        [Fact]
        public void Magnitude_adds_the_target_terms() {
            var t = new DebuffMath.TargetStats { DexMod = 3, SpeedFeet = 30f, ArmorBonus = 6 };
            Assert.Equal(3f, DebuffMath.Magnitude(new DebuffEntry { Scaling = TargetScaling.DexBonusLost }, 1, t), 3);
            Assert.Equal(8f, DebuffMath.Magnitude(new DebuffEntry { Scaling = TargetScaling.DexToMinusFive }, 1, t), 3);
            Assert.Equal(15f, DebuffMath.Magnitude(new DebuffEntry { Scaling = TargetScaling.HalfSpeed }, 1, t), 3);
            Assert.Equal(6f, DebuffMath.Magnitude(new DebuffEntry { Scaling = TargetScaling.ArmorBonusLost }, 1, t), 3);
        }

        [Fact]
        public void Magnitude_negative_dex_modifier_never_adds_a_bonus() {
            var t = new DebuffMath.TargetStats { DexMod = -2 };
            Assert.Equal(0f, DebuffMath.Magnitude(new DebuffEntry { Scaling = TargetScaling.DexBonusLost }, 1, t), 3);
            Assert.Equal(3f, DebuffMath.Magnitude(new DebuffEntry { Scaling = TargetScaling.DexToMinusFive }, 1, t), 3);
        }

        [Fact]
        public void Magnitude_ignores_an_invalid_rank() {
            var e = new DebuffEntry { Fixed = 2f, RankMult = 5f, Rank = new RankSpec { Valid = false } };
            Assert.Equal(2f, DebuffMath.Magnitude(e, 10, default), 3);
        }

        [Fact]
        public void CountWithin_counts_planar_distance_inclusive() {
            var pts = new[] { new Vector3(0, 0, 0), new Vector3(3, 50, 4), new Vector3(6, 0, 0) };
            Assert.Equal(2, DebuffMath.CountWithin(pts, Vector3.zero, 5f));
            Assert.Equal(3, DebuffMath.CountWithin(pts, Vector3.zero, 6f));
            Assert.Equal(0, DebuffMath.CountWithin(new Vector3[0], Vector3.zero, 5f));
        }
    }
}
