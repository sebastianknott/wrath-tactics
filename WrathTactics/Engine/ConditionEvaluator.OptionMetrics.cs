using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;

namespace WrathTactics.Engine {
    public static partial class ConditionEvaluator {
        // Measurements for an explicit (caster, target, ability) triple. The rule-scoped ComputeXxx wrappers read the
        // rule's own [Then] ability off CurrentAction; the debuff-group search measures abilities the rule does not
        // name, so it hands the ability in. CurrentOwner is the caster for the duration of the call (the probe and
        // the penetration lookup read it) and is restored afterwards: the search can run inside a rule evaluation.
        internal static float MeasureDCMinusSave(UnitEntityData caster, UnitEntityData target, AbilityData ability,
                                               Kingmaker.EntitySystem.Stats.SavingThrowType fallbackSave = Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown) {
            var previous = CurrentOwner;
            CurrentOwner = caster;
            try {
                return ComputeDCMinusSaveCore(target, ability, fallbackSave);
            } finally {
                CurrentOwner = previous;
            }
        }

        internal static float MeasureSpellResistMargin(UnitEntityData caster, UnitEntityData target, AbilityData ability) {
            var previous = CurrentOwner;
            CurrentOwner = caster;
            try {
                return ComputeSpellResistMarginCore(target, ability);
            } finally {
                CurrentOwner = previous;
            }
        }
    }

    /// <summary>
    /// Per-tick memo for the debuff search: the same (caster, option) and (option, target) pairs are measured by
    /// several rules and several evaluation passes within one tick. Cleared as soon as game time moves on, so a
    /// value never outlives the state it was read from.
    /// </summary>
    internal static class DebuffMetricCache {
        static readonly Dictionary<string, float> s_values = new Dictionary<string, float>();
        static TimeSpan s_stamp = TimeSpan.MinValue;

        internal static float GetOrCompute(string key, Func<float> compute) {
            var now = Game.Instance?.Player?.GameTime ?? TimeSpan.Zero;
            if (now != s_stamp) {
                s_values.Clear();
                s_stamp = now;
            }
            if (s_values.TryGetValue(key, out var cached)) return cached;
            float value = compute();
            s_values[key] = value;
            return value;
        }

        internal static void Reset() {
            s_values.Clear();
            s_stamp = TimeSpan.MinValue;
        }
    }
}
