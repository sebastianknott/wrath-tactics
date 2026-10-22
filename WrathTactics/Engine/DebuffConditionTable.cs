using System.Collections.Generic;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// Hand table: what a UnitCondition costs its bearer, per effect group (WTW-30). Values come from the game
    /// code (IL: RuleCalculateAC, UnitStatusEffect*, system-mechanics buffs), not from PF1 rules text; see
    /// claude-context/gotchas-casting.md "Debuff groups". Conditions not listed here are not debuffs.
    /// </summary>
    internal static class DebuffConditionTable {

        static DebuffEntry Cond(UnitCondition c, DebuffGroup g, float fixedValue,
                                TargetScaling scaling = TargetScaling.None, LockTier tier = LockTier.None) {
            return new DebuffEntry {
                Group = g, Kind = DebuffEntryKind.Condition, Condition = c, Fixed = fixedValue,
                Scaling = scaling, Tier = tier,
            };
        }

        static void Saves(List<DebuffEntry> list, UnitCondition c, float value) {
            list.Add(Cond(c, DebuffGroup.SaveWill, value));
            list.Add(Cond(c, DebuffGroup.SaveReflex, value));
            list.Add(Cond(c, DebuffGroup.SaveFortitude, value));
        }

        /// <summary>Entries a condition contributes; empty for conditions that are no debuff.</summary>
        internal static List<DebuffEntry> ConditionEntries(UnitCondition c) {
            var l = new List<DebuffEntry>();
            switch (c) {
                case UnitCondition.Helpless:
                case UnitCondition.Paralyzed:
                case UnitCondition.Sleeping:
                    l.Add(Cond(c, DebuffGroup.ArmorClass, 4, TargetScaling.DexToMinusFive));
                    l.Add(Cond(c, DebuffGroup.SaveReflex, 0, TargetScaling.DexToMinusFive));
                    l.Add(Cond(c, DebuffGroup.Incapacitated, 0, TargetScaling.None, LockTier.All));
                    break;
                case UnitCondition.Stunned:
                case UnitCondition.Cowering:
                    l.Add(Cond(c, DebuffGroup.ArmorClass, 2, TargetScaling.DexBonusLost));
                    l.Add(Cond(c, DebuffGroup.Incapacitated, 0, TargetScaling.None, LockTier.All));
                    break;
                case UnitCondition.Dazed:
                    l.Add(Cond(c, DebuffGroup.Incapacitated, 0, TargetScaling.None, LockTier.All));
                    break;
                case UnitCondition.Confusion:
                    l.Add(Cond(c, DebuffGroup.Incapacitated, 0, TargetScaling.None, LockTier.Random));
                    break;
                case UnitCondition.Nauseated:
                    l.Add(Cond(c, DebuffGroup.Incapacitated, 0, TargetScaling.None, LockTier.MoveOnly));
                    break;
                case UnitCondition.Prone:
                    // The game code has no attack penalty for Prone; the lock lasts until the unit stands.
                    l.Add(Cond(c, DebuffGroup.ArmorClass, 4));
                    l.Add(Cond(c, DebuffGroup.Incapacitated, 0, TargetScaling.None, LockTier.UntilStand));
                    break;
                case UnitCondition.Staggered:
                    l.Add(Cond(c, DebuffGroup.Incapacitated, 0, TargetScaling.None, LockTier.NoFullRound));
                    break;
                case UnitCondition.Blindness:
                    l.Add(Cond(c, DebuffGroup.ArmorClass, 2));
                    break;
                case UnitCondition.LoseDexterityToAC:
                    l.Add(Cond(c, DebuffGroup.ArmorClass, 0, TargetScaling.DexBonusLost));
                    break;
                case UnitCondition.Shaken:
                case UnitCondition.Frightened:
                    l.Add(Cond(c, DebuffGroup.Attack, 2));
                    Saves(l, c, 2);
                    break;
                case UnitCondition.Sickened:
                    l.Add(Cond(c, DebuffGroup.Attack, 2));
                    l.Add(Cond(c, DebuffGroup.Damage, 2));
                    Saves(l, c, 2);
                    break;
                case UnitCondition.Entangled:
                    l.Add(Cond(c, DebuffGroup.Attack, 2));
                    l.Add(Cond(c, DebuffGroup.Dexterity, 4));
                    l.Add(Cond(c, DebuffGroup.Speed, 0, TargetScaling.HalfSpeed));
                    break;
                case UnitCondition.Dazzled:
                    l.Add(Cond(c, DebuffGroup.Attack, 1));
                    break;
                case UnitCondition.Fatigued:
                    l.Add(Cond(c, DebuffGroup.Strength, 2));
                    l.Add(Cond(c, DebuffGroup.Dexterity, 2));
                    break;
                case UnitCondition.Exhausted:
                    l.Add(Cond(c, DebuffGroup.Strength, 6));
                    l.Add(Cond(c, DebuffGroup.Dexterity, 6));
                    l.Add(Cond(c, DebuffGroup.Speed, 0, TargetScaling.HalfSpeed));
                    break;
            }
            return l;
        }

        /// <summary>
        /// Combat maneuvers carry their effect in code, not in a buff blueprint: the table names the condition
        /// they inflict. Disarm and Bull Rush fit no group; SunderArmor is handled separately.
        /// </summary>
        internal static UnitCondition? ManeuverCondition(CombatManeuver maneuver) {
            switch (maneuver) {
                case CombatManeuver.Trip: return UnitCondition.Prone;
                case CombatManeuver.DirtyTrickEntangle: return UnitCondition.Entangled;
                case CombatManeuver.DirtyTrickSickened: return UnitCondition.Sickened;
                case CombatManeuver.DirtyTrickBlind: return UnitCondition.Blindness;
                default: return null;
            }
        }

        /// <summary>The AC entry of Sunder Armor: the target's armor bonus is gone.</summary>
        internal static DebuffEntry SunderArmorEntry() {
            return new DebuffEntry {
                Group = DebuffGroup.ArmorClass, Kind = DebuffEntryKind.Stat, Stat = StatType.AC,
                Scaling = TargetScaling.ArmorBonusLost,
            };
        }

        /// <summary>Group a negative stat bonus belongs to; None for stats outside the eleven groups.</summary>
        internal static DebuffGroup GroupForStat(StatType stat) {
            switch (stat) {
                case StatType.AC: return DebuffGroup.ArmorClass;
                case StatType.AdditionalAttackBonus: return DebuffGroup.Attack;
                case StatType.AdditionalDamage: return DebuffGroup.Damage;
                case StatType.SaveWill: return DebuffGroup.SaveWill;
                case StatType.SaveReflex: return DebuffGroup.SaveReflex;
                case StatType.SaveFortitude: return DebuffGroup.SaveFortitude;
                case StatType.Dexterity: return DebuffGroup.Dexterity;
                case StatType.Strength: return DebuffGroup.Strength;
                case StatType.Constitution: return DebuffGroup.Constitution;
                case StatType.Speed: return DebuffGroup.Speed;
                default: return DebuffGroup.None;
            }
        }
    }
}
