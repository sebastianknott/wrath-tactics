using System;
using System.Collections.Generic;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>One remedy as the selector sees it: plain data, no game objects, so synthetic lists can drive it (WTW-30).</summary>
    internal sealed class CleanseOptionInput {
        /// <summary>Stable remedy key (the latch key and the final tie-break).</summary>
        public string Key;
        public string Name;
        public CleanseProfile Profile;
        /// <summary>Spell grade; a class ability's free use counts as 0.</summary>
        public int Grade;
        /// <summary>True for a spell slot or class resource, false for a consumable (wand charge, potion).</summary>
        public bool IsSlot;
        /// <summary>Answers the caster-fact gates of the profile (Lay on Hands mercies). Null = no fact is held.</summary>
        public Func<string, bool> HasFact;
        /// <summary>Removal chance of an effect against one affliction bit; null = the check inputs are unknown.</summary>
        public Func<CleanseEffect, AfflictionKinds, double> Chance;
    }

    /// <summary>The ally and the moment a cleanse is chosen for.</summary>
    internal sealed class CleanseSituation {
        public string AllyId;
        /// <summary>Harmful kinds the ally carries right now (CleanseAfflictionReader.Read).</summary>
        public AfflictionKinds AllyMask;
        /// <summary>Kinds the rule wants cured.</summary>
        public AfflictionKinds RuleKinds;
        /// <summary>Exact-GUID check of the ally's buffs; null = unknown (no buff-identity restriction).</summary>
        public Func<string, bool> AllyHasBuff;
        public double NowSeconds;
        public bool InCombat;
    }

    /// <summary>The pick: index into the option list (-1 = nothing), what it covers, and why the others were passed over.</summary>
    internal sealed class CleanseVerdict {
        public int Index = -1;
        /// <summary>The single kind bits the pick will try (also what the latch records).</summary>
        public AfflictionKinds Covered;
        public int Tier;
        public int ExpectedSteps;
        /// <summary>The kinds the pick was ranked against: ally mask narrowed by the rule.</summary>
        public AfflictionKinds Wanted;
        /// <summary>"name (reason)" per option that was not chosen for a reason other than losing the ranking.</summary>
        public List<string> Rejected = new List<string>();
    }

    /// <summary>
    /// Picks the cleanse remedy for one ally (WTW-30). Pure: builds CleanseCandidates from each profile against the
    /// ally's affliction mask intersected with the rule's kinds, honours the attempt latch and the exact-buff rule for
    /// counter-removals, and orders them with CleanseRanking (Heal tier last by construction).
    /// </summary>
    internal static class CleanseSelector {
        internal const string NoMatch = "no-match";
        internal const string Latched = "latched";
        internal const string Hopeless = "hopeless";

        internal static CleanseVerdict Select(IReadOnlyList<CleanseOptionInput> options, CleanseSituation s) {
            var result = new CleanseVerdict { Wanted = s.AllyMask & s.RuleKinds };
            if (result.Wanted == AfflictionKinds.None || options == null) return result;

            var candidates = new List<CleanseCandidate>();
            var coverage = new List<AfflictionKinds>();
            var indexes = new List<int>();
            for (int i = 0; i < options.Count; i++) {
                var o = options[i];
                var reachable = Reachable(o, s, result.Wanted);
                if (reachable == AfflictionKinds.None) {
                    result.Rejected.Add(o.Name + " (" + NoMatch + ")");
                    continue;
                }
                var free = CleanseAttemptLatch.Unblocked(s.AllyId, reachable, o.Key, s.NowSeconds);
                if (free == AfflictionKinds.None) {
                    result.Rejected.Add(o.Name + " (" + Latched + ")");
                    continue;
                }
                var candidate = Candidate(o, s, free, out var covered);
                if (candidate.ExpectedSteps == 0) {
                    result.Rejected.Add(o.Name + " (" + Hopeless + ")");
                    continue;
                }
                candidates.Add(candidate);
                coverage.Add(covered);
                indexes.Add(i);
            }

            int best = CleanseRanking.Best(candidates);
            if (best < 0) return result;
            result.Index = indexes[best];
            result.Covered = coverage[best];
            result.Tier = candidates[best].Tier;
            result.ExpectedSteps = candidates[best].ExpectedSteps;
            return result;
        }

        // Wanted bits some effect of the option really handles for this ally and caster.
        static AfflictionKinds Reachable(CleanseOptionInput o, CleanseSituation s, AfflictionKinds wanted) {
            var kinds = AfflictionKinds.None;
            foreach (var bit in CleanseTable.Bits(wanted))
                if (o.Profile.EffectFor(bit, o.HasFact, s.AllyHasBuff) != null) kinds |= bit;
            return kinds;
        }

        static CleanseCandidate Candidate(CleanseOptionInput o, CleanseSituation s, AfflictionKinds free, out AfflictionKinds covered) {
            covered = free;
            var chances = new List<double>();
            bool anyRemoval = false;
            foreach (var bit in CleanseTable.Bits(free)) {
                var effect = o.Profile.EffectFor(bit, o.HasFact, s.AllyHasBuff);
                if (!effect.Suppress) anyRemoval = true;
                chances.Add(ChanceOf(o, effect, bit));
            }
            int tier = o.Profile.HealsHp ? CleanseRanking.TierHeal
                : anyRemoval ? CleanseRanking.TierNormal : CleanseRanking.TierSuppressor;
            return new CleanseCandidate {
                Tier = tier, ExpectedSteps = CleanseRanking.ExpectedSteps(chances),
                Grade = o.Grade, IsSlot = o.IsSlot, Key = o.Key,
            };
        }

        static double ChanceOf(CleanseOptionInput o, CleanseEffect effect, AfflictionKinds bit) {
            if (o.Chance != null) return o.Chance(effect, bit);
            // No inputs given: a sure effect is sure, a check with unread inputs is a coin flip.
            return CleanseChance.Probability(effect.Check, null, null, null, null, effect.MaxCasterLevel);
        }
    }

    /// <summary>Static, blueprint-level targeting rules of cleanse options (WTW-30 PM decision b).</summary>
    internal static class CleanseTargeting {
        /// <summary>
        /// Can the ability be aimed at a friendly unit? Mirrors AbilityData.CanTarget (IL): the caster itself needs
        /// CanTargetSelf, any other unit needs CanTargetFriends. A dual-purpose damage-plus-cure ability only counts as
        /// a remedy when this holds (Golden Dragon breath hits enemies only; Cleansing Flames and Water Torrent do not
        /// stop at enemies). Point-castable alone does not make an ally the target of the cure.
        /// </summary>
        internal static bool AllyTargetableByFlags(BlueprintAbility bp, bool allyIsOwner) {
            if (bp == null) return false;
            return allyIsOwner ? bp.CanTargetSelf : bp.CanTargetFriends;
        }
    }
}
