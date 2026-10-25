using System;
using System.Collections.Generic;
using System.Linq;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    // Cleanse selector over synthetic remedy lists (WTW-30): coverage, ranking, latch fall-through, Heal last.
    [Collection("CleanseLatch")]
    public class CleanseSelectorTests {
        const AfflictionKinds Poison = AfflictionKinds.Poison, Curse = AfflictionKinds.Curse,
            Disease = AfflictionKinds.Disease, Paralysis = AfflictionKinds.Paralysis, Stat = AfflictionKinds.StatDebuffAbilityDamage;

        static CleanseProfile Profile(params CleanseEffect[] effects) {
            var p = new CleanseProfile();
            foreach (var e in effects) p.Add(e);
            return p;
        }

        static CleanseEffect Sure(AfflictionKinds k) { return new CleanseEffect { Kinds = k }; }
        static CleanseEffect Dice(AfflictionKinds k) { return new CleanseEffect { Kinds = k, Check = CleanseCheckType.DC }; }
        static CleanseEffect Hold(AfflictionKinds k) { return new CleanseEffect { Kinds = k, Suppress = true }; }

        static CleanseOptionInput Opt(string key, CleanseProfile p, int grade = 1, bool slot = true, double chance = 1.0) {
            return new CleanseOptionInput { Key = key, Name = key, Profile = p, Grade = grade, IsSlot = slot, Chance = (e, bit) => chance };
        }

        // A fresh ally id per test: the latch is process-wide static state.
        static CleanseSituation Sit(AfflictionKinds mask, AfflictionKinds rule = (AfflictionKinds)((1 << 15) - 1), double now = 100) {
            return new CleanseSituation { AllyId = Guid.NewGuid().ToString(), AllyMask = mask, RuleKinds = rule, NowSeconds = now, InCombat = true };
        }

        static string KeyOf(CleanseVerdict s, params CleanseOptionInput[] options) { return options[s.Index].Key; }

        [Fact]
        public void Hopeless_remedy_is_rejected_with_a_reason() {
            var dud = Opt("dud", Profile(Dice(Poison)), chance: 0.0);
            var ok = Opt("ok", Profile(Dice(Poison)), grade: 5, chance: 0.5);
            var only = CleanseSelector.Select(new[] { dud }, Sit(Poison));
            Assert.Equal(-1, only.Index);
            Assert.Contains(only.Rejected, r => r.Contains("dud") && r.Contains("hopeless"));
            Assert.Equal("ok", KeyOf(CleanseSelector.Select(new[] { dud, ok }, Sit(Poison)), dud, ok));
        }

        [Fact]
        public void Nothing_to_cure_selects_nothing() {
            var s = CleanseSelector.Select(new[] { Opt("a", Profile(Sure(Poison))) }, Sit(AfflictionKinds.None));
            Assert.Equal(-1, s.Index);
        }

        [Fact]
        public void Rule_kinds_narrow_the_ally_mask() {
            var o = new[] { Opt("poison", Profile(Sure(Poison))) };
            Assert.Equal(-1, CleanseSelector.Select(o, Sit(Poison | Curse, Curse)).Index);
            Assert.Equal(0, CleanseSelector.Select(o, Sit(Poison | Curse, Poison)).Index);
        }

        [Fact]
        public void Remedy_that_covers_nothing_the_ally_has_is_rejected_with_a_reason() {
            var s = CleanseSelector.Select(new[] { Opt("curse", Profile(Sure(Curse))) }, Sit(Poison));
            Assert.Equal(-1, s.Index);
            Assert.Contains(s.Rejected, r => r.Contains("curse") && r.Contains("no-match"));
        }

        [Fact]
        public void Wider_coverage_wins_at_equal_tier() {
            var one = Opt("one", Profile(Sure(Poison)), grade: 1);
            var two = Opt("two", Profile(Sure(Poison | Curse)), grade: 4);
            var s = CleanseSelector.Select(new[] { one, two }, Sit(Poison | Curse));
            Assert.Equal("two", KeyOf(s, one, two));
            Assert.Equal(Poison | Curse, s.Covered);
        }

        [Fact]
        public void Only_kinds_the_ally_has_count_as_coverage() {
            var wide = Opt("wide", Profile(Sure(Poison | Curse | Disease)), grade: 5);
            var narrow = Opt("narrow", Profile(Sure(Poison)), grade: 1);
            var s = CleanseSelector.Select(new[] { wide, narrow }, Sit(Poison));
            // Both cover exactly Poison for this ally: the cheaper grade wins.
            Assert.Equal("narrow", KeyOf(s, wide, narrow));
            Assert.Equal(Poison, s.Covered);
        }

        [Fact]
        public void Lower_grade_wins_then_slot_before_consumable() {
            var g3 = Opt("g3", Profile(Sure(Poison)), grade: 3);
            var g1wand = Opt("g1wand", Profile(Sure(Poison)), grade: 1, slot: false);
            var g1slot = Opt("g1slot", Profile(Sure(Poison)), grade: 1, slot: true);
            var s = CleanseSelector.Select(new[] { g3, g1wand, g1slot }, Sit(Poison));
            Assert.Equal("g1slot", KeyOf(s, g3, g1wand, g1slot));
        }

        [Fact]
        public void High_chance_dice_remedy_beats_pricier_sure_remedy() {
            var dice = Opt("dice", Profile(Dice(Poison)), grade: 3, chance: 0.95);
            var sure = Opt("sure", Profile(Sure(Poison)), grade: 4);
            Assert.Equal("dice", KeyOf(CleanseSelector.Select(new[] { sure, dice }, Sit(Poison)), sure, dice));
        }

        [Fact]
        public void Low_chance_dice_remedy_ranks_below_sure() {
            var dice = Opt("dice", Profile(Dice(Poison)), grade: 3, chance: 0.30);
            var sure = Opt("sure", Profile(Sure(Poison)), grade: 4);
            Assert.Equal("sure", KeyOf(CleanseSelector.Select(new[] { dice, sure }, Sit(Poison)), dice, sure));
        }

        [Fact]
        public void Unknown_chance_input_uses_the_coin_flip_default() {
            var dice = new CleanseOptionInput { Key = "dice", Name = "dice", Profile = Profile(Dice(Poison)), Grade = 1, IsSlot = true };
            var sure = Opt("sure", Profile(Sure(Poison)), grade: 2);
            var s = CleanseSelector.Select(new[] { dice, sure }, Sit(Poison));
            Assert.Equal("sure", KeyOf(s, dice, sure));
        }

        [Fact]
        public void Suppressor_ranks_below_a_real_remedy()  {
            var hold = Opt("hold", Profile(Hold(Poison)), grade: 0);
            var cure = Opt("cure", Profile(Sure(Poison)), grade: 5);
            var s = CleanseSelector.Select(new[] { hold, cure }, Sit(Poison));
            Assert.Equal("cure", KeyOf(s, hold, cure));
        }

        [Fact]
        public void Suppressor_is_used_when_it_is_all_there_is() {
            var hold = Opt("hold", Profile(Hold(Poison)));
            var s = CleanseSelector.Select(new[] { hold }, Sit(Poison));
            Assert.Equal(0, s.Index);
            Assert.Equal(CleanseRanking.TierSuppressor, s.Tier);
        }

        [Fact]
        public void Heal_is_only_chosen_when_nothing_else_covers() {
            var heal = Opt("heal", HealProfile(Poison | Curse | Disease), grade: 6);
            var cure = Opt("cure", Profile(Sure(Poison)), grade: 3);
            Assert.Equal("cure", KeyOf(CleanseSelector.Select(new[] { heal, cure }, Sit(Poison | Curse)), heal, cure));
            var onlyHeal = CleanseSelector.Select(new[] { heal, cure }, Sit(Curse));
            Assert.Equal("heal", KeyOf(onlyHeal, heal, cure));
            Assert.Equal(CleanseRanking.TierHeal, onlyHeal.Tier);
        }

        static CleanseProfile HealProfile(AfflictionKinds k) {
            var p = Profile(Sure(k));
            p.HealsHp = true;
            return p;
        }

        [Fact]
        public void Latched_remedy_falls_through_to_the_next() {
            var best = Opt("best", Profile(Dice(Poison)), grade: 1, chance: 0.9);
            var other = Opt("other", Profile(Sure(Poison)), grade: 3);
            var situation = Sit(Poison);
            CleanseAttemptLatch.Record(situation.AllyId, Poison, "best", situation.NowSeconds - 3, inCombat: true);
            var s = CleanseSelector.Select(new[] { best, other }, situation);
            Assert.Equal("other", KeyOf(s, best, other));
            Assert.Contains(s.Rejected, r => r.Contains("best") && r.Contains("latched"));
        }

        [Fact]
        public void Latch_expires_after_the_window() {
            var best = Opt("best", Profile(Sure(Poison)), grade: 1);
            var other = Opt("other", Profile(Sure(Poison)), grade: 3);
            var situation = Sit(Poison, now: 100);
            CleanseAttemptLatch.Record(situation.AllyId, Poison, "best", 100, inCombat: true);
            situation.NowSeconds = 100 + CleanseAttemptLatch.AttemptLatchCombatSec;
            Assert.Equal("best", KeyOf(CleanseSelector.Select(new[] { best, other }, situation), best, other));
        }

        [Fact]
        public void Partly_latched_remedy_still_covers_its_free_kinds() {
            var wide = Opt("wide", Profile(Sure(Poison | Curse)), grade: 1);
            var situation = Sit(Poison | Curse);
            CleanseAttemptLatch.Record(situation.AllyId, Poison, "wide", situation.NowSeconds, inCombat: true);
            var s = CleanseSelector.Select(new[] { wide }, situation);
            Assert.Equal(0, s.Index);
            Assert.Equal(Curse, s.Covered);
        }

        [Fact]
        public void Everything_latched_selects_nothing() {
            var only = Opt("only", Profile(Sure(Poison)));
            var situation = Sit(Poison);
            CleanseAttemptLatch.Record(situation.AllyId, Poison, "only", situation.NowSeconds, inCombat: false);
            Assert.Equal(-1, CleanseSelector.Select(new[] { only }, situation).Index);
        }

        [Fact]
        public void Caster_fact_gate_decides_coverage() {
            var gated = new CleanseEffect { Kinds = Poison, GateFacts = new[] { "mercy" } };
            var o = new CleanseOptionInput { Key = "loh", Name = "loh", Profile = Profile(gated), Grade = 0, IsSlot = true, HasFact = f => f == "mercy" };
            var without = new CleanseOptionInput { Key = "loh", Name = "loh", Profile = Profile(gated), Grade = 0, IsSlot = true, HasFact = f => false };
            Assert.Equal(0, CleanseSelector.Select(new[] { o }, Sit(Poison)).Index);
            Assert.Equal(-1, CleanseSelector.Select(new[] { without }, Sit(Poison)).Index);
        }

        // PM decision a: a specific-buff removal only counts when the ally carries that exact buff.
        [Fact]
        public void Specific_buff_removal_needs_that_buff_on_the_ally() {
            var removesSlow = new CleanseEffect { Kinds = AfflictionKinds.StunDaze | AfflictionKinds.SlowedEntangled, RemovedBuffGuids = new[] { "slowbuff" } };
            var o = Opt("haste", Profile(removesSlow));
            var daze = Sit(AfflictionKinds.StunDaze);
            daze.AllyHasBuff = g => false;
            var s = CleanseSelector.Select(new[] { o }, daze);
            Assert.Equal(-1, s.Index);
            Assert.Contains(s.Rejected, r => r.Contains("haste") && r.Contains("no-match"));

            var slowed = Sit(AfflictionKinds.StunDaze | AfflictionKinds.SlowedEntangled);
            slowed.AllyHasBuff = g => g == "slowbuff";
            Assert.Equal(0, CleanseSelector.Select(new[] { o }, slowed).Index);
        }

        [Fact]
        public void Descriptor_removal_counts_by_kind_without_a_buff_identity() {
            var o = Opt("dispel", Profile(Sure(Poison)));
            var sit = Sit(Poison);
            sit.AllyHasBuff = g => false;
            Assert.Equal(0, CleanseSelector.Select(new[] { o }, sit).Index);
        }

        [Fact]
        public void Equal_candidates_break_ties_by_key() {
            var b = Opt("b", Profile(Sure(Poison)));
            var a = Opt("a", Profile(Sure(Poison)));
            Assert.Equal("a", KeyOf(CleanseSelector.Select(new[] { b, a }, Sit(Poison)), b, a));
        }

        [Fact]
        public void Empty_option_list_selects_nothing() {
            Assert.Equal(-1, CleanseSelector.Select(new List<CleanseOptionInput>(), Sit(Poison)).Index);
        }
    }
}
