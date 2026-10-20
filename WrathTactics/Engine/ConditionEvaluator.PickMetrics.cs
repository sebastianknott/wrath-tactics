using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Parts;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static partial class ConditionEvaluator {
        static bool EvaluateEnemyPick(Condition condition, UnitEntityData owner,
            Func<UnitEntityData, float> metric, bool biggest) {
            UnitEntityData pick = null;
            float best = biggest ? float.MinValue : float.MaxValue;
            foreach (var enemy in GetVisibleEnemies(owner)) {
                float val = metric(enemy);
                bool better = biggest ? val > best : val < best;
                if (better) { best = val; pick = enemy; }
            }
            if (pick == null) return false;
            if (EvaluateUnitProperty(condition, pick)) {
                LastMatchedEnemy = pick;
                return true;
            }
            return false;
        }

        static float HpPercent(UnitEntityData unit) {
            int max = unit.Stats.HitPoints.ModifiedValue;
            return max <= 0 ? 0 : (float)unit.HPLeft / max;
        }

        static float UnitAC(UnitEntityData unit) {
            return unit.Stats.AC.ModifiedValue;
        }

        static float UnitFort(UnitEntityData unit) {
            return unit.Stats.SaveFortitude.ModifiedValue;
        }

        static float UnitReflex(UnitEntityData unit) {
            return unit.Stats.SaveReflex.ModifiedValue;
        }

        static float UnitWill(UnitEntityData unit) {
            return unit.Stats.SaveWill.ModifiedValue;
        }

        static float UnitHD(UnitEntityData unit) {
            return UnitExtensions.GetHD(unit);
        }

        // Distance to the rule owner via the CurrentOwner rule-scoped static (set for
        // the whole Evaluate() lifetime, so available on both the dispatch and the
        // bucket path). NaN fails the pick closed outside a rule evaluation.
        static float DistanceToOwner(UnitEntityData unit) {
            if (unit == null || CurrentOwner == null) return float.NaN;
            return UnityEngine.Vector3.Distance(CurrentOwner.Position, unit.Position);
        }

        // Returns (currentSpellDC − target's matching save). Returns float.NaN for
        // any disqualifying condition (non-cast action, unresolvable ability, spell
        // with no save). Callers must check IsNaN before comparing.
        //
        // Save-type lookup mirrors AbilityEffectRunAction.GetSavingThrowTypeInContext:
        // MagicHackData takes precedence (Magic Deceiver fused spells and other
        // hack-altered casts carry their save type on the AbilityData, not on the
        // static blueprint component). Fallback is the blueprint's RunAction component.
        static float ComputeDCMinusSave(UnitEntityData target) {
            if (target == null || CurrentOwner == null || CurrentAction == null) return float.NaN;
            if (CurrentAction.Type != ActionType.CastSpell && CurrentAction.Type != ActionType.CastAbility)
                return float.NaN;

            var ability = ActionValidator.FindAbility(CurrentOwner, CurrentAction.AbilityId);
            if (ability == null) {
                Log.Engine.Trace($"SpellDCMinusSave: FindAbility returned null (guid={CurrentAction.AbilityId})");
                return float.NaN;
            }

            Kingmaker.EntitySystem.Stats.SavingThrowType saveType;
            string saveTypeSource;
            if (ability.MagicHackData != null) {
                saveType = ability.MagicHackData.SavingThrowType;
                saveTypeSource = "MagicHackData";
            } else {
                var runAction = ability.Blueprint
                    .GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityEffectRunAction>();
                saveType = runAction?.SavingThrowType
                    ?? Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown;
                saveTypeSource = runAction != null ? "RunAction" : "no-RunAction-component";
            }

            if (saveType == Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown) {
                Log.Engine.Trace($"SpellDCMinusSave: '{ability.Name}' ({ability.Blueprint?.name}) has no computable save type (source={saveTypeSource})");
                return float.NaN;
            }

            int dc = ability.CalculateParams().DC;
            int save = UnitExtensions.GetSave(target, saveType);
            Log.Engine.Trace($"SpellDCMinusSave: '{ability.Name}' vs {target.CharacterName}: DC {dc} - {saveType} {save} = {dc - save} (source={saveTypeSource})");
            return dc - save;
        }

        // Sentinels of the SpellResistMargin metric (chosen semantics, WTW-22):
        //  +99 = the spell cannot be resisted (blueprint has no SR check, or the target has no SR)
        //        → a ">= X" row PASSES, so SR-free spells and SR-free targets never block the rule.
        //  −99 = the target is immune to the spell (UnitPartSpellResistance.IsImmune)
        //        → a ">= X" row FAILS, the slot is not wasted on an immune target.
        internal const int SpellResistMarginPasses = 99;
        internal const int SpellResistMarginFails = -99;

        // Pure arithmetic: penetration (caster level + bonuses) − spell resistance.
        // sr <= 0 means "nothing to penetrate" and yields the passing sentinel.
        internal static int SpellResistMargin(int cl, int bonus, int sr) {
            if (sr <= 0) return SpellResistMarginPasses;
            return cl + bonus - sr;
        }

        // Sentinel layer. Order mirrors RuleSpellResistanceCheck.get_IsSpellResisted:
        // TargetIsImmune is tested BEFORE HasResistanceRoll, so immunity beats a missing SR check.
        internal static int SpellResistMargin(bool abilityChecksSr, bool targetImmune, int cl, int bonus, int sr) {
            if (targetImmune) return SpellResistMarginFails;
            if (!abilityChecksSr) return SpellResistMarginPasses;
            return SpellResistMargin(cl, bonus, sr);
        }

        // Mirrors UnitPartSpellResistance.GetValue(MechanicsContext) (IL-verified):
        // Max(0, highest applicable SR − AllSRPenalty − (Piercing metamagic ? 5 : 0) + Σ Bonuses).
        internal static int EffectiveSpellResistance(int baseSr, int allSrPenalty, bool piercing, int bonusSum) {
            return Math.Max(0, baseSr - allSrPenalty - (piercing ? 5 : 0) + bonusSum);
        }

        // SpellPenetrationMythicBonus: mythic level with the "Greater" fact, otherwise half of it (int division).
        internal static int MythicPenetration(int mythicLevel, bool hasGreater) {
            return hasGreater ? mythicLevel : mythicLevel / 2;
        }

        // Runs the sources through the game's own ModifiableBonus so ModifierDescriptor stacking is
        // identical to RuleSpellResistanceCheck.AddSpellPenetration (same-descriptor sources collapse
        // to the highest unless the descriptor is stackable, e.g. Feat / UntypedStackable). ModifiableBonus is a plain bookkeeping class: no rule is
        // triggered, nothing is rolled.
        internal static int SumPenetration(IEnumerable<KeyValuePair<ModifierDescriptor, int>> sources) {
            var bonus = new ModifiableBonus();
            foreach (var source in sources) bonus.Add(source.Key, source.Value);
            return bonus;
        }

        // ModifierDescriptor SpellPenetrationMythicBonus passes explicitly (0x19 = UntypedStackable, also the
        // [opt] default of AddSpellPenetration). UntypedStackable and Feat both stack, so Spell Penetration,
        // Greater Spell Penetration and the mythic bonus add up.
        const ModifierDescriptor MythicPenetrationDescriptor = ModifierDescriptor.UntypedStackable;

        // Penetration bonuses of the caster, read WITHOUT triggering RuleSpellResistanceCheck (which
        // rolls a d20 and raises events). Rulebook handlers add them in OnEventAboutToTrigger, so the
        // equivalent is to read the same components off the caster's active facts:
        //  - SpellPenetrationBonus (Spell Penetration / Greater Spell Penetration): Value, Descriptor, optional RequiredFact
        //  - SpellPenetrationMythicBonus: mythic level (or half of it without the Greater fact)
        // Not modelled (document gap): BindAbilitiesToClass, AlliedSpellcaster, UnitUseAbility (niche
        // class/monster mechanics that also call AddSpellPenetration), non-Simple ContextValues on
        // SpellPenetrationBonus (all vanilla ones are Simple), PrimalMagicEssence and
        // UnitPartSpellResistanceCheckReplace (stat-based replacement check; no vanilla caster here).
        static int ComputePenetrationBonus(UnitEntityData caster) {
            var sources = new List<KeyValuePair<ModifierDescriptor, int>>();
            // Facts.List already holds buffs (EntityFactsManager.Add stores every fact in m_Facts,
            // IL-verified); scanning Buffs.RawFacts as well would count a buff's bonus twice.
            CollectPenetration(caster, caster.Descriptor.Facts.List, sources);
            return SumPenetration(sources);
        }

        static void CollectPenetration(UnitEntityData caster, IEnumerable<EntityFact> facts,
            List<KeyValuePair<ModifierDescriptor, int>> sources) {
            foreach (var fact in facts) {
                var bp = fact?.Blueprint;
                if (bp == null || !fact.IsActive) continue;
                foreach (var c in bp.GetComponents<SpellPenetrationBonus>()) {
                    if (c.CheckFact && !caster.Descriptor.HasFact(c.RequiredFact)) continue;
                    if (c.Value.ValueType != ContextValueType.Simple) continue;
                    sources.Add(new KeyValuePair<ModifierDescriptor, int>(c.Descriptor, c.Value.Value));
                }
                foreach (var c in bp.GetComponents<SpellPenetrationMythicBonus>()) {
                    int mythic = caster.Progression.MythicLevel;
                    sources.Add(new KeyValuePair<ModifierDescriptor, int>(MythicPenetrationDescriptor,
                        MythicPenetration(mythic, caster.HasFact(c.Greater))));
                }
            }
        }

        // Returns penetration − target SR for the rule's own [Then] ability (see the sentinels above).
        // Mirrors the engine's check order (IL RuleSpellResistanceCheck.OnTrigger / get_IsSpellResisted):
        //  immune → fail; blueprint SpellResistance false → pass; SR from UnitPartSpellResistance on the
        //  target; penetration = AbilityData.CalculateParams().CasterLevel + ComputePenetrationBonus.
        // NaN (row fails closed) for non-Cast actions and unresolvable abilities, like ComputeDCMinusSave.
        static float ComputeSpellResistMargin(UnitEntityData target) {
            if (target == null || CurrentOwner == null || CurrentAction == null) return float.NaN;
            if (CurrentAction.Type != ActionType.CastSpell && CurrentAction.Type != ActionType.CastAbility)
                return float.NaN;

            var ability = ActionValidator.FindAbility(CurrentOwner, CurrentAction.AbilityId);
            if (ability == null) {
                Log.Engine.Trace($"SpellResistMargin: FindAbility returned null (guid={CurrentAction.AbilityId})");
                return float.NaN;
            }

            // RuleSpellResistanceCheck checks the deliver blueprint of the executing ability, else the spell itself.
            var bp = ability.GetDeliverBlueprint(false) ?? ability.Blueprint;
            var part = target.Get<UnitPartSpellResistance>();
            bool immune = part != null && IsImmuneToCast(part, ability);
            bool checksSr = bp.SpellResistance
                && !(bp.IgnoreSpellResistanceForAlly && !CurrentOwner.Group.IsEnemy(target.Group));

            int cl = 0, bonus = 0, sr = 0;
            if (!immune && checksSr && part != null) {
                bool piercing = ability.MetamagicData != null && ability.MetamagicData.Has(Metamagic.Piercing);
                int bonusSum = 0;
                foreach (var b in part.Bonuses.Values) bonusSum += b;
                sr = EffectiveSpellResistance(part.GetValue(bp, CurrentOwner), part.AllSRPenalty, piercing, bonusSum);
                if (sr > 0) {
                    cl = ability.CalculateParams().CasterLevel;
                    bonus = ComputePenetrationBonus(CurrentOwner);
                }
            }

            int margin = SpellResistMargin(checksSr, immune, cl, bonus, sr);
            Log.Engine.Trace($"SpellResistMargin: '{ability.Name}' vs {target.CharacterName}: CL {cl} + bonus {bonus} - SR {sr} = {margin} (srCheck={checksSr}, immune={immune})");
            return margin;
        }

        // UnitPartSpellResistance.IsImmune(BlueprintAbility, caster) passes no descriptor, and CanApply then skips
        // every SpellDescriptor immunity (IL: `if (!contextDescriptors.HasValue) return false`), so undead or
        // constructs looked open to mind-affecting spells. The cast goes through RuleSpellResistanceCheck ->
        // IsImmune(MechanicsContext), which hands CanApply the source AbilityData and context.SpellDescriptor.
        // Mirror that call with the AbilityData and its SpellDescriptor.
        static bool IsImmuneToCast(UnitPartSpellResistance part, AbilityData ability) {
            var descriptor = ability.SpellDescriptor;
            foreach (var immunity in part.m_SpellImmunities)
                if (part.CanApply(immunity, ability, CurrentOwner, descriptor, null)) return true;
            return false;
        }

        // Engine-authoritative best attack bonus across all living party members.
        // RuleCalculateAttackBonusWithoutTarget returns the same AB the game uses at
        // attack time minus target-side modifiers (flanking, bane, etc.) — it includes
        // BAB, stat mod (correctly picked by weapon type), weapon enhancement, feats,
        // and active buffs. NaN on empty / fully-dead party.
        static float PartyBestAB(UnitEntityData owner) {
            int best = int.MinValue;
            foreach (var ally in GetAllPartyMembers(owner)) {
                int ab = ComputeAB(ally);
                if (ab > best) best = ab;
            }
            return best == int.MinValue ? float.NaN : (float)best;
        }

        // Per-ally AB calculation. Returns int.MinValue for ineligible (dead, no weapon,
        // not in game) so the caller can detect "no usable AB" without a separate flag.
        static int ComputeAB(UnitEntityData ally) {
            if (ally == null || !ally.IsInGame) return int.MinValue;
            if (ally.Descriptor?.State?.IsFinallyDead ?? false) return int.MinValue;

            var weapon = ally.Body?.PrimaryHand?.MaybeWeapon
                      ?? ally.Body?.SecondaryHand?.MaybeWeapon
                      ?? ally.Body?.EmptyHandWeapon;
            if (weapon == null) return int.MinValue;

            var rule = Rulebook.Trigger(new RuleCalculateAttackBonusWithoutTarget(ally, weapon, 0));
            return rule.Result;
        }

        // Computes ab - enemy.AC for the ABMinusAC condition property. `allyPinUniqueId`
        // is optional: empty / null ⇒ use the party-best AB (cached across the rule's
        // enemy scan); set ⇒ AllyProvider.Resolve and use that specific ally's AB.
        // Pinned-AB is NOT cached because each pin is a different ally and the helper is
        // called once per enemy — caching would need a (pin, AB) dict that pays for
        // itself only on multi-enemy bucket scans. Skipping it is fine for rules with
        // single-target picks (the common case for "Wenduag low-AB → Sosiel buffs her").
        // Rule-scoped: CurrentOwner is the rule's owning unit (set in Evaluate, cleared in finally).
        // NaN when the party / pinned ally is empty/dead/weaponless or the enemy is null.
        static float ComputeABMinusAC(UnitEntityData enemy, string allyPinUniqueId) {
            if (enemy == null || CurrentOwner == null) return float.NaN;

            float ab;
            if (string.IsNullOrEmpty(allyPinUniqueId)) {
                if (float.IsNaN(CurrentPartyBestAB))
                    CurrentPartyBestAB = PartyBestAB(CurrentOwner);
                ab = CurrentPartyBestAB;
            } else {
                var pinned = AllyProvider.Resolve(allyPinUniqueId);
                if (pinned == null) {
                    Log.Engine.Trace($"ABMinusAC: ally pin '{allyPinUniqueId}' did not resolve");
                    return float.NaN;
                }
                int rawAb = ComputeAB(pinned);
                ab = rawAb == int.MinValue ? float.NaN : (float)rawAb;
            }

            if (float.IsNaN(ab)) return float.NaN;
            int ac = enemy.Stats.AC.ModifiedValue;
            float margin = ab - ac;
            string source = string.IsNullOrEmpty(allyPinUniqueId) ? "partyBestAB" : $"ally='{allyPinUniqueId}'";
            Log.Engine.Trace($"ABMinusAC: {enemy.CharacterName} AC={ac}, AB={ab} ({source}) -> margin={margin}");
            return margin;
        }

        // Computes max(GetEffectiveHD(member)) over Player.Party. Player.Party
        // (NOT PartyAndPets) is intentional: pets have separate level progression
        // curves and a high-HD Eidolon / Drake / Animal Companion would skew the
        // max. PartyLevel here means "the player squad's level" — pets are
        // explicitly excluded. Cached once per Evaluate call via CurrentPartyMaxLevel.
        static int ComputePartyMaxEffectiveLevel() {
            if (CurrentPartyMaxLevel >= 0) return CurrentPartyMaxLevel;
            int max = 0;
            var party = Game.Instance?.Player?.Party;
            if (party != null) {
                foreach (var member in party) {
                    int eff = UnitExtensions.GetEffectiveHD(member);
                    if (eff > max) max = eff;
                }
            }
            CurrentPartyMaxLevel = max;
            return max;
        }

        // Computes enemyEffectiveHD - partyMaxEffectiveLevel for the
        // EnemyHDMinusPartyLevel condition property. Mythic-inclusive on both
        // sides so the margin stays meaningful through late Wrath. Returns NaN
        // when the party is empty (theoretical — not reachable mid-combat) so
        // the row fails-closed rather than reading 0.
        static float ComputeHDMinusPartyLevel(UnitEntityData enemy) {
            if (enemy == null) return float.NaN;
            int partyMax = ComputePartyMaxEffectiveLevel();
            if (partyMax == 0) return float.NaN;
            int enemyHD = UnitExtensions.GetEffectiveHD(enemy);
            float margin = enemyHD - partyMax;
            Log.Engine.Trace($"EnemyHDMinusPartyLevel: {enemy.CharacterName} HD={enemyHD} vs PartyMax={partyMax} -> margin={margin}");
            return margin;
        }

        static Func<UnitEntityData, float> PickMetric(ConditionSubject s, out bool biggest) {
            biggest = false;
            switch (s) {
                case ConditionSubject.EnemyBiggestThreat:  biggest = true;  return e => ThreatCalculator.Calculate(e);
                case ConditionSubject.EnemyLowestThreat:   biggest = false; return e => ThreatCalculator.Calculate(e);
                case ConditionSubject.EnemyHighestHp:      biggest = true;  return HpPercent;
                case ConditionSubject.EnemyLowestHp:       biggest = false; return HpPercent;
                case ConditionSubject.EnemyHighestAC:      biggest = true;  return UnitAC;
                case ConditionSubject.EnemyLowestAC:       biggest = false; return UnitAC;
                case ConditionSubject.EnemyHighestFort:    biggest = true;  return UnitFort;
                case ConditionSubject.EnemyLowestFort:     biggest = false; return UnitFort;
                case ConditionSubject.EnemyHighestReflex:  biggest = true;  return UnitReflex;
                case ConditionSubject.EnemyLowestReflex:   biggest = false; return UnitReflex;
                case ConditionSubject.EnemyHighestWill:    biggest = true;  return UnitWill;
                case ConditionSubject.EnemyLowestWill:     biggest = false; return UnitWill;
                case ConditionSubject.EnemyHighestHD:      biggest = true;  return UnitHD;
                case ConditionSubject.EnemyLowestHD:       biggest = false; return UnitHD;
                case ConditionSubject.EnemyNearest:        biggest = false; return DistanceToOwner;
                default:                                   return null;
            }
        }
    }
}
