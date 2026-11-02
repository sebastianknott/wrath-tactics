using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
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
            // The game adds situational save bonuses (vs mind-affecting, fear, school,
            // arcane spells, ...) only inside RuleSavingThrow, so the stat's
            // ModifiedValue alone overstates the margin. The probe re-reads those
            // components off the target's facts; it never rolls.
            var probe = SpellSaveBonusProbe.Probe(CurrentOwner, target, ability, saveType);
            int margin = SpellDcMargin.Margin(dc, probe.CasterDcBonus, save, probe.SituationalBonus);
            Log.Engine.Trace($"SpellDCMinusSave: '{ability.Name}' vs {target.CharacterName}: DC {dc}"
                + (probe.CasterDcBonus != 0 ? $" +{probe.CasterDcBonus} caster" : "")
                + $" - {saveType} base {save}"
                + (probe.SituationalBonus != 0 ? $" - situational {probe.SituationalBonus}" : "")
                + $" = {margin} (source={saveTypeSource})"
                + (string.IsNullOrEmpty(probe.Breakdown) ? "" : $" [{probe.Breakdown.TrimStart()}]"));
            return margin;
        }

        // Engine-authoritative best attack bonus against `enemy` across all living party
        // members. Uses RuleCalculateAttackBonus (with target), the same pure calculation the
        // game runs for PowerAttack previews: it adds the target-dependent modifiers
        // (shoot-into-melee -4, concealment, flanking) on top of the WithoutTarget result.
        // NoAb on empty / fully-dead party.
        static int PartyBestAB(UnitEntityData owner, UnitEntityData enemy) {
            var abs = new List<int>();
            foreach (var ally in GetAllPartyMembers(owner))
                abs.Add(CachedAB(ally, enemy));
            return AbMargin.BestAb(abs);
        }

        // ComputeAB behind the per-evaluation (attacker, enemy) cache.
        static int CachedAB(UnitEntityData ally, UnitEntityData enemy) {
            return AbMargin.GetOrCompute(CurrentABCache, ally, enemy, ComputeAB);
        }

        // Per-ally AB against a specific enemy. Returns AbMargin.NoAb for ineligible (dead,
        // no weapon, not in game) so the caller can detect "no usable AB" without a separate
        // flag. Weapon: main hand, else off hand, else unarmed. The rule is a pure
        // calculation (no dice, no log), safe to trigger standalone.
        static int ComputeAB(UnitEntityData ally, UnitEntityData enemy) {
            if (ally == null || enemy == null || !ally.IsInGame) return AbMargin.NoAb;
            if (ally.Descriptor?.State?.IsFinallyDead ?? false) return AbMargin.NoAb;

            var weapon = ally.Body?.PrimaryHand?.MaybeWeapon
                      ?? ally.Body?.SecondaryHand?.MaybeWeapon
                      ?? ally.Body?.EmptyHandWeapon;
            if (weapon == null) return AbMargin.NoAb;

            var rule = Rulebook.Trigger(new RuleCalculateAttackBonus(ally, enemy, weapon, 0));
            return rule.Result;
        }

        // Computes ab - enemy.AC for the ABMinusAC condition property, where ab is the
        // attack bonus AGAINST THIS ENEMY (see PartyBestAB). `allyPinUniqueId` is optional:
        // empty / null => best AB among the party; set => AllyProvider.Resolve and use that
        // specific ally's AB. Both paths go through the (attacker, enemy) cache, so enemy
        // lists do not re-trigger the rule for the same pair within one Evaluate.
        // Rule-scoped: CurrentOwner is the rule's owning unit (set in Evaluate, cleared in finally).
        // NaN when the party / pinned ally is empty/dead/weaponless or the enemy is null.
        static float ComputeABMinusAC(UnitEntityData enemy, string allyPinUniqueId) {
            if (enemy == null || CurrentOwner == null) return float.NaN;

            int ab;
            if (string.IsNullOrEmpty(allyPinUniqueId)) {
                ab = PartyBestAB(CurrentOwner, enemy);
            } else {
                var pinned = AllyProvider.Resolve(allyPinUniqueId);
                if (pinned == null) {
                    Log.Engine.Trace($"ABMinusAC: ally pin '{allyPinUniqueId}' did not resolve");
                    return float.NaN;
                }
                ab = CachedAB(pinned, enemy);
            }

            int ac = enemy.Stats.AC.ModifiedValue;
            float margin = AbMargin.Margin(ab, ac);
            if (float.IsNaN(margin)) return float.NaN;
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
