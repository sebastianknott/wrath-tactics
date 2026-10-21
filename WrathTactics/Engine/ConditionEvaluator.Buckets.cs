using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static partial class ConditionEvaluator {
        static bool EvaluateEnemyBucket(List<Condition> conds, UnitEntityData owner) {
            var enemies = GetVisibleEnemies(owner).ToList();
            var nonCountConds = conds.Where(c => c.Subject != ConditionSubject.EnemyCount).ToList();
            var countConds    = conds.Where(c => c.Subject == ConditionSubject.EnemyCount).ToList();

            // Count-only groups stay evaluable with zero visible enemies so
            // zero-accepting gates (EnemyCount = 0 / <= N / < N / !=) can pass —
            // mirrors EvaluateAllyBucket. Existential conditions still fail below
            // (no enemy can match), so only pure count gates get through.
            if (enemies.Count == 0 && countConds.Count == 0)
                return false;

            // Sort by the first Pick subject's metric (if any).
            Condition pickCond = nonCountConds.FirstOrDefault(c => PickMetric(c.Subject, out _) != null);
            IEnumerable<UnitEntityData> ordered = enemies;
            if (pickCond != null) {
                var metric = PickMetric(pickCond.Subject, out bool biggest);
                ordered = biggest
                    ? enemies.OrderByDescending(metric)
                    : enemies.OrderBy(metric);
            }

            // Pick-or-Enemy path: find first enemy that passes every non-Count condition.
            UnitEntityData matchedEnemy = null;
            if (nonCountConds.Count > 0) {
                foreach (var enemy in ordered) {
                    bool allPass = true;
                    foreach (var c in nonCountConds) {
                        if (!EvaluateUnitProperty(c, enemy)) { allPass = false; break; }
                    }
                    if (allPass) { matchedEnemy = enemy; break; }
                }
                if (matchedEnemy == null) return false;
            }

            // Count path: count enemies that pass every non-Count condition AND the rows of
            // each count set. Unflagged Count rows form ONE shared set (threshold = max Value2,
            // operator = first row's CountOperator); every row flagged CountSeparately is its own
            // set with its own threshold/operator. All sets must hold (see CountSet).
            if (countConds.Count > 0) {
                var sets = CountSet.Build(countConds);
                var tally = CountSets.Tally(enemies, sets,
                    enemy => nonCountConds.All(c => EvaluateUnitProperty(c, enemy)),
                    MatchesPropertyThreshold);
                if (!CountSets.AllPass(sets, tally.Counts)) {
                    Log.Engine.Trace($"EnemyBucket miss: {DescribeCountSets(sets, tally.Counts)}");
                    return false;
                }
                // Count-only groups (no per-enemy conditions) previously latched nothing,
                // so TargetType.ConditionTarget resolved to None and the rule silently
                // skipped at validation ("rule matches but nobody attacks" report). Fall
                // back to the first counted match. Stays null for pass-with-zero gates
                // (e.g. EnemyCount < 3 passing on count=0) — no enemy satisfied the
                // predicate, so there is no ConditionTarget to offer.
                if (matchedEnemy == null) matchedEnemy = CountSets.FirstCountedAny(tally);
            }

            // Latch only after the whole bucket passed — latching before the count
            // gate leaks a unit from a failing group into later OR-groups.
            if (matchedEnemy != null) LastMatchedEnemy = matchedEnemy;
            return true;
        }

        // Ally analogue of EvaluateEnemyBucket — no Pick subjects exist for Ally scope,
        // so the logic is simpler: a matching Ally (for the non-Count path) and/or a
        // satisfied Count threshold.
        static bool EvaluateAllyBucket(List<Condition> conds, UnitEntityData owner) {
            var allies = GetAllPartyMembers(owner).Where(a => a != owner).ToList();
            var nonCountConds = conds.Where(c =>
                c.Subject != ConditionSubject.AllyCount
                && c.Subject != ConditionSubject.AllyByName).ToList();
            var countConds    = conds.Where(c => c.Subject == ConditionSubject.AllyCount).ToList();
            if (allies.Count == 0 && countConds.Count == 0)
                return false;

            // AllyByName conditions pin a specific ally per condition (Value2 = UniqueId).
            // They short-circuit the iterative match below: each must pass against its
            // own pinned ally, or the whole group fails. LastMatchedAlly latches onto
            // the first pinned ally (at the end, once the whole bucket passed) so
            // ConditionTarget / PointAtConditionTarget can find it downstream.
            UnitEntityData firstPinned = null;
            var byNameConds = conds.Where(c => c.Subject == ConditionSubject.AllyByName).ToList();
            foreach (var c in byNameConds) {
                var pinned = AllyProvider.Resolve(c.Value2);
                if (pinned == null) {
                    Log.Engine.Trace($"AllyByName: cannot resolve UniqueId='{c.Value2}' (Property={c.Property})");
                    return false;
                }
                if (!EvaluateUnitProperty(c, pinned)) return false;
                if (firstPinned == null) firstPinned = pinned;
            }

            UnitEntityData matchedAlly = null;
            if (nonCountConds.Count > 0) {
                foreach (var ally in allies) {
                    bool allPass = true;
                    foreach (var c in nonCountConds) {
                        if (!EvaluateUnitProperty(c, ally)) { allPass = false; break; }
                    }
                    if (allPass) { matchedAlly = ally; break; }
                }
                if (matchedAlly == null) return false;
            }

            if (countConds.Count > 0) {
                var sets = CountSet.Build(countConds);
                // AllyCount historically includes self; keep that behavior (use GetAllPartyMembers
                // without filtering owner) for Count, to match the previous EvaluateAllyCount.
                var members = GetAllPartyMembers(owner).ToList();
                var tally = CountSets.Tally(members, sets,
                    ally => nonCountConds.All(c => EvaluateUnitProperty(c, ally)),
                    MatchesPropertyThreshold);
                // Trace only on a miss (per-tick spam otherwise).
                if (!CountSets.AllPass(sets, tally.Counts)) {
                    var perAlly = new List<string>();
                    for (int i = 0; i < members.Count; i++) {
                        var ally = members[i];
                        int hpPct = ally.Stats.HitPoints.ModifiedValue > 0
                            ? (int)(100f * ally.HPLeft / ally.Stats.HitPoints.ModifiedValue) : 0;
                        float dist = UnityEngine.Vector3.Distance(owner.Position, ally.Position);
                        var passed = tally.PerUnit[i];
                        string verdict = passed == null ? "fail@non-count"
                            : string.Join("/", passed.Select((p, k) => p ? "in" : "out"));
                        perAlly.Add($"{ally.CharacterName}(hp={hpPct}%,d={dist:F1}m):{verdict}");
                    }
                    Log.Engine.Trace($"AllyBucket miss: {DescribeCountSets(sets, tally.Counts)} [{string.Join(", ", perAlly)}]");
                    return false;
                }
                // Count-only fallback so ConditionTarget resolves — see EvaluateEnemyBucket.
                // Note: may latch the owner (AllyCount includes self by design).
                if (matchedAlly == null) matchedAlly = CountSets.FirstCountedAny(tally);
            }

            // Latch only after the whole bucket passed (see EvaluateEnemyBucket).
            // Pinned-ally precedence over iterative match mirrors the pre-refactor
            // "first writer wins" order: byName latched before the non-count loop.
            var latch = firstPinned ?? matchedAlly;
            if (latch != null) LastMatchedAlly = latch;
            return true;
        }

        // "set0[AllyCount HpPercent]: count=2 GreaterOrEqual threshold=3 -> miss; set1[...]: ...".
        // One entry per count set so independent (CountSeparately) counts are visible in logs.
        static string DescribeCountSets(List<CountSet> sets, int[] counts) {
            var parts = new List<string>();
            for (int i = 0; i < sets.Count; i++) {
                string rows = string.Join("+", sets[i].Rows.Select(r => r.Property.ToString()));
                parts.Add($"set{i}[{rows}]: count={counts[i]} {sets[i].Operator} threshold={sets[i].Threshold} -> {(sets[i].Passes(counts[i]) ? "ok" : "miss")}");
            }
            return string.Join("; ", parts);
        }

        // Count threshold = max usable Value2 across Count rows; 1 when NO row
        // yields a usable value (empty/garbage field keeps the legacy "at least 1"
        // default). An explicit 0 must survive: EnemyCount = 0 / <= 0 are valid
        // "no matching enemies" gates — a max-accumulator seeded at 1 (pre-1.21.1)
        // silently clamped 0 to 1. Negative and NaN parses are rejected like
        // garbage (the old clamp already swallowed them): letting them through
        // would create always-true gates (count >= -1) or poison the fold
        // (Math.Max propagates NaN; NaN also fails the >= 0 check). +Infinity
        // is rejected as new behavior — the old fold let it through as an
        // always-false >= gate, useless by construction.
        internal static float ResolveCountThreshold(List<Condition> countConds) {
            float? threshold = null;
            foreach (var cc in countConds) {
                if (float.TryParse(cc.Value2, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out float v)
                    && v >= 0f && !float.IsInfinity(v)) {
                    threshold = threshold.HasValue ? System.Math.Max(threshold.Value, v) : v;
                }
            }
            return threshold ?? 1f;
        }
    }
}
