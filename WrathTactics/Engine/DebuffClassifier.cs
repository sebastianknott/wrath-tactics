using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Actions;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using WrathTactics.Logging;
using WrathTactics.Models;

using AbilityTargetType = Kingmaker.UnitLogic.Abilities.Components.TargetType;

namespace WrathTactics.Engine {
    /// <summary>
    /// Reads a BlueprintAbility into a DebuffProfile (WTW-30): which effect groups it lowers, how much, and
    /// whether it can hit allies. Blueprint reading only; ranking lives in DebuffMath. Results (including
    /// "not a debuff") are cached per blueprint GUID and filled lazily, so the first query of an ability pays
    /// for the walk and later ticks are dictionary lookups.
    ///
    /// The walk follows what the engine executes: RunAction -> Conditional / saving throw / ConditionalSaved
    /// branches -> ContextActionApplyBuff -> the buff's stat bonuses and conditions, recursing through
    /// AddFactContextActions (Glitterdust's blindness lives two buffs deep).
    /// </summary>
    internal static class DebuffClassifier {
        static readonly Dictionary<string, DebuffProfile> s_cache = new Dictionary<string, DebuffProfile>();

        internal static DebuffProfile Get(BlueprintAbility bp) {
            if (bp == null) return DebuffProfile.NotADebuff;
            string guid = bp.AssetGuid.ToString();
            if (s_cache.TryGetValue(guid, out var cached)) return cached;
            DebuffProfile profile;
            try {
                profile = new Reader(bp).Read();
            } catch (Exception ex) {
                // Blueprint init guard: one malformed blueprint must not take the tick down. Cached as a
                // non-debuff so it is not re-read every tick.
                Log.Engine.Warn($"DebuffClassifier: '{bp.name}' unreadable ({ex.GetType().Name}: {ex.Message}) - treated as no debuff");
                profile = DebuffProfile.NotADebuff;
            }
            s_cache[guid] = profile;
            return profile;
        }

        static T Find<T>(BlueprintScriptableObject bp) where T : BlueprintComponent {
            if (bp?.ComponentsArray == null) return null;
            foreach (var c in bp.ComponentsArray)
                if (c is T typed) return typed;
            return null;
        }

        sealed class Reader {
            readonly BlueprintAbility m_bp;
            readonly BlueprintAbility m_touch;
            readonly DebuffProfile m_profile = new DebuffProfile();
            readonly HashSet<string> m_visitedBuffs = new HashSet<string>();
            bool m_spawnsArea;
            bool m_coneOrLine;
            bool m_areaUnfiltered;
            bool m_sawSave;

            public Reader(BlueprintAbility bp) {
                m_bp = bp;
                m_touch = Find<AbilityEffectStickyTouch>(bp)?.TouchDeliveryAbility;
            }

            public DebuffProfile Read() {
                m_profile.ChecksSr = m_bp.SpellResistance || (m_touch != null && m_touch.SpellResistance);
                ReadTargeting(m_bp);
                if (m_touch != null) ReadTargeting(m_touch);
                ReadEffects(m_bp);
                if (m_touch != null) ReadEffects(m_touch);

                if (m_profile.Entries.Count == 0) return DebuffProfile.NotADebuff;
                m_profile.IsDebuff = true;
                m_profile.AllySafe = DecideAllySafe();
                return m_profile;
            }

            void ReadTargeting(BlueprintAbility bp) {
                var around = Find<AbilityTargetsAround>(bp);
                if (around != null) {
                    m_profile.Area = DebuffAreaKind.AroundEnemy;
                    m_profile.RadiusMeters = around.m_Radius.Meters;
                    if (around.m_TargetType != AbilityTargetType.Enemy) m_areaUnfiltered = true;
                }
                var chain = Find<AbilityDeliverChain>(bp);
                if (chain != null) {
                    m_profile.Area = DebuffAreaKind.ChainEnemy;
                    if (chain.m_TargetType != AbilityTargetType.Enemy) m_areaUnfiltered = true;
                }
                var aoe = Find<AbilityAoERadius>(bp);
                if (aoe != null) {
                    if (m_profile.Area == DebuffAreaKind.Single) {
                        m_profile.Area = DebuffAreaKind.AroundEnemy;
                        m_profile.RadiusMeters = aoe.m_Radius.Meters;
                    }
                    if (aoe.m_TargetType != AbilityTargetType.Enemy) m_areaUnfiltered = true;
                }
                var projectile = Find<AbilityDeliverProjectile>(bp);
                if (projectile != null && projectile.Type != AbilityProjectileType.Simple) m_coneOrLine = true;
            }

