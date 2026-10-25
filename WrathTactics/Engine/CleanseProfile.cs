using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Stats;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>One thing an ability does about afflictions (WTW-30 cleanse): remove or suppress some kinds.</summary>
    internal sealed class CleanseEffect {
        public AfflictionKinds Kinds;
        /// <summary>True when the afflictions are only held off (immunity, suppression), not removed.</summary>
        public bool Suppress;
        public CleanseCheckType Check;
        /// <summary>Skill rolled for the SkillDC / SkillCasterLevel checks.</summary>
        public StatType Skill;
        /// <summary>Caster-level cap of the check, 0 = none.</summary>
        public int MaxCasterLevel;
        /// <summary>Caster must carry all of these facts (exact GUIDs) for the effect to apply, e.g. a Lay on Hands mercy. Empty = always.</summary>
        public string[] GateFacts = new string[0];
        /// <summary>Caster must carry none of these facts (a negated caster-fact condition). Empty = no restriction.</summary>
        public string[] ForbiddenFacts = new string[0];
    }

    /// <summary>
    /// Cached, caster-independent reading of what a BlueprintAbility does about afflictions (WTW-30 cleanse).
    /// Plain data: CleanseClassifier fills it, the selector and CleanseRanking consume it.
    /// </summary>
    internal sealed class CleanseProfile {
        public static readonly CleanseProfile NotACleanse = new CleanseProfile();

        public readonly List<CleanseEffect> Effects = new List<CleanseEffect>();
        /// <summary>The ability heals hit points as well (Heal, Lay on Hands): ranked as the last resort.</summary>
        public bool HealsHp;

        public bool IsCleanse { get { return Effects.Count > 0; } }

        public void Add(CleanseEffect effect) {
            if (effect != null && effect.Kinds != AfflictionKinds.None) Effects.Add(effect);
        }

        /// <summary>
        /// Kinds covered for a caster. <paramref name="hasFact"/> answers the caster-fact gates of mercy-style effects
        /// (null = no fact is held). Removal and suppression together.
        /// </summary>
        public AfflictionKinds Covers(Func<string, bool> hasFact) {
            var k = AfflictionKinds.None;
            foreach (var e in Effects) if (Active(e, hasFact)) k |= e.Kinds;
            return k;
        }

        public AfflictionKinds Removes(Func<string, bool> hasFact) {
            var k = AfflictionKinds.None;
            foreach (var e in Effects) if (!e.Suppress && Active(e, hasFact)) k |= e.Kinds;
            return k;
        }

        /// <summary>
        /// The effect that handles a kind: a removal over a suppression, then the surest check
        /// (None first), then declaration order. Null when nothing covers it.
        /// </summary>
        public CleanseEffect EffectFor(AfflictionKinds kind, Func<string, bool> hasFact) {
            CleanseEffect best = null;
            foreach (var e in Effects) {
                if ((e.Kinds & kind) == 0 || !Active(e, hasFact)) continue;
                if (best == null || Better(e, best)) best = e;
            }
            return best;
        }

        static bool Better(CleanseEffect e, CleanseEffect best) {
            if (e.Suppress != best.Suppress) return !e.Suppress;
            return e.Check == CleanseCheckType.None && best.Check != CleanseCheckType.None;
        }

        static bool Active(CleanseEffect e, Func<string, bool> hasFact) {
            if (hasFact == null) return e.GateFacts.Length == 0;
            foreach (var f in e.GateFacts) if (!hasFact(f)) return false;
            foreach (var f in e.ForbiddenFacts) if (hasFact(f)) return false;
            return true;
        }

        /// <summary>CleanseRanking tier: 2 when it also heals hit points, 1 when it only suppresses, else 0.</summary>
        public int Tier {
            get {
                if (HealsHp) return CleanseRanking.TierHeal;
                foreach (var e in Effects) if (!e.Suppress) return CleanseRanking.TierNormal;
                return CleanseRanking.TierSuppressor;
            }
        }
    }
}
