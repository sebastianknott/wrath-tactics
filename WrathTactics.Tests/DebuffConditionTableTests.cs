using System.Linq;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class DebuffConditionTableTests {
        static DebuffEntry Entry(UnitCondition c, DebuffGroup g) =>
            DebuffConditionTable.ConditionEntries(c).FirstOrDefault(e => e.Group == g);

        [Theory]
        [InlineData(UnitCondition.Helpless)]
        [InlineData(UnitCondition.Paralyzed)]
        [InlineData(UnitCondition.Sleeping)]
        public void Helpless_family_hits_armor_class_reflex_and_locks(UnitCondition c) {
            Assert.Equal(TargetScaling.DexToMinusFive, Entry(c, DebuffGroup.ArmorClass).Scaling);
            Assert.NotNull(Entry(c, DebuffGroup.SaveReflex));
            Assert.Equal(LockTier.All, Entry(c, DebuffGroup.Incapacitated).Tier);
        }

        [Fact]
        public void Stunned_loses_dex_bonus_and_locks() {
            Assert.Equal(2f, Entry(UnitCondition.Stunned, DebuffGroup.ArmorClass).Fixed);
            Assert.Equal(LockTier.All, Entry(UnitCondition.Stunned, DebuffGroup.Incapacitated).Tier);
        }

        [Theory]
        [InlineData(UnitCondition.Confusion, (int)LockTier.Random)]
        [InlineData(UnitCondition.Nauseated, (int)LockTier.MoveOnly)]
        [InlineData(UnitCondition.Staggered, (int)LockTier.NoFullRound)]
        [InlineData(UnitCondition.Prone, (int)LockTier.UntilStand)]
        [InlineData(UnitCondition.Dazed, (int)LockTier.All)]
        public void Lock_tiers_per_condition(UnitCondition c, int tier) {
            Assert.Equal((LockTier)tier, Entry(c, DebuffGroup.Incapacitated).Tier);
        }

        [Fact]
        public void Sickened_covers_attack_damage_and_all_saves() {
            foreach (var g in new[] { DebuffGroup.Attack, DebuffGroup.Damage, DebuffGroup.SaveWill,
                                       DebuffGroup.SaveReflex, DebuffGroup.SaveFortitude })
                Assert.Equal(2f, Entry(UnitCondition.Sickened, g).Fixed);
        }

        [Fact]
        public void Shaken_has_no_damage_entry() {
            Assert.Null(Entry(UnitCondition.Shaken, DebuffGroup.Damage));
            Assert.Equal(2f, Entry(UnitCondition.Shaken, DebuffGroup.Attack).Fixed);
        }

        [Fact]
        public void Entangled_halves_speed_and_penalises_dexterity() {
            Assert.Equal(TargetScaling.HalfSpeed, Entry(UnitCondition.Entangled, DebuffGroup.Speed).Scaling);
            Assert.Equal(4f, Entry(UnitCondition.Entangled, DebuffGroup.Dexterity).Fixed);
        }

        [Fact]
        public void Exhausted_is_stronger_than_fatigued() {
            Assert.Equal(2f, Entry(UnitCondition.Fatigued, DebuffGroup.Strength).Fixed);
            Assert.Equal(6f, Entry(UnitCondition.Exhausted, DebuffGroup.Strength).Fixed);
        }

        [Fact]
        public void Unlisted_conditions_are_no_debuffs() {
            Assert.Empty(DebuffConditionTable.ConditionEntries(UnitCondition.Fatigued).Where(e => e.Group == DebuffGroup.Attack));
            Assert.Empty(DebuffConditionTable.ConditionEntries(UnitCondition.Invisible));
        }

        [Theory]
        [InlineData(CombatManeuver.Trip, UnitCondition.Prone)]
        [InlineData(CombatManeuver.DirtyTrickEntangle, UnitCondition.Entangled)]
        [InlineData(CombatManeuver.DirtyTrickSickened, UnitCondition.Sickened)]
        [InlineData(CombatManeuver.DirtyTrickBlind, UnitCondition.Blindness)]
        public void Maneuvers_map_to_their_condition(CombatManeuver m, UnitCondition expected) {
            Assert.Equal(expected, DebuffConditionTable.ManeuverCondition(m));
        }

        [Fact]
        public void Bull_rush_fits_no_group() {
            Assert.Null(DebuffConditionTable.ManeuverCondition(CombatManeuver.BullRush));
        }

        [Fact]
        public void Sunder_armor_is_an_armor_class_entry_scaling_with_armor() {
            var e = DebuffConditionTable.SunderArmorEntry();
            Assert.Equal(DebuffGroup.ArmorClass, e.Group);
            Assert.Equal(TargetScaling.ArmorBonusLost, e.Scaling);
        }
    }
}
