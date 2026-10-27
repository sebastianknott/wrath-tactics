using System;

namespace WrathTactics.Engine {
    /// <summary>Mirrors RuleDispelMagic.CheckType (IL-verified, numeric values match).</summary>
    internal enum CleanseCheckType { None = 0, CasterLevel = 1, DC = 2, SkillDC = 3, SkillCasterLevel = 4 }

    /// <summary>
    /// Chance that a dispel check removes one affliction (WTW-30 cleanse). The game rolls
    /// d20 + bonus >= DC (RuleDispelMagic, DC 0 = automatic). The roll itself is never triggered here:
    /// the inputs come from non-rolling helpers and this class only does the arithmetic.
    /// </summary>
    internal static class CleanseChance {
        /// <summary>Used when an input of the check cannot be read (unverified path): a coin flip.</summary>
        internal const double UnknownCheckChance = 0.5;

        /// <summary>CasterLevel checks use DC = 11 + the affliction's caster level (IL: RuleDispelMagic).</summary>
        internal const int CasterLevelCheckBase = 11;

        /// <summary>Chances are bucketed upward in steps of this many percent for ranking.</summary>
        internal const int RoundingStepPercent = 10;

        /// <summary>
        /// Probability in [0,1]. Null inputs mean "could not be read" and give UnknownCheckChance.
        /// maxCasterLevel &lt;= 0 means no cap.
        /// </summary>
        internal static double Probability(CleanseCheckType check, int? dc, int? casterLevel, int? skillBonus,
                                           int? afflictionCasterLevel, int maxCasterLevel = 0) {
            switch (check) {
                case CleanseCheckType.None:
                    return 1.0;
                case CleanseCheckType.DC:
                    if (dc == null || casterLevel == null) return UnknownCheckChance;
                    return Roll(dc.Value, Cap(casterLevel.Value, maxCasterLevel));
                case CleanseCheckType.CasterLevel:
                    if (casterLevel == null || afflictionCasterLevel == null) return UnknownCheckChance;
                    return Roll(CasterLevelCheckBase + afflictionCasterLevel.Value, Cap(casterLevel.Value, maxCasterLevel));
                case CleanseCheckType.SkillDC:
                    if (dc == null || skillBonus == null) return UnknownCheckChance;
                    return Roll(dc.Value, skillBonus.Value);
                case CleanseCheckType.SkillCasterLevel:
                    if (skillBonus == null || afflictionCasterLevel == null) return UnknownCheckChance;
                    return Roll(CasterLevelCheckBase + afflictionCasterLevel.Value, skillBonus.Value);
                default:
                    return UnknownCheckChance;
            }
        }

        static int Cap(int casterLevel, int maxCasterLevel) {
            return maxCasterLevel > 0 ? Math.Min(casterLevel, maxCasterLevel) : casterLevel;
        }

        // d20 + bonus >= dc; a DC of 0 is an automatic success.
        static double Roll(int dc, int bonus) {
            if (dc == 0) return 1.0;
            double p = (21 - (dc - bonus)) / 20.0;
            return Math.Max(0.0, Math.Min(1.0, p));
        }

        /// <summary>Probability as a count of 10% steps, rounded up (0.95 -> 10, 0.30 -> 3, 0.31 -> 4).</summary>
        internal static int Steps(double probability) {
            int pct = (int)Math.Round(probability * 100.0);
            return (pct + RoundingStepPercent - 1) / RoundingStepPercent;
        }
    }
}