            bool DecideAllySafe() {
                bool canEnemies = m_bp.CanTargetEnemies || (m_touch != null && m_touch.CanTargetEnemies);
                bool canPoint = m_bp.CanTargetPoint;
                if (!canEnemies) { m_profile.UnsafeReason = "cannot target enemies"; return false; }
                if (m_spawnsArea) { m_profile.UnsafeReason = "leaves an area effect behind"; return false; }
                if (m_coneOrLine) { m_profile.UnsafeReason = "cone or line"; return false; }
                if (m_profile.Area != DebuffAreaKind.Single && m_areaUnfiltered) {
                    m_profile.UnsafeReason = "area not limited to enemies"; return false;
                }
                if (m_profile.Area == DebuffAreaKind.Single && canPoint) {
                    m_profile.UnsafeReason = "point target"; return false;
                }
                return true;
            }

            void ReadEffects(BlueprintAbility bp) {
                var run = Find<AbilityEffectRunAction>(bp);
                if (run == null) return;
                if (run.SavingThrowType != SavingThrowType.Unknown && !m_sawSave) {
                    m_profile.Save = run.SavingThrowType;
                    m_sawSave = true;
                }
                var ctx = new WalkContext { Ability = bp, Gate = SaveGate.Always };
                WalkList(run.Actions, ctx);
            }

            sealed class WalkContext {
                public BlueprintAbility Ability;
                public SaveGate Gate;
                public ContextActionApplyBuff Apply;
                public BlueprintBuff Buff;
            }

            void WalkList(ActionList list, WalkContext ctx) {
                if (list?.Actions == null) return;
                foreach (var a in list.Actions) WalkAction(a, ctx);
            }

            void WalkAction(GameAction action, WalkContext ctx) {
                switch (action) {
                    case Conditional c:
                        WalkList(c.IfTrue, ctx);
                        WalkList(c.IfFalse, ctx);
                        break;
                    case ContextActionSavingThrow st:
                        if (!m_sawSave && st.Type != SavingThrowType.Unknown) {
                            m_profile.Save = st.Type;
                            m_sawSave = true;
                        }
                        WalkList(st.Actions, ctx);
                        break;
                    case ContextActionConditionalSaved cs:
                        WalkList(cs.Failed, With(ctx, SaveGate.OnFail));
                        WalkList(cs.Succeed, With(ctx, SaveGate.OnSuccess));
                        break;
                    case ContextActionApplyBuff apply:
                        AnalyzeBuff(apply.Buff, ctx, apply);
                        break;
                    case Demoralize dem:
                        // Intimidate check, not a save: the weaker buff stands for the whole option, the
                        // greater one is only remembered so "already affected" sees it.
                        AnalyzeBuff(dem.m_Buff?.Get(), ctx, null);
                        var greater = dem.m_GreaterBuff?.Get();
                        if (greater != null && m_profile.Entries.Count > 0) m_profile.AppliedBuffGuids.Add(greater.AssetGuid.ToString());
                        break;
                    case ContextActionCombatManeuver man:
                        AddManeuver(man.Type);
                        WalkList(man.OnSuccess, ctx);
                        break;
                    case ContextActionSpawnAreaEffect _:
                        m_spawnsArea = true;
                        break;
                    case BuffActionAddStatBonus bonus:
                        AddStatValue(bonus.Stat, bonus.Value, ctx);
                        break;
                }
            }

