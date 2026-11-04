using System;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// Spell groups usable as the ability filter of IsBeingCastOnByAlly. The filter stays a single
    /// string in Condition.Value: a 32-char GUID (one spell), empty (any) or "group:&lt;Key&gt;"
    /// (every spell that falls into the group). Not persisted as an enum, so no index to protect.
    /// </summary>
    [Flags]
    public enum AbilityGroup {
        None = 0,
        Healing = 1,
        Control = 2,
        Damage = 4
    }

    internal static class AbilityGroups {
        public const string Prefix = "group:";

        // Control: hard crowd-control and movement denial. Descriptors verified present in the game's
        // SpellDescriptor enum (IL).
        const SpellDescriptor ControlMask =
            SpellDescriptor.Paralysis | SpellDescriptor.Stun | SpellDescriptor.Sleep
            | SpellDescriptor.Daze | SpellDescriptor.Fear | SpellDescriptor.Compulsion
            | SpellDescriptor.Charm | SpellDescriptor.MovementImpairing;

        const SpellDescriptor DamageMask =
            SpellDescriptor.Fire | SpellDescriptor.Cold | SpellDescriptor.Electricity
            | SpellDescriptor.Acid | SpellDescriptor.Sonic;

        // Heal by descriptor in addition to the name keywords of ActionValidator.ClassifyHeal:
        // RestoreHP / ChannelPositiveHeal are the engine's own flags for HP restoration.
        const SpellDescriptor HealMask = SpellDescriptor.RestoreHP | SpellDescriptor.ChannelPositiveHeal;

        /// <summary>Filter value for a group, e.g. "group:Healing".</summary>
        public static string ToFilter(AbilityGroup group) => Prefix + group;

        public static bool IsGroupFilter(string filter) =>
            !string.IsNullOrEmpty(filter) && filter.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>Parses "group:Healing"; false for GUIDs, empty text, unknown or composite keys.</summary>
        public static bool TryParse(string filter, out AbilityGroup group) {
            group = AbilityGroup.None;
            if (!IsGroupFilter(filter)) return false;
            var key = filter.Substring(Prefix.Length);
            if (key != "Healing" && key != "Control" && key != "Damage") return false;
            return Enum.TryParse(key, false, out group);
        }

        /// <summary>Groups of a blueprint: heal by ClassifyHeal name keywords, rest by descriptor bits.</summary>
        public static AbilityGroup Classify(BlueprintAbility blueprint) {
            if (blueprint == null) return AbilityGroup.None;
            return Classify(ActionValidator.ClassifyHeal(blueprint) == HealEnergyType.Positive,
                blueprint.SpellDescriptor);
        }

        /// <summary>
        /// Groups of a cast ability. <paramref name="isPositiveHeal"/> is the name-keyword verdict
        /// (<c>ClassifyHeal == Positive</c>); the descriptor bits come from
        /// <c>BlueprintAbility.SpellDescriptor</c>. A spell can be in several groups.
        /// </summary>
        public static AbilityGroup Classify(bool isPositiveHeal, SpellDescriptor descriptor) {
            var groups = AbilityGroup.None;
            if (isPositiveHeal || (descriptor & HealMask) != 0) groups |= AbilityGroup.Healing;
            if ((descriptor & ControlMask) != 0) groups |= AbilityGroup.Control;
            if ((descriptor & DamageMask) != 0) groups |= AbilityGroup.Damage;
            return groups;
        }
    }
}
