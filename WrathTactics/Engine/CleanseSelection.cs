using Kingmaker.EntitySystem.Entities;

namespace WrathTactics.Engine {
    /// <summary>
    /// Hands the remedy chosen during validation to the executor within one rule check, so the cast is the very
    /// option that was ranked (same latch idea as DebuffSelection). Cleared with the other matched entities before
    /// every rule; a mismatching owner or ally reads as "nothing latched".
    /// </summary>
    internal static class CleanseSelection {
        static UnitEntityData s_owner;
        static UnitEntityData s_ally;
        static CleanseChoice? s_choice;

        internal static void Remember(UnitEntityData owner, UnitEntityData ally, CleanseChoice choice) {
            s_owner = owner;
            s_ally = ally;
            s_choice = choice;
        }

        internal static CleanseChoice? Take(UnitEntityData owner, UnitEntityData ally) {
            var choice = s_choice;
            bool matches = choice.HasValue && ReferenceEquals(s_owner, owner) && ReferenceEquals(s_ally, ally);
            Clear();
            return matches ? choice : null;
        }

        internal static void Clear() {
            s_owner = null;
            s_ally = null;
            s_choice = null;
        }
    }
}
