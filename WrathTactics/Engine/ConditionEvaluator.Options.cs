using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Kingmaker.EntitySystem.Entities;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static partial class ConditionEvaluator {
        // GroupOption binding (WTW-30 phase b). The rows of one condition group hold for a single option of the
        // named effect group; the option that satisfied them for an enemy is latched so CastConditionOption casts
        // exactly that one. All state is per Evaluate() / per group (see ClearMatchedEntities).
        static readonly Dictionary<UnitEntityData, DebuffChoice> s_optionByEnemy = new Dictionary<UnitEntityData, DebuffChoice>();
        static readonly HashSet<UnitEntityData> s_optionFailed = new HashSet<UnitEntityData>();
        static int s_optionPairs;
        static bool s_optionCapHit;
        static bool s_optionBundleEvaluated;
        // The count path (EnemyCount rows) has to see every enemy, so it runs with the pair cap switched off.
        static bool s_optionEnforceCap = true;
        // Per-pass cache: the option list of one group and the enemy positions do not change between enemies.
        static DebuffGroup s_passGroup;
        static List<DebuffOption> s_passOptions;
        static List<Vector3> s_passPositions;

        /// <summary>The option that satisfied the GroupOption rows for <see cref="LastMatchedEnemy"/>, if any.</summary>
        internal static DebuffChoice? LastMatchedChoice =>
            LastMatchedEnemy != null && s_optionByEnemy.TryGetValue(LastMatchedEnemy, out var c) ? c : (DebuffChoice?)null;

        internal static DebuffOption LastMatchedOption => LastMatchedChoice?.Option;

        static void ClearOptionLatch() {
            s_optionByEnemy.Clear();
            s_optionFailed.Clear();
        }

        static void BeginOptionPass() {
            ClearOptionLatch();
            s_optionPairs = 0;
            s_optionCapHit = false;
            s_optionBundleEvaluated = false;
            s_optionEnforceCap = true;
            s_passGroup = DebuffGroup.None;
            s_passOptions = null;
            s_passPositions = null;
        }

        // End of Evaluate(): a pass that evaluated the bundle without being cut short has seen every enemy, so the
        // resume point is obsolete. A tick that never reached the bundle keeps it. The latch itself stays:
        // CanExecute/Execute read it after Evaluate returned (like LastMatchedEnemy).
        static void EndOptionPass() {
            if (OptionBundling.ShouldClearCursor(s_optionBundleEvaluated, s_optionCapHit) && CurrentOwner != null && CurrentRuleId != null)
                OptionCursor.Clear(OptionCursorKey());
        }

        // Keyed by rule id (stable, unlike an object hash) and owner.
        static string OptionCursorKey() {
            return OptionCursor.KeyFor(CurrentOwner.UniqueId, CurrentRuleId);
        }

        // Lets the enemy loop of a capped rule continue where the previous tick stopped.
        static IEnumerable<UnitEntityData> RotateToOptionCursor(IEnumerable<UnitEntityData> ordered, List<Condition> conds) {
            if (OptionBundling.FindBundle(conds) == null || CurrentOwner == null || CurrentRuleId == null) return ordered;
            string startId = OptionCursor.Get(OptionCursorKey());
            if (startId == null) return ordered;
            return OptionBundling.RotateTo(ordered, e => e.UniqueId == startId);
        }

        static List<Condition> BundleOptionRows(List<Condition> conds) {
            return OptionBundling.Bundle(conds);
        }

        // An enemy passes iff at least one option of the bundle's group satisfies every option row. Options are
        // visited best first, so the latched one is the best that fits the rows.
        static bool EvaluateOptionBundle(OptionRowsBundle bundle, UnitEntityData enemy) {
            if (bundle.Group == DebuffGroup.None || CurrentOwner == null || CurrentAction == null || CurrentRuleId == null) {
                Log.Engine.Trace("GroupOption: no effect group chosen or no active rule, returning false");
                return false;
            }
            s_optionBundleEvaluated = true;
            if (s_optionByEnemy.ContainsKey(enemy)) return true;
            if (s_optionFailed.Contains(enemy)) return false;

            if (OptionBundling.CapReached(s_optionPairs, s_optionEnforceCap)) {
                if (!s_optionCapHit) {
                    s_optionCapHit = true;
                    OptionCursor.Set(OptionCursorKey(), enemy.UniqueId);
                    Log.Engine.Trace($"GroupOption: pair cap {OptionBundling.MaxOptionPairsPerRuleTick} reached, resume at enemy {enemy.CharacterName}");
                }
                return false;
            }

            if (s_passOptions == null || s_passGroup != bundle.Group) {
                s_passGroup = bundle.Group;
                s_passOptions = DebuffOptionEnumerator.Enumerate(CurrentOwner, bundle.Group, CurrentAction.Sources);
                s_passPositions = ActionValidator.EnemyPositions(CurrentOwner);
            }
            var ranked = ActionValidator.RankDebuffOptions(CurrentOwner, enemy, bundle.Group, s_passOptions, s_passPositions);
            // Budget counts every option looked at, rejected ones included (their checks cost too).
            s_optionPairs += s_passOptions.Count > 0 ? s_passOptions.Count : 1;
            foreach (var choice in ranked) {
                var values = new OptionValues { Dcms = choice.Dcms, Srm = choice.Srm, Effect = choice.Magnitude, Hits = choice.Hits, Kind = choice.Option.Kind };
                if (!OptionBundling.AllRowsMatch(bundle.Rows, values)) continue;
                s_optionByEnemy[enemy] = choice;
                Log.Engine.Trace($"GroupOption {bundle.Group}: '{choice.Option.Name}' [{choice.Option.Kind}] satisfies all rows for {enemy.CharacterName}");
                return true;
            }
            s_optionFailed.Add(enemy);
            return false;
        }
    }
}
