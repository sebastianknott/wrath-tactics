namespace WrathTactics.Models {
    /// <summary>
    /// Editor-side default for the cleanse kinds (WTW-30 cleanse). A fresh rule starts with
    /// CleanseKinds = None (flag value 0), which would never fire, so switching to RemoveCondition
    /// ticks every kind. Only the empty value is replaced: an explicit player choice is kept.
    /// </summary>
    public static class CleanseDefaults {
        public static readonly AfflictionKinds All =
            AfflictionKinds.Paralysis | AfflictionKinds.Confusion | AfflictionKinds.StunDaze
            | AfflictionKinds.FearShaken | AfflictionKinds.NauseaSickened | AfflictionKinds.Blindness
            | AfflictionKinds.Poison | AfflictionKinds.Disease | AfflictionKinds.Curse
            | AfflictionKinds.FatigueExhausted | AfflictionKinds.StatDebuffAbilityDamage
            | AfflictionKinds.NegativeLevel | AfflictionKinds.Petrified | AfflictionKinds.SlowedEntangled
            | AfflictionKinds.Staggered;

        public static AfflictionKinds KindsFor(ActionType action, AfflictionKinds current) {
            if (action == ActionType.RemoveCondition && current == AfflictionKinds.None) return All;
            return current;
        }
    }
}
