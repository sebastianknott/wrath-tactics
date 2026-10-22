using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
        }

        // End of Evaluate(): a pass that was not cut short has seen every enemy, so the resume point is obsolete.
        // The latch itself stays: CanExecute/Execute read it after Evaluate returned (like LastMatchedEnemy).
        static void EndOptionPass() {
            if (!s_optionCapHit && CurrentOwner != null && CurrentAction != null)
                OptionCursor.Clear(OptionCursorKey());
        }

        static string OptionCursorKey() {
            return OptionCursor.KeyFor(CurrentOwner.UniqueId, RuntimeHelpers.GetHashCode(CurrentAction));
        }

        // Lets the enemy loop of a capped rule continue where the previous tick stopped.
        static IEnumerable<UnitEntityData> RotateToOptionCursor(IEnumerable<UnitEntityData> ordered, List<Condition> conds) {
            if (OptionBundling.FindBundle(conds) == null || CurrentOwner == null || CurrentAction == null) return ordered;
            string startId = OptionCursor.Get(OptionCursorKey());
            if (startId == null) return ordered;
            return OptionBundling.RotateTo(ordered, e => e.UniqueId == startId);
        }

        static List<Condition> BundleOptionRows(List<Condition> conds) {
            if (!conds.Any(c => c.Subject == ConditionSubject.GroupOption)) return conds;
            var bundled = OptionBundling.Bundle(conds, out int dropped);
            if (dropped > 0)
                Log.Engine.Trace($"GroupOption: {dropped} row(s) name another effect group, ignored (one effect group per condition group)");
            return bundled;
        }

        // An enemy passes iff at least one option of the bundle's group satisfies every option row. Options are
        // visited best first, so the latched one is the best that fits the rows.
        static bool EvaluateOptionBundle(OptionRowsBundle bundle, UnitEntityData enemy) {
            if (bundle.Group == DebuffGroup.None || CurrentOwner == null || CurrentAction == null) {
                Log.Engine.Trace("GroupOption: no effect group chosen or no active rule, returning false");
                return false;
            }
            if (s_optionByEnemy.ContainsKey(enemy)) return true;
            if (s_optionFailed.Contains(enemy)) return false;

            if (OptionBundling.CapReached(s_optionPairs)) {
                if (!s_optionCapHit) {
                    s_optionCapHit = true;
                    OptionCursor.Set(OptionCursorKey(), enemy.UniqueId);
                    Log.Engine.Trace($"GroupOption: pair cap {OptionBundling.MaxOptionPairsPerRuleTick} reached, resume at enemy {enemy.CharacterName}");
                }
                return false;
            }

            var ranked = ActionValidator.RankDebuffOptions(CurrentOwner, enemy, bundle.Group, CurrentAction.Sources);
            s_optionPairs += ranked.Count > 0 ? ranked.Count : 1;
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
