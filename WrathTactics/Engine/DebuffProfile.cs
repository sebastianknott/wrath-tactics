using System.Collections.Generic;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    internal enum DebuffAreaKind { Single, AroundEnemy, ChainEnemy }

    internal enum DebuffEntryKind { Stat, Condition }

    /// <summary>Whether the entry applies unconditionally, only when the target fails its save, or only when it succeeds.</summary>
    internal enum SaveGate { Always, OnFail, OnSuccess }

    /// <summary>
    /// How much of the target's action economy an Incapacitated entry takes. Declared in ascending order
    /// of preference: a higher value wins a tie on expected lock duration.
    /// </summary>
    internal enum LockTier { None, NoFullRound, UntilStand, MoveOnly, Random, All }

    /// <summary>Target-dependent term of an entry's magnitude (Dex bonus lost, half the target's speed).</summary>
    internal enum TargetScaling {
        None,
        /// <summary>Loses the (positive) Dexterity bonus: adds max(0, dexMod).</summary>
        DexBonusLost,
        /// <summary>Dexterity bonus is set to -5 (Helpless family): adds dexMod + 5.</summary>
        DexToMinusFive,
        /// <summary>Half the target's speed in feet.</summary>
        HalfSpeed,
        /// <summary>The armor bonus to AC is removed (Sunder Armor).</summary>
        ArmorBonusLost
    }

    /// <summary>
    /// A context-rank formula evaluated at the caster level (mirrors ContextRankConfig.ApplyProgression and
    /// ApplyMinMax, IL-verified). Invalid means "not a caster-level formula": the value is unknown.
    /// </summary>
    internal sealed class RankSpec {
        public bool Valid;
        public ContextRankProgression Progression;
        public int Start, Step;
        public bool UseMin, UseMax;
        public int Min, Max;
        public int[] CustomBase = new int[0];
        public int[] CustomValue = new int[0];
    }

    /// <summary>
    /// One thing a debuff does to its target. Magnitude = Fixed + RankMult * rank(casterLevel) + target term.
    /// Group-level facts (save gate, ends-on-damage) are per entry because one option can carry several effects.
    /// </summary>
    internal sealed class DebuffEntry {
        public DebuffGroup Group;
        public DebuffEntryKind Kind;
        public StatType Stat;
        public UnitCondition Condition;
        public float Fixed;
        public RankSpec Rank;
        public float RankMult;
        public TargetScaling Scaling;
        /// <summary>The magnitude could not be derived from the blueprint; Fixed holds the fallback.</summary>
        public bool UnknownMagnitude;
        public SaveGate Gate;
        public bool EndsOnDamage;
        public bool SaveEachRound;
        public LockTier Tier;
        /// <summary>Duration in rounds when known statically, else &lt; 0 (resolved through Duration).</summary>
        public float DurationRounds = -1f;
        public ContextDurationValue Duration;
        public bool Permanent;
        /// <summary>Exact-GUID of the buff that carries this entry (empty for maneuver conditions).</summary>
        public string BuffGuid = "";
    }

    /// <summary>
    /// Cached, caster-independent reading of what a BlueprintAbility does to an enemy (WTW-30).
    /// Plain data: the blueprint reader fills it, the ranking math consumes it.
    /// </summary>
    internal sealed class DebuffProfile {
        public static readonly DebuffProfile NotADebuff = new DebuffProfile();

        public bool IsDebuff;
        public bool AllySafe;
        public string UnsafeReason = "";
        public DebuffAreaKind Area;
        public float RadiusMeters;
        public SavingThrowType Save;
        public bool ChecksSr;
        public readonly List<DebuffEntry> Entries = new List<DebuffEntry>();
        public readonly HashSet<string> AppliedBuffGuids = new HashSet<string>();

        public bool HasGroup(DebuffGroup g) {
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Group == g) return true;
            return false;
        }
    }
}
