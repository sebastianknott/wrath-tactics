using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.ElementsSystem;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics.Conditions;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// Reads a BlueprintAbility into a CleanseProfile (WTW-30 cleanse): which afflictions it removes or holds off,
    /// by which check, and under which caster facts. Blueprint reading only; the chance lives in CleanseChance and
    /// the ordering in CleanseRanking. Results (including "not a cleanse") are cached per blueprint reference.
    ///
    /// The walk follows what the engine executes: RunAction -> Conditional / saving throw branches ->
    /// ContextActionDispelMagic (Descriptor + m_CheckType), RemoveBuffsByDescriptor, RemoveBuff(SingleStack),
    /// HealStatDamage, HealEnergyDrain; ContextActionApplyBuff buffs are read for SuppressBuffs,
    /// BuffDescriptorImmunity and AddConditionImmunity (suppressors). A Conditional whose checker is made only of
    /// ContextConditionCasterHasFact gates its true branch on those facts (Lay on Hands mercies).
    /// </summary>
    internal static class CleanseClassifier {
        sealed class ReferenceComparer : IEqualityComparer<BlueprintAbility> {
            public bool Equals(BlueprintAbility x, BlueprintAbility y) { return ReferenceEquals(x, y); }
            public int GetHashCode(BlueprintAbility obj) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj); }
        }

        static readonly Dictionary<BlueprintAbility, CleanseProfile> s_cache =
            new Dictionary<BlueprintAbility, CleanseProfile>(new ReferenceComparer());

        internal static CleanseProfile Get(BlueprintAbility bp) {
            if (bp == null) return CleanseProfile.NotACleanse;
            if (s_cache.TryGetValue(bp, out var cached)) return cached;
            CleanseProfile profile;
            try {
                profile = new Reader().Read(bp);
            } catch (Exception ex) {
                // Blueprint init guard: one malformed blueprint must not take the tick down. Cached as a
                // non-cleanse so it is not re-read every tick.
                Log.Engine.Warn($"CleanseClassifier: '{bp.name}' unreadable ({ex.GetType().Name}: {ex.Message}) - treated as no cleanse");
                profile = CleanseProfile.NotACleanse;
            }
            s_cache[bp] = profile;
            return profile;
        }

        static T Find<T>(BlueprintScriptableObject bp) where T : BlueprintComponent {
            if (bp?.ComponentsArray == null) return null;
            foreach (var c in bp.ComponentsArray)
                if (c is T typed) return typed;
            return null;
        }

        sealed class Reader {
            readonly CleanseProfile m_profile = new CleanseProfile();
            static readonly string[] NoGate = new string[0];

            public CleanseProfile Read(BlueprintAbility bp) {
                ReadEffects(bp);
                var touch = Find<AbilityEffectStickyTouch>(bp)?.TouchDeliveryAbility;
                if (touch != null) ReadEffects(touch);
                return m_profile.IsCleanse ? m_profile : CleanseProfile.NotACleanse;
            }

            void ReadEffects(BlueprintAbility bp) {
                var run = Find<AbilityEffectRunAction>(bp);
                if (run != null) WalkList(run.Actions, NoGate);
            }

            void WalkList(ActionList list, string[] gate) {
                if (list?.Actions == null) return;
                foreach (var a in list.Actions) WalkAction(a, gate);
            }

            void WalkAction(GameAction action, string[] gate) {
                switch (action) {
                    case Conditional c: {
                        var facts = CasterFactGate(c.ConditionsChecker);
                        WalkList(c.IfTrue, facts == null ? gate : Concat(gate, facts));
                        WalkList(c.IfFalse, gate);
                        break;
                    }
                    case ContextActionSavingThrow st:
                        WalkList(st.Actions, gate);
                        break;
                    case ContextActionConditionalSaved cs:
                        WalkList(cs.Failed, gate);
                        WalkList(cs.Succeed, gate);
                        break;
                    case ContextActionDispelMagic dispel:
                        AddDispel(dispel, gate);
                        break;
                    case ContextActionRemoveBuffsByDescriptor byDescriptor:
                        AddRemoval(CleanseTable.KindsFromDescriptor((SpellDescriptor)byDescriptor.SpellDescriptor), gate);
                        break;
                    case ContextActionRemoveBuff removeBuff:
                        // ToCaster strips the caster's own buff (a cost, not a cure of the target).
                        if (!removeBuff.ToCaster) AddRemoval(CleanseTable.KindsOfBuff(removeBuff.m_Buff?.Get()), gate);
                        break;
                    case ContextActionRemoveBuffSingleStack single:
                        AddRemoval(CleanseTable.KindsOfBuff(single.m_TargetBuff?.Get()), gate);
                        break;
                    case ContextActionHealStatDamage _:
                        AddRemoval(AfflictionKinds.StatDebuffAbilityDamage, gate);
                        break;
                    case ContextActionHealEnergyDrain _:
                        AddRemoval(AfflictionKinds.NegativeLevel, gate);
                        break;
                    case ContextActionHealTarget _:
                        m_profile.HealsHp = true;
                        break;
                    case ContextActionApplyBuff apply:
                        ReadSuppressors(apply.Buff, gate);
                        break;
                }
            }

            // Only a checker made of positive CasterHasFact conditions joined by AND is understood; anything
            // else is walked ungated (like DebuffClassifier does for unknown conditions).
            static string[] CasterFactGate(ConditionsChecker checker) {
                var conditions = checker?.Conditions;
                if (conditions == null || conditions.Length == 0 || checker.Operation != Operation.And) return null;
                var facts = new List<string>();
                foreach (var cond in conditions) {
                    var has = cond as ContextConditionCasterHasFact;
                    if (has == null || has.Not || has.Fact == null) return null;
                    facts.Add(has.Fact.AssetGuid.ToString());
                }
                return facts.ToArray();
            }

            static string[] Concat(string[] a, string[] b) {
                var r = new string[a.Length + b.Length];
                a.CopyTo(r, 0);
                b.CopyTo(r, a.Length);
                return r;
            }

            // A dispel with an empty descriptor is a plain Dispel Magic (schools only): not a cleanse.
            // CheckBonus/ContextBonus are ignored (unverified, rare); the cap is read only when it is a constant.
            void AddDispel(ContextActionDispelMagic d, string[] gate) {
                var kinds = CleanseTable.KindsFromDescriptor((SpellDescriptor)d.Descriptor);
                if (kinds == AfflictionKinds.None) return;
                int cap = 0;
                if (d.m_UseMaxCasterLevel && d.m_MaxCasterLevel != null
                    && d.m_MaxCasterLevel.ValueType == ContextValueType.Simple)
                    cap = d.m_MaxCasterLevel.Value;
                m_profile.Add(new CleanseEffect {
                    Kinds = kinds, Check = (CleanseCheckType)(int)d.m_CheckType, Skill = d.m_Skill,
                    MaxCasterLevel = cap, GateFacts = gate,
                });
            }

            void AddRemoval(AfflictionKinds kinds, string[] gate) {
                m_profile.Add(new CleanseEffect { Kinds = kinds, GateFacts = gate });
            }

            void ReadSuppressors(BlueprintBuff buff, string[] gate) {
                if (buff?.ComponentsArray == null) return;
                foreach (var comp in buff.ComponentsArray) {
                    var kinds = AfflictionKinds.None;
                    switch (comp) {
                        case SuppressBuffs sup:
                            kinds = CleanseTable.KindsFromDescriptor((SpellDescriptor)sup.Descriptor);
                            if (sup.m_Buffs != null)
                                foreach (var r in sup.m_Buffs) kinds |= CleanseTable.KindsOfBuff(r?.Get());
                            break;
                        case BuffDescriptorImmunity imm:
                            kinds = CleanseTable.KindsFromDescriptor((SpellDescriptor)imm.Descriptor);
                            break;
                        case AddConditionImmunity cond:
                            kinds = CleanseTable.KindsFromCondition(cond.Condition);
                            break;
                    }
                    if (kinds != AfflictionKinds.None)
                        m_profile.Add(new CleanseEffect { Kinds = kinds, Suppress = true, GateFacts = gate });
                }
            }
        }
    }
}