            static WalkContext With(WalkContext ctx, SaveGate gate) {
                return new WalkContext { Ability = ctx.Ability, Gate = gate, Apply = ctx.Apply, Buff = ctx.Buff };
            }

            void AddManeuver(CombatManeuver maneuver) {
                if (maneuver == CombatManeuver.SunderArmor) {
                    m_profile.Entries.Add(DebuffConditionTable.SunderArmorEntry());
                    return;
                }
                var cond = DebuffConditionTable.ManeuverCondition(maneuver);
                if (cond == null) return;
                foreach (var e in DebuffConditionTable.ConditionEntries(cond.Value)) {
                    e.DurationRounds = DebuffMath.UnknownDurationRounds;
                    if (e.Condition == UnitCondition.Prone) e.DurationRounds = DebuffMath.ProneLockRounds;
                    m_profile.Entries.Add(e);
                }
            }

            // Reads one buff into entries. Entries collected from this buff share its duration, its
            // ends-on-damage trait and its save-each-round trait, which are only known after the scan.
            void AnalyzeBuff(BlueprintBuff buff, WalkContext ctx, ContextActionApplyBuff apply) {
                if (buff == null) return;
                string guid = buff.AssetGuid.ToString();
                if (!m_visitedBuffs.Add(guid)) return;

                var added = new List<DebuffEntry>();
                bool endsOnDamage = false, saveEachRound = false;
                var inner = new WalkContext { Ability = ctx.Ability, Gate = ctx.Gate, Apply = apply, Buff = buff };
                int before = m_profile.Entries.Count;

                foreach (var comp in buff.ComponentsArray) {
                    switch (comp) {
                        case AddStatBonus sb when sb.Value < 0:
                            AddStat(added, sb.Stat, -sb.Value, null, 0f, ctx);
                            break;
                        case AddGenericStatBonus gb when gb.Value < 0:
                            AddStat(added, gb.Stat, -gb.Value, null, 0f, ctx);
                            break;
                        case AddContextStatBonus cb when cb.Multiplier < 0:
                            AddContextStat(added, buff, ctx.Ability, cb, ctx);
                            break;
                        case BuffAllSavesBonus saves when saves.Value < 0:
                            AddStat(added, StatType.SaveWill, -saves.Value, null, 0f, ctx);
                            AddStat(added, StatType.SaveReflex, -saves.Value, null, 0f, ctx);
                            AddStat(added, StatType.SaveFortitude, -saves.Value, null, 0f, ctx);
                            break;
                        case AddCondition ac:
                            AddCondition(added, ac.Condition, ctx);
                            break;
                        case BuffStatusCondition bsc:
                            AddCondition(added, bsc.Condition, ctx);
                            if (bsc.SaveEachRound) saveEachRound = true;
                            break;
                        case AddIncomingDamageTrigger trig when RemovesSelf(trig.Actions):
                            endsOnDamage = true;
                            break;
                        case AddFactContextActions facts:
                            // Activated and NewRound can themselves apply buffs (Glitterdust) or add stat bonuses (Ray of Enfeeblement).
                            WalkList(facts.Activated, inner);
                            WalkList(facts.NewRound, inner);
                            break;
                    }
                }
                m_profile.Entries.AddRange(added);

                bool any = m_profile.Entries.Count > before;
                if (!any) return;
                m_profile.AppliedBuffGuids.Add(guid);
                for (int i = before; i < m_profile.Entries.Count; i++) {
                    var e = m_profile.Entries[i];
                    if (e.BuffGuid.Length == 0) e.BuffGuid = guid;
                    if (endsOnDamage) e.EndsOnDamage = true;
                    if (saveEachRound) e.SaveEachRound = true;
                    if (e.DurationRounds < 0f && e.Duration == null && !e.Permanent) ApplyDuration(e, apply);
                }
            }

            static void ApplyDuration(DebuffEntry e, ContextActionApplyBuff apply) {
                if (apply == null) { e.DurationRounds = DebuffMath.UnknownDurationRounds; return; }
                if (apply.Permanent) { e.Permanent = true; e.DurationRounds = DebuffMath.MaxLockRounds; return; }
                if (apply.UseDurationSeconds) { e.DurationRounds = Math.Max(1f, apply.DurationSeconds / 6f); return; }
                e.Duration = apply.DurationValue;
            }

