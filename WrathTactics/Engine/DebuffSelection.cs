using Kingmaker.EntitySystem.Entities;

namespace WrathTactics.Engine {
    /// <summary>
    /// Hands the option chosen during validation to the executor within one rule check, so the cast is the very
    /// option that was ranked (the same latch idea as ConditionEvaluator.LastMatchedEnemy). Cleared with the other
    /// matched entities before every rule; a mismatching owner or target reads as "nothing latched".
    /// </summary>
    internal static class DebuffSelection {
        static UnitEntityData s_owner;
        static UnitEntityData s_target;
        static DebuffChoice? s_choice;

        internal static void Remember(UnitEntityData owner, UnitEntityData target, DebuffChoice choice) {
            s_owner = owner;
            s_target = target;
            s_choice = choice;
        }

        internal static DebuffChoice? Take(UnitEntityData owner, UnitEntityData target) {
            var choice = s_choice;
            bool matches = choice.HasValue && ReferenceEquals(s_owner, owner) && ReferenceEquals(s_target, target);
            Clear();
            return matches ? choice : null;
        }

        internal static void Clear() {
            s_owner = null;
            s_target = null;
            s_choice = null;
        }
    }
}
