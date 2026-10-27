using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class CleanseRankingTests {
        static CleanseCandidate C(string key, int steps = 10, int grade = 3, bool slot = true, int tier = 0) {
            return new CleanseCandidate { Key = key, ExpectedSteps = steps, Grade = grade, IsSlot = slot, Tier = tier };
        }

        static string Winner(params CleanseCandidate[] all) {
            return all[CleanseRanking.Best(all)].Key;
        }

        [Fact]
        public void Poison_dice_remedy_beats_pricier_sure_remedy() {
            var neut = C("NeutPoison", steps: CleanseChance.Steps(0.95), grade: 3);
            var sure = C("Sure", steps: CleanseChance.Steps(1.0), grade: 4);
            Assert.Equal("NeutPoison", Winner(sure, neut));
        }

        [Fact]
        public void Low_chance_dice_remedy_ranks_below_sure() {
            var dice = C("Dice", steps: CleanseChance.Steps(0.30), grade: 1);
            var sure = C("Sure", steps: CleanseChance.Steps(1.0), grade: 5);
            Assert.Equal("Sure", Winner(dice, sure));
        }

        [Fact]
        public void Suppressor_ranks_below_a_normal_remedy_even_when_it_covers_more() {
            var normal = C("Normal", steps: 10, tier: CleanseRanking.TierNormal);
            var suppressor = C("Supp", steps: 40, grade: 0, tier: CleanseRanking.TierSuppressor);
            Assert.Equal("Normal", Winner(suppressor, normal));
        }

        [Fact]
        public void Heal_is_last() {
            var heal = C("Heal", steps: 100, grade: 0, tier: CleanseRanking.TierHeal);
            var supp = C("Supp", steps: 10, tier: CleanseRanking.TierSuppressor);
            Assert.Equal("Supp", Winner(heal, supp));
        }

        [Fact]
        public void Wider_coverage_wins_before_grade() {
            var wide = C("Wide", steps: 20, grade: 5);
            var narrow = C("Narrow", steps: 10, grade: 1);
            Assert.Equal("Wide", Winner(narrow, wide));
        }

        [Fact]
        public void Equal_coverage_prefers_the_lower_grade_and_class_abilities_at_zero() {
            Assert.Equal("Low", Winner(C("High", grade: 4), C("Low", grade: 2)));
            Assert.Equal("Ability", Winner(C("Spell", grade: 1), C("Ability", grade: 0)));
        }

        [Fact]
        public void Grade_is_decided_before_slot_versus_consumable() {
            var cheapPotion = C("Potion", grade: 1, slot: false);
            var pricierSlot = C("Slot", grade: 3, slot: true);
            Assert.Equal("Potion", Winner(pricierSlot, cheapPotion));
        }

        [Fact]
        public void Equal_grade_prefers_the_slot_over_a_consumable() {
            Assert.Equal("Slot", Winner(C("Potion", slot: false), C("Slot", slot: true)));
        }

        [Fact]
        public void Option_guid_is_the_final_tie_break() {
            Assert.Equal("a", Winner(C("b"), C("a")));
            Assert.Equal(0, CleanseRanking.Compare(C("a"), C("a")));
        }

        [Fact]
        public void Best_of_nothing_is_minus_one() {
            Assert.Equal(-1, CleanseRanking.Best(new CleanseCandidate[0]));
        }

        [Fact]
        public void ExpectedSteps_sums_bucketed_chances() {
            Assert.Equal(10 + 8 + 3, CleanseRanking.ExpectedSteps(new[] { 1.0, 0.75, 0.30 }));
            Assert.Equal(0, CleanseRanking.ExpectedSteps(new double[0]));
        }
    }
}