            static bool RemovesSelf(ActionList list) {
                if (list?.Actions == null) return false;
                foreach (var a in list.Actions) if (a is ContextActionRemoveSelf) return true;
                return false;
            }

            void AddCondition(List<DebuffEntry> into, UnitCondition condition, WalkContext ctx) {
                foreach (var e in DebuffConditionTable.ConditionEntries(condition)) {
                    e.Gate = ctx.Gate;
                    into.Add(e);
                }
            }

            void AddStat(List<DebuffEntry> into, StatType stat, float magnitude, RankSpec rank, float rankMult, WalkContext ctx) {
                var group = DebuffConditionTable.GroupForStat(stat);
                if (group == DebuffGroup.None) return;
                into.Add(new DebuffEntry {
                    Group = group, Kind = DebuffEntryKind.Stat, Stat = stat, Fixed = magnitude,
                    Rank = rank, RankMult = rankMult, Gate = ctx.Gate,
                });
            }

            void AddContextStat(List<DebuffEntry> into, BlueprintBuff buff, BlueprintAbility ability,
                                AddContextStatBonus cb, WalkContext ctx) {
                var group = DebuffConditionTable.GroupForStat(cb.Stat);
                if (group == DebuffGroup.None) return;
                var v = Linear.Eval(cb.Value, ability, buff);
                // The bonus is Value * Multiplier with a negative multiplier: the debuff size is the positive product.
                var entry = new DebuffEntry {
                    Group = group, Kind = DebuffEntryKind.Stat, Stat = cb.Stat, Gate = ctx.Gate,
                    Fixed = v.Fixed * -cb.Multiplier, Rank = v.Rank, RankMult = v.RankCoef * -cb.Multiplier,
                };
                if (!v.Known) { entry.Fixed = DebuffMath.UnknownMagnitude; entry.UnknownMagnitude = true; }
                into.Add(entry);
            }

            // BuffActionAddStatBonus: the value is added as is (IL), so a debuff carries a negative value.
            void AddStatValue(StatType stat, ContextValue value, WalkContext ctx) {
                var group = DebuffConditionTable.GroupForStat(stat);
                if (group == DebuffGroup.None) return;
                var v = Linear.Eval(value, ctx.Ability, ctx.Buff);
                if (!v.Known) {
                    m_profile.Entries.Add(new DebuffEntry {
                        Group = group, Kind = DebuffEntryKind.Stat, Stat = stat, Gate = ctx.Gate,
                        Fixed = DebuffMath.UnknownMagnitude, UnknownMagnitude = true,
                    });
                    return;
                }
                // Debuff when the (average) value is negative; a rank term counts at caster level 1 for the sign test.
                float probe = v.Fixed + v.RankCoef * (v.Rank != null && v.Rank.Valid ? DebuffMath.ApplyRank(v.Rank, 1) : 0);
                if (probe >= 0f && v.Fixed >= 0f && v.RankCoef >= 0f) return;
                m_profile.Entries.Add(new DebuffEntry {
                    Group = group, Kind = DebuffEntryKind.Stat, Stat = stat, Gate = ctx.Gate,
                    Fixed = -v.Fixed, Rank = v.Rank, RankMult = -v.RankCoef,
                });
            }
        }

        /// <summary>
        /// A context value reduced to Fixed + RankCoef * rank(casterLevel). Simple values are fixed, Rank values
        /// look up the matching ContextRankConfig, Shared values resolve the ability's ContextCalculateSharedValue
        /// ((dice average + bonus) * modifier, IL-verified). Anything else is unknown.
        /// </summary>
        internal struct Linear {
            public bool Known;
            public float Fixed;
            public RankSpec Rank;
            public float RankCoef;

            public static Linear Unknown() { return new Linear(); }

