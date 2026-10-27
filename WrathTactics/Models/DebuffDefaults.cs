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

        /// <summary>
        /// Source mask for a CastDebuffGroup rule. The editor offers All, Spell, Scroll and Spell+Scroll
        /// (a potion never debuffs an enemy); a mask carried over from another action (Potion-only, None)
        /// would show a wrong dropdown entry and enumerate nothing, so it falls back to All.
        /// </summary>
        public static SpellSourceMask SourcesFor(ActionType action, SpellSourceMask current) {
            if (action != ActionType.CastDebuffGroup) return current;
            switch (current) {
                case SpellSourceMask.All:
                case SpellSourceMask.Spell:
                case SpellSourceMask.Scroll:
                case SpellSourceMask.Spell | SpellSourceMask.Scroll:
                    return current;
                default:
                    return SpellSourceMask.All;
            }
        }
    }
}
