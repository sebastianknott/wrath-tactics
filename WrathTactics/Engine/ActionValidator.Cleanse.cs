using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>The remedy the RemoveCondition search picked, with the kinds it will try and the numbers behind the pick.</summary>
    internal struct CleanseChoice {
        public CleanseOption Option;
        /// <summary>Single kind bits the cast will try (and the attempt latch records).</summary>
        public AfflictionKinds Covered;
        /// <summary>Ally mask narrowed by the rule: what the pick was ranked against.</summary>
        public AfflictionKinds Wanted;
        public int Tier;
        /// <summary>Expected removals in 10% steps (CleanseRanking.ExpectedSteps).</summary>
        public int Steps;
    }

    public static partial class ActionValidator {
        static bool s_cleanseParamsWarned;

        /// <summary>
        /// RemoveCondition: the best remedy for the ally's current afflictions (WTW-30 cleanse). The ally's mask is
        /// read fresh here (CleanseAfflictionReader.Read), narrowed by the rule's kinds, and every remedy the owner
        /// can cast now is ranked by CleanseSelector (tier, expected removals, grade, slot-before-consumable).
        /// An ally another party member is already curing, and remedies still inside their attempt window, are skipped.
        /// One log line per decision names the pick, the kinds it covers and the options that were rejected.
        /// </summary>
        internal static CleanseChoice? FindBestCleanse(UnitEntityData owner, UnitEntityData ally, AfflictionKinds ruleKinds,
                                                       SpellSourceMask sources) {
            if (owner == null || ally == null) return null;
            if (ally.Descriptor?.State?.IsFinallyDead ?? true) {
                Log.Engine.Trace($"Cleanse {owner.CharacterName} -> {ally.CharacterName}: ally is dead");
                return null;
            }

            var mask = CleanseAfflictionReader.Read(ally);
            var wanted = mask & ruleKinds;
            if (wanted == AfflictionKinds.None) {
                Log.Engine.Trace($"Cleanse {owner.CharacterName} -> {ally.CharacterName}: nothing to cure (ally carries [{mask}], rule wants [{ruleKinds}])");
                return null;
            }
            var curing = BeingCastOnByAlly(owner, ally);
            if (curing != null) {
                Log.Engine.Debug($"Cleanse {owner.CharacterName} -> {ally.CharacterName}: skipped, {curing} is already casting a cure on them");
                return null;
            }

            var rejected = new List<string>();
            var options = DebuffOptionEnumerator.EnumerateCleanse(owner, ally, sources, rejected);
            var facts = DebuffOptionEnumerator.OwnerFactGuids(owner);
            var allyBuffs = TargetBuffGuids(ally);
            var afflictionCl = AfflictionCasterLevels(ally);
            var inputs = options.Select(o => InputOf(owner, o, facts, afflictionCl)).ToList();

            var player = Game.Instance.Player;
            var situation = new CleanseSituation {
                AllyId = ally.UniqueId, AllyMask = mask, RuleKinds = ruleKinds,
                AllyHasBuff = allyBuffs.Contains, NowSeconds = player.GameTime.TotalSeconds, InCombat = player.IsInCombat,
            };
            var verdict = CleanseSelector.Select(inputs, situation);
            rejected.AddRange(verdict.Rejected);
            string why = rejected.Count > 0 ? "; rejected: " + string.Join(", ", rejected) : "";

            if (verdict.Index < 0) {
                Log.Engine.Debug($"Cleanse {owner.CharacterName} -> {ally.CharacterName}: no remedy for [{verdict.Wanted}]{why}");
                return null;
            }
            var option = options[verdict.Index];
            Log.Engine.Debug($"Cleanse {owner.CharacterName} -> {ally.CharacterName}: picked '{option.Name}' [{option.Kind}] "
                + $"covering [{verdict.Covered}] of [{verdict.Wanted}] (tier {verdict.Tier}, expected {verdict.ExpectedSteps}/10 per step){why}");
            return new CleanseChoice {
                Option = option, Covered = verdict.Covered, Wanted = verdict.Wanted, Tier = verdict.Tier, Steps = verdict.ExpectedSteps,
            };
        }

        /// <summary>
        /// Validator entry of RemoveCondition. The ally is the rule's resolved target (Target: condition target), its
        /// state is re-read here, and the pick is latched for the executor. <paramref name="abilitySlot"/> is the slot
        /// the remedy's ability occupies (a class ability or wand may be Swift or Move).
        /// </summary>
        internal static bool CanExecuteCleanse(UnitEntityData owner, UnitEntityData ally, AfflictionKinds ruleKinds,
                                               SpellSourceMask sources, out UnitCommand.CommandType? abilitySlot) {
            abilitySlot = null;
            var choice = FindBestCleanse(owner, ally, ruleKinds, sources);
            if (choice == null) return false;
            CleanseSelection.Remember(owner, ally, choice.Value);
            abilitySlot = choice.Value.Option.Ability.RuntimeActionType;
            return true;
        }

        // Name of a party member (not the owner) who has a cure on this ally in the Standard slot or the queue.
        static string BeingCastOnByAlly(UnitEntityData owner, UnitEntityData ally) {
            var party = Game.Instance?.Player?.PartyAndPets;
            if (party == null) return null;
            foreach (var other in party) {
                if (other == null || ReferenceEquals(other, owner)) continue;
                if (IsCuring(other.Commands?.Standard, ally)) return other.CharacterName;
                var queue = other.Commands?.Queue;
                if (queue != null) foreach (var queued in queue) if (IsCuring(queued, ally)) return other.CharacterName;
            }
            return null;
        }

        static bool IsCuring(UnitCommand cmd, UnitEntityData ally) {
            if (cmd == null || cmd.IsFinished) return false;
            var cast = cmd as UnitUseAbility;
            if (cast == null || !ReferenceEquals(cast.Target?.Unit, ally)) return false;
            return CleanseClassifier.Get(cast.Ability?.Blueprint).IsCleanse;
        }

        static CleanseOptionInput InputOf(UnitEntityData owner, CleanseOption option, HashSet<string> facts,
                                          Dictionary<AfflictionKinds, int> afflictionCl) {
            return new CleanseOptionInput {
                Key = option.Key, Name = option.Name, Profile = option.Profile,
                Grade = option.SpellLevel, IsSlot = option.IsSlot,
                HasFact = facts.Contains,
                Chance = (effect, bit) => CheckChance(owner, option, effect, bit, afflictionCl),
            };
        }

        // Removal chance of one effect against one affliction, from non-rolling inputs only: the ability's own
        // calculated parameters (RuleCalculateAbilityParams, no dice), the owner's skill value and the casting level
        // of the ally's affliction. An input that cannot be read stays null and CleanseChance falls back to its
        // coin-flip default (UNVERIFIED paths, per the plan: DC-check bonus = caster level, CasterLevel check
        // DC = 11 + the affliction's caster level, skill checks add the plain skill value).
        static double CheckChance(UnitEntityData owner, CleanseOption option, CleanseEffect effect, AfflictionKinds bit,
                                  Dictionary<AfflictionKinds, int> afflictionCl) {
            if (effect.Check == CleanseCheckType.None) return 1.0;
            int? dc = null, cl = null;
            try {
                var p = option.Ability.CalculateParams();
                dc = p.DC;
                cl = p.CasterLevel;
            } catch (Exception ex) {
                // Per-tick guard: unreadable ability parameters degrade this check to the unknown-chance default.
                if (!s_cleanseParamsWarned) {
                    s_cleanseParamsWarned = true;
                    Log.Engine.Warn($"Cleanse: parameters of '{option.Name}' unreadable ({ex.GetType().Name}), using the unknown-check chance");
                }
            }
            int? skill = null;
            if (effect.Check == CleanseCheckType.SkillDC || effect.Check == CleanseCheckType.SkillCasterLevel)
                skill = owner.Stats.GetStat(effect.Skill)?.ModifiedValue;
            int? affliction = afflictionCl.TryGetValue(bit, out var level) ? level : (int?)null;
            return CleanseChance.Probability(effect.Check, dc, cl, skill, affliction, effect.MaxCasterLevel);
        }

        // Highest casting level of the ally's buffs per affliction bit (the CL a dispel check is rolled against).
        static Dictionary<AfflictionKinds, int> AfflictionCasterLevels(UnitEntityData ally) {
            var result = new Dictionary<AfflictionKinds, int>();
            foreach (var buff in ally.Buffs.RawFacts) {
                var bp = buff.Blueprint;
                if (bp == null || buff.Context == null) continue;
                int level = buff.Context.Params.CasterLevel;
                foreach (var bit in CleanseTable.Bits(CleanseTable.KindsOfBuff(bp)))
                    if (!result.TryGetValue(bit, out var known) || level > known) result[bit] = level;
            }
            return result;
        }
    }
}