            public static Linear Eval(ContextValue v, BlueprintAbility ability, BlueprintBuff buff) {
                if (v == null) return Unknown();
                switch (v.ValueType) {
                    case ContextValueType.Simple:
                        return new Linear { Known = true, Fixed = v.Value };
                    case ContextValueType.Rank: {
                        var spec = FindRankSpec(v.ValueRank, buff, ability);
                        if (spec == null || !spec.Valid) return Unknown();
                        return new Linear { Known = true, Rank = spec, RankCoef = 1f };
                    }
                    case ContextValueType.Shared: {
                        var calc = FindShared(ability, v.ValueShared);
                        if (calc == null) return Unknown();
                        var count = Eval(calc.Value.DiceCountValue, ability, buff);
                        var bonus = Eval(calc.Value.BonusValue, ability, buff);
                        if (!count.Known || !bonus.Known) return Unknown();
                        if (count.Rank != null && bonus.Rank != null) return Unknown();
                        float faces = DiceAverage(calc.Value.DiceType);
                        var r = count.Rank != null ? count : bonus;
                        float mod = (float)calc.Modifier;
                        return new Linear {
                            Known = true,
                            Fixed = (count.Fixed * faces + bonus.Fixed) * mod,
                            Rank = r.Rank,
                            RankCoef = (count.Rank != null ? count.RankCoef * faces : bonus.RankCoef) * mod,
                        };
                    }
                    default:
                        return Unknown();
                }
            }

            // Average of one die; Zero rolls nothing, One is a flat 1.
            static float DiceAverage(Kingmaker.RuleSystem.DiceType dice) {
                int sides = (int)dice;
                return sides <= 0 ? 0f : (sides + 1) / 2f;
            }

            static ContextCalculateSharedValue FindShared(BlueprintAbility ability, AbilitySharedValue slot) {
                if (ability?.ComponentsArray == null) return null;
                foreach (var c in ability.ComponentsArray)
                    if (c is ContextCalculateSharedValue calc && calc.ValueType == slot) return calc;
                return null;
            }

            static RankSpec FindRankSpec(Kingmaker.Enums.AbilityRankType type, BlueprintBuff buff, BlueprintAbility ability) {
                var cfg = FindConfig(buff, type) ?? FindConfig(ability, type);
                return cfg == null ? null : BuildSpec(cfg);
            }

            static ContextRankConfig FindConfig(BlueprintScriptableObject bp, Kingmaker.Enums.AbilityRankType type) {
                if (bp?.ComponentsArray == null) return null;
                foreach (var c in bp.ComponentsArray)
                    if (c is ContextRankConfig cfg && cfg.Type == type) return cfg;
                return null;
            }

            // Only caster-level based formulas can be evaluated for a caster; ClassLevel (hexes) is taken as the
            // caster level, which holds for the single-class sources it appears on.
            static RankSpec BuildSpec(ContextRankConfig cfg) {
                var spec = new RankSpec();
                switch (cfg.m_BaseValueType) {
                    case ContextRankBaseValueType.CasterLevel:
                    case ContextRankBaseValueType.ClassLevel:
                    case ContextRankBaseValueType.MaxCasterLevel:
                        spec.Valid = true;
                        break;
                    default:
                        return spec;
                }
                spec.Progression = cfg.m_Progression;
                spec.Start = cfg.m_StartLevel;
                spec.Step = cfg.m_StepLevel;
                spec.UseMin = cfg.m_UseMin; spec.Min = cfg.m_Min;
                spec.UseMax = cfg.m_UseMax; spec.Max = cfg.m_Max;
                if (cfg.m_CustomProgression != null) {
                    spec.CustomBase = new int[cfg.m_CustomProgression.Length];
                    spec.CustomValue = new int[cfg.m_CustomProgression.Length];
                    for (int i = 0; i < cfg.m_CustomProgression.Length; i++) {
                        spec.CustomBase[i] = cfg.m_CustomProgression[i].BaseValue;
                        spec.CustomValue[i] = cfg.m_CustomProgression[i].ProgressionValue;
                    }
                }
                return spec;
            }
        }
    }
}
