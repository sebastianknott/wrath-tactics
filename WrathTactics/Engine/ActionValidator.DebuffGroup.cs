using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Enums;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.Utility;
using UnityEngine;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>The option the CastDebuffGroup search picked, with the numbers behind the pick.</summary>
    internal struct DebuffChoice {
        public DebuffOption Option;
        public float Ev;
        public float Magnitude;
        public float PFail;
        public float PSr;
        public int Hits;
        /// <summary>Measured DC minus save for the pair; NaN when no save is computable.</summary>
        public float Dcms;
        /// <summary>Measured spell-resist margin for the pair; NaN when not computable.</summary>
        public float Srm;
    }

    public static partial class ActionValidator {
        // One scored candidate on the way to the pick.
        sealed class Scored {
            public DebuffOption Option;
            public DebuffChoice Choice;
            public DebuffMath.DebuffScore Score;
        }

        /// <summary>
        /// CastDebuffGroup: the strongest ally-safe option of ActionDef.DebuffGroup against this target (WTW-30).
        /// Enumerates everything the owner can cast now, drops options that cannot work on the target (engine
        /// CanTarget, buff already on it, immune), scores the rest by expected effect and orders them by
        /// DebuffMath.Compare. Options are visited by descending upper bound so the loop stops as soon as no
        /// remaining option can beat the best one (pruning), which spares the expensive DC and SR measurements.
        /// </summary>
        internal static DebuffChoice? FindBestDebuff(UnitEntityData owner, UnitEntityData target, ActionDef action) {
            var group = action.DebuffGroup;
            if (group == DebuffGroup.None || target == null) return null;

            var options = DebuffOptionEnumerator.Enumerate(owner, group, action.Sources);
            if (options.Count == 0) {
                Log.Engine.Trace($"DebuffGroup {group}: no option for {target.CharacterName}");
                return null;
            }

            var rejected = new List<string>();
            var stats = ReadTargetStats(target);
            var enemyPositions = EnemyPositions(owner);
            var candidates = new List<Scored>();
            foreach (var option in options) {
                string reason = RejectReason(option, group, target);
                if (reason != null) {
                    rejected.Add($"{option.Name} ({reason})");
                    Log.Engine.Trace($"DebuffGroup {group}: '{option.Name}' [{option.Kind}] rejected ({reason})");
                    continue;
                }
                int cl = CasterLevelOf(owner, option);
                var scored = new Scored { Option = option };
                int hits = HitsOf(option, target, enemyPositions);
                scored.Choice = new DebuffChoice { Option = option, Hits = hits, PFail = 1f, PSr = 1f };
                scored.Score = new DebuffMath.DebuffScore {
                    // The bound must include the hit count: an area option with many hits outranks its single value.
                    Ev = UpperBound(owner, option, group, cl, stats) * hits,
                    EndsOnDamage = EndsOnDamageIn(option, group),
                    Tier = TierIn(option, group),
                    SpellLevel = option.SpellLevel,
                    Kind = option.Kind,
                };
                candidates.Add(scored);
            }

            // Preferred class first (effects that survive damage), then by upper bound descending.
            bool damageOriented = group != DebuffGroup.Incapacitated;
            candidates.Sort((a, b) => {
                int ca = damageOriented && a.Score.EndsOnDamage ? 1 : 0;
                int cb = damageOriented && b.Score.EndsOnDamage ? 1 : 0;
                if (ca != cb) return ca.CompareTo(cb);
                return b.Score.Ev.CompareTo(a.Score.Ev);
            });

            Scored best = null;
            int pruned = 0;
            foreach (var c in candidates) {
                if (best != null) {
                    int cClass = damageOriented && c.Score.EndsOnDamage ? 1 : 0;
                    int bClass = damageOriented && best.Score.EndsOnDamage ? 1 : 0;
                    // Sorted by (class, bound): once one candidate cannot beat the best, none after it can.
                    if (cClass > bClass || (cClass == bClass && c.Score.Ev < best.Score.Ev)) {
                        pruned = candidates.Count - candidates.IndexOf(c);
                        break;
                    }
                }
                if (!Measure(owner, target, c, group, stats, rejected)) continue;
                if (best == null || DebuffMath.Compare(c.Score, best.Score, group) < 0) best = c;
            }

            if (best == null) {
                Log.Engine.Trace($"DebuffGroup {group}: no option for {target.CharacterName}");
                return null;
            }

            var ch = best.Choice;
            Log.Engine.Debug($"DebuffGroup {group} {owner.CharacterName} -> {target.CharacterName}: picked '{best.Option.Name}' [{best.Option.Kind}] "
                + $"EV={ch.Ev:F2} (effect {ch.Magnitude:F1}, pSave {ch.PFail:F2}, pSR {ch.PSr:F2}, hits {ch.Hits} within {ch.Option.Profile.RadiusMeters:F1} m)"
                + (rejected.Count > 0 ? "; rejected: " + string.Join(", ", rejected) : "")
                + (pruned > 0 ? $"; {pruned} pruned" : ""));
            return ch;
        }

        /// <summary>
        /// Every usable option of the group against this target with its measured numbers, best first (same order
        /// as FindBestDebuff's pick). Unlike FindBestDebuff nothing is pruned: a GroupOption condition row may
        /// accept an option the plain ranking would have skipped. Options rejected by the cheap checks or found
        /// immune are absent. Used by the condition bundle (WTW-30 phase b).
        /// </summary>
        internal static List<DebuffChoice> RankDebuffOptions(UnitEntityData owner, UnitEntityData target,
                                                             DebuffGroup group, SpellSourceMask mask) {
            var result = new List<DebuffChoice>();
            if (group == DebuffGroup.None || target == null) return result;

            var stats = ReadTargetStats(target);
            var rejected = new List<string>();
            var enemyPositions = EnemyPositions(owner);
            var scored = new List<Scored>();
            foreach (var option in DebuffOptionEnumerator.Enumerate(owner, group, mask)) {
                string reason = RejectReason(option, group, target);
                if (reason != null) {
                    Log.Engine.Trace($"DebuffGroup {group}: '{option.Name}' [{option.Kind}] rejected ({reason})");
                    continue;
                }
                var c = new Scored { Option = option };
                c.Choice = new DebuffChoice { Option = option, Hits = HitsOf(option, target, enemyPositions), PFail = 1f, PSr = 1f };
                c.Score = new DebuffMath.DebuffScore {
                    EndsOnDamage = EndsOnDamageIn(option, group),
                    Tier = TierIn(option, group),
                    SpellLevel = option.SpellLevel,
                    Kind = option.Kind,
                };
                if (Measure(owner, target, c, group, stats, rejected)) scored.Add(c);
            }
            scored.Sort((a, b) => DebuffMath.Compare(a.Score, b.Score, group));
            foreach (var c in scored) result.Add(c.Choice);
            return result;
        }

        static List<Vector3> EnemyPositions(UnitEntityData owner) {
            return TargetResolver.VisibleEnemies(owner).Select(e => e.Position).ToList();
        }

        // Enemies the option hits when cast at the target; area options count the enemies around the target.
        static int HitsOf(DebuffOption option, UnitEntityData target, List<Vector3> enemyPositions) {
            return DebuffMath.HitsFor(option.Profile.Area, option.Profile.RadiusMeters, enemyPositions, target.Position);
        }

        // Cheap per-option exclusions that need no DC/SR measurement.
        static string RejectReason(DebuffOption option, DebuffGroup group, UnitEntityData target) {
            var ability = option.Ability;
            if (!ability.CanTargetPoint && !ability.CanTarget(new TargetWrapper(target))) return "cannot-target";
            if (AlreadyAffected(option, target)) return "already-affected";
            if (ImmuneToAllConditions(option, group, target)) return "immune";
            return null;
        }

        // Exact blueprint GUID of a buff this option applies, already on the target (never a substring match).
        static bool AlreadyAffected(DebuffOption option, UnitEntityData target) {
            var guids = option.Profile.AppliedBuffGuids;
            if (guids.Count == 0) return false;
            foreach (var buff in target.Buffs.RawFacts)
                if (guids.Contains(buff.Blueprint.AssetGuid.ToString())) return true;
            return false;
        }

        // Condition immunity only blocks an option when EVERY group entry is a condition the target is immune to;
        // a stat penalty in the same option still lands.
        static bool ImmuneToAllConditions(DebuffOption option, DebuffGroup group, UnitEntityData target) {
            bool any = false;
            foreach (var e in option.Profile.Entries) {
                if (e.Group != group) continue;
                if (e.Kind != DebuffEntryKind.Condition) return false;
                any = true;
                if (!target.State.HasConditionImmunity(e.Condition)) return false;
            }
            return any;
        }

        static DebuffMath.TargetStats ReadTargetStats(UnitEntityData target) {
            var s = target.Stats;
            return new DebuffMath.TargetStats {
                DexMod = s.Dexterity.Bonus,
                SpeedFeet = s.Speed.ModifiedValue,
                ArmorBonus = s.AC.GetDescriptorBonus(ModifierDescriptor.Armor),
            };
        }

        static int CasterLevelOf(UnitEntityData owner, DebuffOption option) {
            return (int)DebuffMetricCache.GetOrCompute($"cl|{owner.UniqueId}|{option.Key}",
                () => option.Ability.CalculateParams().CasterLevel);
        }

        static bool EndsOnDamageIn(DebuffOption option, DebuffGroup group) {
            foreach (var e in option.Profile.Entries)
                if (e.Group == group && e.EndsOnDamage) return true;
            return false;
        }

        static LockTier TierIn(DebuffOption option, DebuffGroup group) {
            var tier = LockTier.None;
            foreach (var e in option.Profile.Entries)
                if (e.Group == group && e.Tier > tier) tier = e.Tier;
            return tier;
        }

        // Rounds an Incapacitated entry lasts: static value, else the blueprint duration evaluated for this
        // caster, else the named fallback.
        static float DurationRoundsOf(UnitEntityData owner, DebuffOption option, DebuffEntry e) {
            if (e.DurationRounds >= 0f) return e.DurationRounds;
            if (e.Duration == null) return DebuffMath.UnknownDurationRounds;
            return DebuffMetricCache.GetOrCompute($"dur|{owner.UniqueId}|{option.Key}|{e.BuffGuid}", () => {
                try {
                    var context = option.Ability.CreateExecutionContext(new TargetWrapper(owner));
                    return Math.Max(1, e.Duration.Calculate(context).Value);
                } catch (Exception ex) {
                    // Per-tick guard: a duration that cannot be evaluated must not abort the search.
                    Log.Engine.Debug($"DebuffGroup: duration of '{option.Name}' unresolved ({ex.GetType().Name}), using {DebuffMath.UnknownDurationRounds} round(s)");
                    return DebuffMath.UnknownDurationRounds;
                }
            });
        }

        // Value of the option if the save always fails and SR never applies: the bound the pruning relies on.
        static float UpperBound(UnitEntityData owner, DebuffOption option, DebuffGroup group, int cl, DebuffMath.TargetStats stats) {
            return Aggregate(owner, option, group, cl, stats, 1f, out _, ignoreGate: true);
        }

        // Entries of one group combine as a sum (stat penalties stack in this model, WTW-30 D9); lock durations
        // do not add up, the longest counts.
        static float Aggregate(UnitEntityData owner, DebuffOption option, DebuffGroup group, int cl,
                               DebuffMath.TargetStats stats, float pFail, out float rawMagnitude, bool ignoreGate = false) {
            float sum = 0f, max = 0f, raw = 0f;
            foreach (var e in option.Profile.Entries) {
                if (e.Group != group) continue;
                float gate = ignoreGate ? 1f : DebuffMath.GateChance(e.Gate, pFail);
                if (group == DebuffGroup.Incapacitated) {
                    float rounds = DebuffMath.ExpectedLockRounds(DurationRoundsOf(owner, option, e), pFail, e.SaveEachRound);
                    max = Math.Max(max, rounds * gate);
                    raw = Math.Max(raw, rounds);
                } else {
                    float m = DebuffMath.Magnitude(e, cl, stats);
                    sum += m * gate;
                    raw += m;
                }
            }
            rawMagnitude = raw;
            return group == DebuffGroup.Incapacitated ? max : sum;
        }

        // Measures save and SR for the pair and finishes the score. False when the option turns out immune.
        static bool Measure(UnitEntityData owner, UnitEntityData target, Scored c, DebuffGroup group,
                            DebuffMath.TargetStats stats, List<string> rejected) {
            var option = c.Option;
            var ability = option.Ability;
            float srm = DebuffMetricCache.GetOrCompute($"sr|{option.Key}|{target.UniqueId}|{owner.UniqueId}",
                () => ConditionEvaluator.MeasureSpellResistMargin(owner, target, ability));
            if (!float.IsNaN(srm) && srm <= ConditionEvaluator.SpellResistMarginFails) {
                rejected.Add($"{option.Name} (immune)");
                Log.Engine.Trace($"DebuffGroup {group}: '{option.Name}' [{option.Kind}] rejected (immune)");
                return false;
            }
            float pSr = float.IsNaN(srm) ? 1f : DebuffMath.PenetrationChance((int)srm);

            float dcms = DebuffMetricCache.GetOrCompute($"dc|{option.Key}|{target.UniqueId}|{owner.UniqueId}",
                () => ConditionEvaluator.MeasureDCMinusSave(owner, target, ability));
            // No computable save (NaN) means nothing to resist: the effect always lands.
            float pFail = float.IsNaN(dcms) ? 1f : DebuffMath.FailChance((int)dcms);

            int cl = CasterLevelOf(owner, option);
            int hits = c.Choice.Hits;
            float gated = Aggregate(owner, option, group, cl, stats, pFail, out float raw);
            c.Choice.Ev = DebuffMath.ExpectedValue(gated, 1f, pSr, hits);
            c.Choice.Magnitude = raw;
            c.Choice.PFail = pFail;
            c.Choice.PSr = pSr;
            c.Choice.Dcms = dcms;
            c.Choice.Srm = srm;
            c.Score.Ev = c.Choice.Ev;
            return true;
        }
    }
}
