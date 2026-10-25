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
                profile = ReadUncached(bp);
            } catch (Exception ex) {
                // Blueprint init guard: one malformed blueprint must not take the tick down. Cached as a
                // non-cleanse so it is not re-read every tick.
                Log.Engine.Warn($"CleanseClassifier: '{bp.name}' unreadable ({ex.GetType().Name}: {ex.Message}) - treated as no cleanse");
                profile = CleanseProfile.NotACleanse;
            }
            s_cache[bp] = profile;
            return profile;
        }

        /// <summary>The reading itself, without cache and without the failure guard (tests surface exceptions).</summary>
        internal static CleanseProfile ReadUncached(BlueprintAbility bp) { return new Reader().Read(bp); }

        /// <summary>
        /// Caster facts an effect hangs on: all of <see cref="Required"/> held and none of <see cref="Forbidden"/>.
        /// </summary>
        sealed class Gate {
            public static readonly Gate None = new Gate(new string[0], new string[0]);
            public readonly string[] Required;
            public readonly string[] Forbidden;
            public Gate(string[] required, string[] forbidden) { Required = required; Forbidden = forbidden; }
            public Gate And(Gate other) { return other == null ? this : new Gate(Concat(Required, other.Required), Concat(Forbidden, other.Forbidden)); }
        }

        static string[] Concat(string[] a, string[] b) {
            var r = new string[a.Length + b.Length];
            a.CopyTo(r, 0);
            b.CopyTo(r, a.Length);
            return r;
        }

        /// <summary>Which branches of a Conditional a friendly target can end up in.</summary>
        internal enum AllySide { Both, TrueOnly, FalseOnly }

        /// <summary>
        /// Reads the ally/enemy conditions of an AND checker. A member only an enemy satisfies (not-ally, is-enemy)
        /// closes the true branch to allies; a lone member only an ally satisfies (is-ally, not-enemy) closes the false
        /// branch. Anything else (OR, other conditions beside the ally check, no ally check) leaves both open. Dual-purpose
        /// abilities (Cleansing Flames, Water Torrent) keep their damage on the enemy side and their cure on the ally side.
        /// </summary>
        internal static AllySide AllySideOf(ConditionsChecker checker) {
            var conditions = checker?.Conditions;
            if (conditions == null || conditions.Length == 0 || checker.Operation != Operation.And) return AllySide.Both;
            bool enemyOnly = false, allyOnly = false;
            foreach (var cond in conditions) {
                if (cond is ContextConditionIsAlly ally) { if (ally.Not) enemyOnly = true; else allyOnly = true; }
                else if (cond is ContextConditionIsEnemy enemy) { if (enemy.Not) allyOnly = true; else enemyOnly = true; }
            }
            if (enemyOnly) return AllySide.FalseOnly;
            if (allyOnly && conditions.Length == 1) return AllySide.TrueOnly;
            return AllySide.Both;
        }

        /// <summary>True for the ability type whose HP heals count as the tier-2 last resort: spells only.</summary>
        internal static bool IsHealSpellType(AbilityType type) { return type == AbilityType.Spell; }

        static T Find<T>(BlueprintScriptableObject bp) where T : BlueprintComponent {
            if (bp?.ComponentsArray == null) return null;
            foreach (var c in bp.ComponentsArray)
                if (c is T typed) return typed;
            return null;
        }

        sealed class Reader {
            readonly CleanseProfile m_profile = new CleanseProfile();
            bool m_spell;

            public CleanseProfile Read(BlueprintAbility bp) {
                m_spell = IsHealSpellType(bp.Type);
                ReadEffects(bp);
                var touch = Find<AbilityEffectStickyTouch>(bp)?.TouchDeliveryAbility;
                if (touch != null) ReadEffects(touch);
                return m_profile.IsCleanse ? m_profile : CleanseProfile.NotACleanse;
            }

            void ReadEffects(BlueprintAbility bp) {
                var run = Find<AbilityEffectRunAction>(bp);
                if (run != null) WalkList(run.Actions, Gate.None);
            }

            void WalkList(ActionList list, Gate gate) {
                if (list?.Actions == null) return;
                foreach (var a in list.Actions) WalkAction(a, gate);
            }

            void WalkAction(GameAction action, Gate gate) {
                switch (action) {
                    case Conditional c: {
                        // Heal-vs-damage flip on the target's NegativeEnergyAffinity (undead): only the branch a
                        // living ally takes is read (Heal heals them; Harm hurts them, its cure is for undead).
                        bool? livingBranch = LivingBranch(c.ConditionsChecker);
                        var side = AllySideOf(c.ConditionsChecker);
                        if (livingBranch != false && side != AllySide.FalseOnly)
                            WalkList(c.IfTrue, gate.And(CasterFactGate(c.ConditionsChecker)));
                        if (livingBranch != true && side != AllySide.TrueOnly) WalkList(c.IfFalse, gate);
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
                        if (!removeBuff.ToCaster) AddBuffRemoval(removeBuff.m_Buff?.Get(), gate);
                        break;
                    case ContextActionRemoveBuffSingleStack single:
                        AddBuffRemoval(single.m_TargetBuff?.Get(), gate);
                        break;
                    case ContextActionHealStatDamage _:
                        AddRemoval(AfflictionKinds.StatDebuffAbilityDamage, gate);
                        break;
                    case ContextActionHealEnergyDrain _:
                        AddRemoval(AfflictionKinds.NegativeLevel, gate);
                        break;
                    case ContextActionHealTarget _:
                        // Only spell-slot heals (Heal family) are the last resort; Lay on Hands and other
                        // class-resource abilities stay regular cleanses (PM decision, WTW-30-26).
                        if (m_spell) m_profile.HealsHp = true;
                        break;
                    case ContextActionApplyBuff apply:
                        ReadSuppressors(apply.Buff, gate);
                        break;
                }
            }

            // For a lone "target has NegativeEnergyAffinity" check: true when the living-ally branch is IfTrue
            // (negated check), false when it is IfFalse; null for any other checker.
            static bool? LivingBranch(ConditionsChecker checker) {
                var conditions = checker?.Conditions;
                if (conditions == null || conditions.Length != 1) return null;
                var has = conditions[0] as ContextConditionHasFact;
                if (has?.Fact == null || has.Fact.AssetGuid.ToString() != ActionValidator.NegativeEnergyAffinityGuid) return null;
                return has.Not;
            }

            // Only a checker made of CasterHasFact conditions joined by AND is understood: a positive one requires the
            // fact, a negated one (Not) forbids it. Anything else is walked ungated (like DebuffClassifier does for
            // unknown conditions). Lay on Hands: "caster has MercyPoisoned" gates the poison cure; the Martyr's
            // mercies additionally say "and has no <suppress> buff".
            static Gate CasterFactGate(ConditionsChecker checker) {
                var conditions = checker?.Conditions;
                if (conditions == null || conditions.Length == 0 || checker.Operation != Operation.And) return null;
                var required = new List<string>();
                var forbidden = new List<string>();
                foreach (var cond in conditions) {
                    var has = cond as ContextConditionCasterHasFact;
                    if (has == null || has.Fact == null) return null;
                    (has.Not ? forbidden : required).Add(has.Fact.AssetGuid.ToString());
                }
                return new Gate(required.ToArray(), forbidden.ToArray());
            }

            // A dispel with an empty descriptor is a plain Dispel Magic (schools only): not a cleanse.
            // CheckBonus/ContextBonus are ignored (unverified, rare); the cap is read only when it is a constant.
            void AddDispel(ContextActionDispelMagic d, Gate gate) {
                var kinds = CleanseTable.KindsFromDescriptor((SpellDescriptor)d.Descriptor);
                if (kinds == AfflictionKinds.None) return;
                int cap = 0;
                if (d.m_UseMaxCasterLevel && d.m_MaxCasterLevel != null
                    && d.m_MaxCasterLevel.ValueType == ContextValueType.Simple)
                    cap = d.m_MaxCasterLevel.Value;
                m_profile.Add(new CleanseEffect {
                    Kinds = kinds, Check = (CleanseCheckType)(int)d.m_CheckType, Skill = d.m_Skill,
                    MaxCasterLevel = cap, GateFacts = gate.Required, ForbiddenFacts = gate.Forbidden,
                });
            }

            // A removal of one named buff: the kinds are the buff's, but it only helps an ally carrying that buff
            // (Haste strips Slow, Bless strips Bane; PM decision a).
            void AddBuffRemoval(BlueprintBuff buff, Gate gate) {
                var kinds = CleanseTable.KindsOfBuff(buff);
                if (kinds == AfflictionKinds.None) return;
                m_profile.Add(new CleanseEffect {
                    Kinds = kinds, GateFacts = gate.Required, ForbiddenFacts = gate.Forbidden,
                    RemovedBuffGuids = new[] { buff.AssetGuid.ToString() },
                });
            }

            void AddRemoval(AfflictionKinds kinds, Gate gate) {
                m_profile.Add(new CleanseEffect { Kinds = kinds, GateFacts = gate.Required, ForbiddenFacts = gate.Forbidden });
            }

            void ReadSuppressors(BlueprintBuff buff, Gate gate) {
                if (buff?.ComponentsArray == null) return;
                foreach (var comp in buff.ComponentsArray) {
                    var kinds = CleanseTable.ProtectedKinds(comp);
                    if (kinds != AfflictionKinds.None)
                        m_profile.Add(new CleanseEffect {
                            Kinds = kinds, Suppress = true, GateFacts = gate.Required, ForbiddenFacts = gate.Forbidden,
                        });
                }
            }
        }
    }
}
