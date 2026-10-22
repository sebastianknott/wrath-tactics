namespace WrathTactics.Models {
    /// <summary>
    /// Editor-side default for the CastDebuffGroup picker. ActionDef.DebuffGroup starts on None
    /// (enum index 0), which selects nothing; switching to a debuff action picks ArmorClass.
    /// Only the index-0 value is replaced, an explicit choice is kept.
    /// </summary>
    public static class DebuffDefaults {
        public static DebuffGroup GroupFor(ActionType action, DebuffGroup current) {
            if (action != ActionType.CastDebuffGroup) return current;
            return current == DebuffGroup.None ? DebuffGroup.ArmorClass : current;
        }
    }
}
