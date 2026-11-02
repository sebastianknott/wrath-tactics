using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    // WTW-33: EnemyHDMinusPartyMedian. Same margin as EnemyHDMinusPartyLevel, but against the
    // median of the active party instead of its highest level, so one Legend or newcomer outlier
    // does not shift every threshold. The maximum-based property stays untouched.
    public static partial class ConditionEvaluator {
        // Per-Evaluate cache like CurrentPartyMaxLevel. -1 = not computed yet; the median of a
        // real party is never negative, an empty party gives 0 and fails closed (NaN margin).
        static float CurrentPartyMedian = -1f;
        static string CurrentPartyMedianLevels = "[]";
        static int CurrentPartyMedianMax;

        static void ResetPartyMedian() {
            CurrentPartyMedian = -1f;
            CurrentPartyMedianLevels = "[]";
            CurrentPartyMedianMax = 0;
        }

        // Player.Party is the active group: Player.UpdateCharacterLists/AddCharacterToLists
        // (Assembly-CSharp 2.7.x IL) add a unit only when its master's UnitPartCompanion.State
        // is InParty and the unit is not a pet. Remote, ExCompanion and InPartyDetached states
        // and guests without a companion part stay out. Pets are excluded on purpose (PartyAndPets
        // would pull in Eidolon / Animal Companion levels).
        static float ComputePartyMedianEffectiveLevel() {
            if (CurrentPartyMedian >= 0f) return CurrentPartyMedian;
            var levels = new List<int>();
            int max = 0;
            var party = Game.Instance?.Player?.Party;
            if (party != null) {
                foreach (var member in party) {
                    int eff = UnitExtensions.GetEffectiveHD(member);
                    levels.Add(eff);
                    if (eff > max) max = eff;
                }
            }
            CurrentPartyMedian = PartyMedian.Median(levels);
            CurrentPartyMedianLevels = PartyMedian.Describe(levels);
            CurrentPartyMedianMax = max;
            return CurrentPartyMedian;
        }

        static float ComputeHDMinusPartyMedian(UnitEntityData enemy) {
            if (enemy == null) return float.NaN;
            float median = ComputePartyMedianEffectiveLevel();
            int enemyHD = UnitExtensions.GetEffectiveHD(enemy);
            float margin = PartyMedian.Margin(enemyHD, median);
            if (float.IsNaN(margin)) return float.NaN;
            Log.Engine.Trace($"EnemyHDMinusPartyMedian: {enemy.CharacterName} HD={enemyHD} vs PartyMedian={PartyMedian.Format(median)} (levels {CurrentPartyMedianLevels}, max={CurrentPartyMedianMax}) -> margin={PartyMedian.Format(margin)}");
            return margin;
        }
    }
}
