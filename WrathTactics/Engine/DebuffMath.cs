using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.UnitLogic.Mechanics.Components;
using UnityEngine;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// Pure arithmetic of the debuff ranking (WTW-30): chances, expected lock rounds, rank formulas and the
    /// option comparison. No engine state, fully unit-testable.
    /// </summary>
    internal static class DebuffMath {
        // Named fallbacks (WTW-30 D8): used when the blueprint holds no readable value.
        internal const float UnknownMagnitude = 1f;
        internal const float UnknownDurationRounds = 1f;
        internal const float MaxLockRounds = 10f;
        internal const float ProneLockRounds = 1f;

        const float ScoreEpsilon = 1e-4f;

        /// <summary>P(target fails its save) for DC − save = m. A d20 save passes on roll + save >= DC.</summary>
        internal static float FailChance(int dcMinusSave) {
            return Mathf.Clamp01((dcMinusSave - 1) / 20f);
        }

        /// <summary>P(spell penetrates SR) for the SpellResistMargin sentinels (+99 no check, −99 immune) or margin M.</summary>
        internal static float PenetrationChance(int spellResistMargin) {
            if (spellResistMargin >= ConditionEvaluator.SpellResistMarginPasses) return 1f;
            if (spellResistMargin <= ConditionEvaluator.SpellResistMarginFails) return 0f;
            return Mathf.Clamp01((21 + spellResistMargin) / 20f);
        }

        /// <summary>
        /// Expected rounds the target stays locked. With a save every round the effect survives round k
        /// with probability pFail^k, so the sum of the geometric series up to the duration applies.
        /// </summary>
        internal static float ExpectedLockRounds(float durationRounds, float pFail, bool saveEachRound) {
            if (!saveEachRound) return durationRounds;
            if (pFail >= 1f) return durationRounds;
            return (1f - Mathf.Pow(pFail, durationRounds)) / (1f - pFail);
        }

        internal static float ExpectedValue(float magnitude, float pSave, float pSr, int hits) {
            return magnitude * pSave * pSr * hits;
        }

        /// <summary>Chance the entry's gate lets the effect through, given the chance the target fails.</summary>
        internal static float GateChance(SaveGate gate, float pFail) {
            switch (gate) {
                case SaveGate.OnFail: return pFail;
                case SaveGate.OnSuccess: return 1f - pFail;
                default: return 1f;
            }
        }

        /// <summary>Mirrors ContextRankConfig.ApplyProgression + ApplyMinMax (IL-verified) for a base value.</summary>
        internal static int ApplyRank(RankSpec spec, int baseValue) {
            int v = baseValue;
            int step = spec.Step, start = spec.Start;
            switch (spec.Progression) {
                case ContextRankProgression.AsIs: break;
                case ContextRankProgression.Div2: v = v / 2; break;
                case ContextRankProgression.OnePlusDiv2: v = 1 + (v - 1) / 2; break;
                case ContextRankProgression.StartPlusDivStep: v = 1 + Math.Max((v - start) / Nonzero(step), 0); break;
                case ContextRankProgression.DivStep: v = v / Nonzero(step); break;
                case ContextRankProgression.OnePlusDivStep: v = 1 + Math.Max(v / Nonzero(step), 0); break;
                case ContextRankProgression.MultiplyByModifier: v = v * step; break;
                case ContextRankProgression.HalfMore: v = v + v / 2; break;
                case ContextRankProgression.BonusValue: v = v + step; break;
                case ContextRankProgression.DelayedStartPlusDivStep:
                    v = v < start ? 0 : 1 + Math.Max((v - start) / Nonzero(step), 0); break;
                case ContextRankProgression.StartPlusDoubleDivStep: v = 1 + 2 * Math.Max((v - start) / Nonzero(step), 0); break;
                case ContextRankProgression.Div2PlusStep: v = v / 2 + step; break;
                case ContextRankProgression.Custom: v = Progression(spec.CustomBase, spec.CustomValue, v); break;
                case ContextRankProgression.DoublePlusBonusValue: v = v * 2 + step; break;
                case ContextRankProgression.SquaredValue: v = v * v; break;
            }
            if (spec.UseMin) v = Math.Max(v, spec.Min);
            if (spec.UseMax) v = Math.Min(v, spec.Max);
            return v;
        }

        static int Nonzero(int step) {
            return step == 0 ? 1 : step;
        }

        /// <summary>
        /// ContextRankConfig custom progression: the first item whose BaseValue is >= value wins, past the end
        /// the last item (an empty table yields 0). Peer Into Mind: CL 5 → 1, CL 6 → 2, CL 18 → 6.
        /// </summary>
        internal static int Progression(int[] baseValues, int[] progressionValues, int value) {
            if (baseValues.Length == 0) return 0;
            for (int i = 0; i < baseValues.Length; i++)
                if (value <= baseValues[i]) return progressionValues[i];
            return progressionValues[progressionValues.Length - 1];
        }

        /// <summary>Target facts the magnitude of an entry may depend on.</summary>
        internal struct TargetStats {
            public int DexMod;
            public float SpeedFeet;
            public int ArmorBonus;
        }

        /// <summary>Magnitude of one entry for a caster level against a target.</summary>
        internal static float Magnitude(DebuffEntry e, int casterLevel, TargetStats target) {
            float m = e.Fixed;
            if (e.Rank != null && e.Rank.Valid) m += e.RankMult * ApplyRank(e.Rank, casterLevel);
            switch (e.Scaling) {
                case TargetScaling.DexBonusLost: m += Math.Max(0, target.DexMod); break;
                case TargetScaling.DexToMinusFive: m += Math.Max(0, target.DexMod + 5); break;
                case TargetScaling.HalfSpeed: m += target.SpeedFeet / 2f; break;
                case TargetScaling.ArmorBonusLost: m += Math.Max(0, target.ArmorBonus); break;
            }
            return m;
        }

        /// <summary>Number of positions within radius of center (planar distance, XZ).</summary>
        internal static int CountWithin(IEnumerable<Vector3> positions, Vector3 center, float radius) {
            int n = 0;
            foreach (var p in positions) {
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz <= radius * radius) n++;
            }
            return n;
        }

        /// <summary>
        /// Enemies an option hits when cast at the pick: area options around an enemy count the enemies within the
        /// radius of the pick (the pick itself included, so at least 1); single-target and chain options count 1
        /// (WTW-30 D10: chains are not modelled).
        /// </summary>
        internal static int HitsFor(DebuffAreaKind area, float radiusMeters, IEnumerable<Vector3> enemyPositions, Vector3 center) {
            if (area != DebuffAreaKind.AroundEnemy || radiusMeters <= 0f) return 1;
            return Math.Max(1, CountWithin(enemyPositions, center, radiusMeters));
        }

        /// <summary>True when the profile flags allow casting at an enemy without risk to allies.</summary>
        internal static bool IsAllySafe(bool canTargetEnemies, bool spawnsAreaEffect, bool canTargetPoint,
                                        DebuffAreaKind area, bool areaFilteredToEnemies, bool coneOrLine) {
            if (!canTargetEnemies || spawnsAreaEffect || coneOrLine) return false;
            if (area == DebuffAreaKind.Single) return !canTargetPoint;
            return areaFilteredToEnemies;
        }

        /// <summary>What the ranking needs to know about one scored option.</summary>
        internal struct DebuffScore {
            public float Ev;
            public bool EndsOnDamage;
            public LockTier Tier;
            public int SpellLevel;
            public DebuffSourceKind Kind;
        }

        /// <summary>
        /// An option whose expected value is not above epsilon cannot change the target (a save that always
        /// holds, SR that always stops it). It must never be picked: EndsOnDamage sorts ahead of EV, so a zero
        /// option would otherwise beat a real one.
        /// </summary>
        internal static bool CanLand(float ev) {
            return ev > ScoreEpsilon;
        }

        // Sort class of the search: effects that end on damage are tried last in damage-oriented groups.
        static int SortClass(DebuffScore s, DebuffGroup group) {
            return group != DebuffGroup.Incapacitated && s.EndsOnDamage ? 1 : 0;
        }

        /// <summary>Indices of the bounds in the order the search measures them: class ascending, bound descending, stable.</summary>
        internal static List<int> OrderByBound(IReadOnlyList<DebuffScore> bounds, DebuffGroup group) {
            var order = new List<int>(bounds.Count);
            for (int i = 0; i < bounds.Count; i++) order.Add(i);
            return order.OrderBy(i => SortClass(bounds[i], group)).ThenByDescending(i => bounds[i].Ev).ToList();
        }

        /// <summary>
        /// True when a candidate with this upper bound cannot beat the best measured score. Within epsilon the
        /// bound still counts as a tie that the spell-level tiebreak may decide.
        /// </summary>
        internal static bool CannotBeat(DebuffScore bound, DebuffScore best, DebuffGroup group) {
            int cb = SortClass(bound, group), cbest = SortClass(best, group);
            if (cb != cbest) return cb > cbest;
            return bound.Ev + ScoreEpsilon < best.Ev;
        }

        /// <summary>
        /// The search over upper bounds: visits candidates by (class, bound descending), measures one only
        /// while it can still beat the best measured so far, and stops at the first that cannot (pruning, which
        /// spares the expensive DC and SR measurements). measure returns the final score, or null when the
        /// candidate is rejected. Returns the index of the best candidate, -1 when none survived.
        /// </summary>
        internal static int Pick(IReadOnlyList<DebuffScore> bounds, Func<int, DebuffScore?> measure,
                                 DebuffGroup group, out int pruned) {
            pruned = 0;
            var order = OrderByBound(bounds, group);
            int best = -1;
            DebuffScore bestScore = default(DebuffScore);
            for (int k = 0; k < order.Count; k++) {
                int i = order[k];
                if (best >= 0 && CannotBeat(bounds[i], bestScore, group)) {
                    pruned = order.Count - k;
                    break;
                }
                var measured = measure(i);
                if (!measured.HasValue) continue;
                if (best < 0 || Compare(measured.Value, bestScore, group) < 0) {
                    best = i;
                    bestScore = measured.Value;
                }
            }
            return best;
        }

        /// <summary>
        /// Orders two scored options for a group: negative when a is the better pick. Damage-oriented groups put
        /// effects that end on damage last; Incapacitated breaks equal lock time by LockTier. Ties go to the
        /// lower spell level (keeps strong slots free), then the lower source kind (slot before consumable).
        /// </summary>
        internal static int Compare(DebuffScore a, DebuffScore b, DebuffGroup group) {
            if (group != DebuffGroup.Incapacitated && a.EndsOnDamage != b.EndsOnDamage)
                return a.EndsOnDamage ? 1 : -1;
            if (Math.Abs(a.Ev - b.Ev) > ScoreEpsilon) return a.Ev > b.Ev ? -1 : 1;
            if (group == DebuffGroup.Incapacitated && a.Tier != b.Tier)
                return a.Tier > b.Tier ? -1 : 1;
            if (a.SpellLevel != b.SpellLevel) return a.SpellLevel < b.SpellLevel ? -1 : 1;
            if (a.Kind != b.Kind) return a.Kind < b.Kind ? -1 : 1;
            return 0;
        }
    }
}
