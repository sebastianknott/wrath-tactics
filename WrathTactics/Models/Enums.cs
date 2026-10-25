namespace WrathTactics.Models {
    public enum ConditionSubject {
        Self,
        Ally,
        AllyCount,
        Enemy,
        EnemyCount,
        Combat,
        EnemyBiggestThreat,  // the single enemy with highest threat
        EnemyLowestThreat,   // the single enemy with lowest threat
        EnemyHighestHp,      // the single enemy with highest HP%
        EnemyLowestHp,       // the single enemy with lowest HP%
        EnemyLowestAC,       // the single enemy with lowest AC
        EnemyHighestAC,      // the single enemy with highest AC
        EnemyLowestFort,     // the single enemy with lowest Fortitude save
        EnemyHighestFort,    // the single enemy with highest Fortitude save
        EnemyLowestReflex,   // the single enemy with lowest Reflex save
        EnemyHighestReflex,  // the single enemy with highest Reflex save
        EnemyLowestWill,     // the single enemy with lowest Will save
        EnemyHighestWill,    // the single enemy with highest Will save
        EnemyHighestHD,      // the single enemy with highest HD
        EnemyLowestHD,       // the single enemy with lowest HD
        AllyByName,          // a specific ally pinned by UniqueId stored in Condition.Value2
        EnemyNearest,        // the single enemy closest to the rule owner
        GroupOption          // 22: a debuff option of the effect group named in Value2 (WTW-30); rows bind to ONE option
    }

    public enum ConditionProperty {
        HpPercent,
        AC,
        HasBuff,
        HasCondition,
        SpellSlotsAtLevel,
        SpellSlotsAboveLevel,
        Resource,
        CreatureType,
        CombatRounds,
        IsDead,
        SaveFortitude,
        SaveReflex,
        SaveWill,
        Alignment,
        IsInCombat,
        HitDice,
        SpellDCMinusSave,
        HasClass,
        WithinRange,
        ABMinusAC,           // partyBestAB - enemy.AC — Enemy-scope only
        IsTargetingSelf,     // Enemy-scope: this enemy targets the rule owner
        IsTargetingAlly,     // Enemy-scope: this enemy targets a non-owner ally
        IsTargetedByAlly,    // Enemy-scope: a non-owner ally targets this enemy
        IsTargetedByEnemy,        // Ally-scope: an enemy targets this ally
        IsSummon,                 // Yes/No — UnitPartSummonedMonster present (excludes pets/companions)
        EnemyHDMinusPartyLevel,   // enemyEffectiveHD − partyMaxEffectiveLevel — Enemy-scope only
        IsPet,                    // Yes/No — UnitPartPet present (Animal Companion / Aivu / Lich-Skelett / Clone / Night Hag / Eidolon)
        IsFlanked,                // Yes/No — engine UnitCombatState.IsFlanked (positional flanking)
        AdjacentEnemyCount,       // numeric — enemies within Melee range (≤2 m / 5 ft); see RangeBrackets.MaxMeters
        HasDescriptorEffect,      // Value = SpellDescriptor name (Poison/Disease/Bleed) — any active buff carrying that descriptor; =/!=
        ImmuneToEnergy,           // Value = DamageEnergyType name (Fire/Cold/Electricity/Acid/Sonic) — UnitPartDamageReduction.IsImmune; =/!=
        AbilityDamage,            // Yes/No — any of the six ability scores carries Damage (Str/Dex/Con/Int/Wis/Cha); for Restoration / Lesser Restoration rules. Drain not counted.
        NegativeLevels,           // Yes/No — UnitPartNegativeLevels.Count > 0 (temporary + permanent energy drain); for Restoration rules.
        HpFlat,                   // numeric — current hit points, flat (HPLeft = HitPoints.ModifiedValue − Damage; temp HP excluded, same expression as the engine's AbilityTargetHPCondition Power-Word gate)
        WieldsRangedWeapon,       // Yes/No — current weapon is ranged (GetFirstWeapon → Blueprint.IsRanged, i.e. AttackType Ranged/RangedTouch); for "focus the archers" rules
        SpellResistMargin,        // numeric — caster level + penetration bonuses − target spell resistance for the rule's own [Then] spell; Enemy-scope; +99 = no SR check / no SR, −99 = immune
        OptionEffect,             // numeric — magnitude of the GroupOption's effect (stat penalty points, or expected lock rounds for Incapacitated); GroupOption only
        OptionSource,             // value-list — DebuffSourceKind name of the GroupOption (Spell/ClassAbility/…); =/!=; GroupOption only
        OptionEnemiesInRadius     // numeric — enemies the GroupOption's option hits when cast at the enemy (area radius count, 1 for single/chain); GroupOption only
    }

    public enum ConditionOperator {
        LessThan,
        GreaterThan,
        Equal,
        NotEqual,
        GreaterOrEqual,
        LessOrEqual
    }

    public enum ActionType {
        CastSpell,
        CastAbility,    // class abilities (non-spell, non-item)
        UseItem,
        ToggleActivatable,
        AttackTarget,
        Heal,           // automatically use best available heal
        DoNothing,
        ThrowSplash,    // throw a splash weapon (Alchemist's Fire, Acid Flask, Holy Water)
        SwitchWeaponSet, // swap to a specific HandsEquipmentSet (engine allocates 4 slots, index 0-3)
        MoveToTarget,   // walk towards the target until within ActionDef.MoveWithin (UnitMoveTo, Move slot)
        MoveAwayFromTarget, // walk away from the target until beyond ActionDef.MoveWithin (UnitMoveTo, Move slot; FleeGeometry)
        CastDebuffGroup,    // 11: cast the strongest ally-safe debuff of ActionDef.DebuffGroup (WTW-30)
        CastConditionOption, // 12: cast the option bound by a GroupOption condition row (WTW-30 phase b)
        RemoveCondition     // 13: cure the afflictions in ActionDef.CleanseKinds from an ally (WTW-30 cleanse)
    }

    /// <summary>
    /// Harmful afflictions a cleanse rule can target (WTW-30 cleanse). Flags, persisted as a number: None MUST stay 0,
    /// append new bits at the END only (never reorder).
    /// </summary>
    [System.Flags]
    public enum AfflictionKinds {
        None = 0,
        Paralysis = 1 << 0,
        Confusion = 1 << 1,
        StunDaze = 1 << 2,
        FearShaken = 1 << 3,
        NauseaSickened = 1 << 4,
        Blindness = 1 << 5,
        Poison = 1 << 6,
        Disease = 1 << 7,
        Curse = 1 << 8,
        FatigueExhausted = 1 << 9,
        StatDebuffAbilityDamage = 1 << 10,
        NegativeLevel = 1 << 11,
        Petrified = 1 << 12,
        SlowedEntangled = 1 << 13
    }

    /// <summary>
    /// Effect group of a debuff (WTW-30). Newtonsoft serialises numeric indices: None MUST stay 0, append at the END only.
    /// </summary>
    public enum DebuffGroup {
        None,
        ArmorClass,
        Attack,
        Damage,
        SaveWill,
        SaveReflex,
        SaveFortitude,
        Dexterity,
        Strength,
        Constitution,
        Speed,
        Incapacitated
    }

    /// <summary>Where a debuff option comes from (WTW-30). Append only.</summary>
    public enum DebuffSourceKind {
        Spell,
        ClassAbility,
        ItemAbility,
        Scroll,
        ThrownItem
    }

    public enum HealMode {
        Any,            // Use any available heal (spell > scroll > potion)
        Strongest,      // Use the highest-level heal available
        Weakest         // Use the lowest-level heal (conserve resources)
    }

    /// <summary>
    /// Pin for the Heal action. Auto detects via target's NegativeEnergyAffinity / CreatureType
    /// and picks Cure (living) or Inflict/Harm (undead) accordingly. Positive / Negative force
    /// a specific energy type regardless of target — power-user override.
    ///
    /// Newtonsoft serialises numeric indices. Auto MUST stay at index 0 so missing JSON fields
    /// deserialise as Auto on legacy configs. Append new values at the END only.
    /// </summary>
    public enum HealEnergyType {
        Auto,       // Detect via target's NegativeEnergyAffinity / CreatureType (default)
        Positive,   // Force Cure / Heal / Channel-Positive only
        Negative,   // Force Inflict / Harm / Channel-Negative only
        None        // Sentinel returned by ClassifyHeal for non-heal blueprints (never persisted)
    }

    /// <summary>
    /// Which classes of heal source the engine may draw from. Flag-based so combinations
    /// (e.g. Spell+Potion to skip scrolls when UMD is bad) are expressible. Default is All.
    /// Spell covers spellbook casts, class abilities (Channel, Lay on Hands), and wands/staves.
    /// </summary>
    [System.Flags]
    public enum HealSourceMask {
        None   = 0,
        Spell  = 1,
        Scroll = 2,
        Potion = 4,
        All    = Spell | Scroll | Potion,
    }

    /// <summary>
    /// Which classes of source the CastSpell action may draw from. Flag-based so combinations
    /// (Spell+Scroll etc.) are expressible. Default is All. Spell covers spellbook casts and
    /// wands in quickslots. Structurally identical to HealSourceMask but kept separate so
    /// Heal's code path is untouched; a future refactor can unify them.
    /// </summary>
    [System.Flags]
    public enum SpellSourceMask {
        None   = 0,
        Spell  = 1,
        Scroll = 2,
        Potion = 4,
        All    = Spell | Scroll | Potion,
    }

    public enum ThrowSplashMode {
        Any,        // Use whatever splash item is first in inventory
        Strongest,  // Use the highest-damage splash item
        Cheapest    // Use the lowest-cost splash item
    }

    public enum ToggleMode {
        On,
        Off
    }

    // Arcanist Arcane Reservoir boost spent right before a CastSpell (ArcaneReservoir.cs).
    public enum ReservoirBoost {
        None,
        CasterLevel,
        DC
    }

    public enum TargetType {
        Self,
        AllyLowestHp,
        AllyWithCondition,
        AllyMissingBuff,
        EnemyNearest,
        EnemyLowestHp,
        EnemyHighestHp,
        EnemyHighestAC,
        EnemyLowestAC,
        EnemyHighestFort,
        EnemyLowestFort,
        EnemyHighestReflex,
        EnemyLowestReflex,
        EnemyHighestWill,
        EnemyLowestWill,
        EnemyHighestThreat,
        EnemyCreatureType,
        ConditionTarget,    // the enemy/ally that matched the triggering condition
        EnemyHighestHD,
        EnemyLowestHD,
        PointAtSelf,            // ~1 square in front of caster
        PointAtConditionTarget, // ~1 square toward caster from matched unit
        SpecificAlly,           // ally pinned by UniqueId stored in TargetDef.Filter
        EnemyMostEnemyNeighbors,// enemy with the most other living enemies within ~5 m (fireball / AoE damage)
        AllyMostAllyNeighbors,  // ally with the most other living allies within ~5 m (area buffs)
    }

    // Far sits between Medium and Long by distance but is appended last:
    // members are append-only, and the UI orders brackets via its own name
    // list (ConditionRowWidget.RangeBracketNames), not by enum index.
    public enum RangeBracket { Melee, Cone, Short, Medium, Long, Far }

    public static class RangeBrackets {
        /// <summary>True when <paramref name="distanceMeters"/> lies outside the bracket, i.e.
        /// a MoveToTarget rule still has ground to cover. Pure; shared by validator and tests.</summary>
        public static bool Beyond(float distanceMeters, RangeBracket b) {
            return distanceMeters > MaxMeters(b);
        }

        public static float MaxMeters(RangeBracket b) {
            switch (b) {
                case RangeBracket.Melee:  return 2f;
                case RangeBracket.Cone:   return 5f;
                case RangeBracket.Short:  return 10f;
                case RangeBracket.Medium: return 20f;
                case RangeBracket.Far:    return 30f;
                case RangeBracket.Long:   return 40f;
                default:                  return float.PositiveInfinity;
            }
        }

        public static bool TryParse(string s, out RangeBracket b) {
            return System.Enum.TryParse(s, ignoreCase: true, result: out b);
        }

        public static float LowerMeters(RangeBracket b) {
            switch (b) {
                case RangeBracket.Melee:  return 0f;
                case RangeBracket.Cone:   return MaxMeters(RangeBracket.Melee);
                case RangeBracket.Short:  return MaxMeters(RangeBracket.Cone);
                case RangeBracket.Medium: return MaxMeters(RangeBracket.Short);
                // Far (20–30 m) deliberately overlaps Long (20–40 m): rules
                // persist bracket names, so re-basing Long's lower edge onto
                // Far would silently narrow every existing Long rule.
                case RangeBracket.Far:    return MaxMeters(RangeBracket.Medium);
                case RangeBracket.Long:   return MaxMeters(RangeBracket.Medium);
                default:                  return 0f;
            }
        }

        public static string Label(RangeBracket b) {
            return WrathTactics.Localization.EnumLabels.For(b);
        }

        // Effective interval the evaluator actually checks for (bracket, op) —
        // mirrors the WithinRange operator switch in ConditionEvaluator
        // (Equal: lo<d<=hi; LessThan: d<=lo; GreaterOrEqual: d>lo; ...).
        // Null for operators outside the six comparison values.
        public static string EffectiveHint(RangeBracket b, ConditionOperator op) {
            float lo = LowerMeters(b);
            float hi = MaxMeters(b);
            switch (op) {
                case ConditionOperator.Equal:          return $"{M(lo)}–{M(hi)} m";
                case ConditionOperator.NotEqual:       return $"≠ {M(lo)}–{M(hi)} m";
                case ConditionOperator.LessOrEqual:    return $"≤ {M(hi)} m";
                case ConditionOperator.LessThan:       return $"≤ {M(lo)} m";
                case ConditionOperator.GreaterOrEqual: return $"> {M(lo)} m";
                case ConditionOperator.GreaterThan:    return $"> {M(hi)} m";
                default:                               return null;
            }
        }

        // Localized bracket name + effective interval: "Short (≤ 5 m)". The
        // static "( 10 m )" part of Label() misled users into reading "< Short
        // (10 m)" as "closer than 10 m" (it means "below the bracket": ≤ 5 m).
        public static string EffectiveLabel(RangeBracket b, ConditionOperator op) {
            var hint = EffectiveHint(b, op);
            if (hint == null) return Label(b);
            var label = Label(b);
            int paren = label.IndexOf('(');
            var name = paren > 0 ? label.Substring(0, paren).Trim() : label.Trim();
            return $"{name} ({hint})";
        }

        static string M(float meters) =>
            meters.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }
}
