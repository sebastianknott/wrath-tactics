using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>One thing the owner can cast right now to cure an ally (WTW-30 cleanse).</summary>
    internal sealed class CleanseOption {
        public AbilityData Ability;
        /// <summary>Set for inventory-backed options (potions): the stack to consume.</summary>
        public ItemEntity InventorySource;
        public DebuffSourceKind Kind;
        public int SpellLevel;
        public CleanseProfile Profile;
        /// <summary>Spell slot or class resource (true) versus consumable: wand charge, potion (false).</summary>
        public bool IsSlot;
        /// <summary>Stable within a tick: blueprint, metamagic, source kind, item, book and level.</summary>
        public string Key;

        public string Name => Ability.Name ?? Ability.Blueprint.name;
    }

    // Ally mode of the option enumerator (WTW-30 cleanse): same sources and gates as the debuff mode (slots,
    // charges, resources, AbilityData.IsAvailable), but the target is a friendly unit. Scrolls are never offered;
    // potions only when the ally is the owner (a potion affects the drinker). Rejections of real cleanses are
    // appended to `rejected` as "name [kind] (reason)" for the one decision log line.
    internal static partial class DebuffOptionEnumerator {
        internal static List<CleanseOption> EnumerateCleanse(UnitEntityData owner, UnitEntityData ally, SpellSourceMask mask,
                                                             List<string> rejected) {
            var result = new List<CleanseOption>();
            if ((mask & SpellSourceMask.Spell) != 0) {
                AddCleanseSpellbook(owner, ally, result, rejected);
                AddCleanseAbilities(owner, ally, result, rejected);
            }
            if ((mask & SpellSourceMask.Potion) != 0 && ReferenceEquals(owner, ally))
                AddCleansePotions(owner, ally, result, rejected);
            return result;
        }

        static CleanseProfile CleanseRelevant(AbilityData data) {
            var profile = CleanseClassifier.Get(data.Blueprint);
            return profile.IsCleanse ? profile : null;
        }

        // Decision b: the ability must be aimable at this very ally. Static flags first (IL-mirrored), then the
        // engine's own check (restrictions, dead targets). Point-castable alone does not qualify.
        static string AllyTargetRejection(UnitEntityData owner, UnitEntityData ally, AbilityData data) {
            if (!CleanseTargeting.AllyTargetableByFlags(data.Blueprint, ReferenceEquals(owner, ally))) return "cannot-target-ally";
            if (!data.CanTarget(new TargetWrapper(ally))) return "cannot-target-ally (engine)";
            return null;
        }

        static void Reject(List<string> rejected, AbilityData data, DebuffSourceKind kind, string reason) {
            rejected.Add($"{data.Name ?? data.Blueprint.name} [{kind}] ({reason})");
        }

        static void AddCleanse(List<CleanseOption> into, AbilityData data, CleanseProfile profile, DebuffSourceKind kind,
                               int level, bool isSlot, ItemEntity item) {
            into.Add(new CleanseOption {
                Ability = data, InventorySource = item, Kind = kind, SpellLevel = level, Profile = profile,
                IsSlot = isSlot, Key = KeyOf(data, kind, item),
            });
        }

        static void AddCleanseSpellbook(UnitEntityData owner, UnitEntityData ally, List<CleanseOption> into, List<string> rejected) {
            foreach (var book in owner.Spellbooks) {
                int maxLevel = book.MaxSpellLevel;
                for (int level = 0; level <= maxLevel; level++) {
                    AddCleanseBookList(owner, ally, book, level, book.GetKnownSpells(level), into, rejected);
                    AddCleanseBookList(owner, ally, book, level, book.GetSpecialSpells(level), into, rejected);
                    AddCleanseBookList(owner, ally, book, level, book.GetCustomSpells(level), into, rejected);
                }
            }
        }

        static void AddCleanseBookList(UnitEntityData owner, UnitEntityData ally, Kingmaker.UnitLogic.Spellbook book, int level,
                                       IEnumerable<AbilityData> spells,
                                       List<CleanseOption> into, List<string> rejected) {
            foreach (var spell in spells) {
                foreach (var data in Expand(spell)) {
                    var profile = CleanseRelevant(data);
                    if (profile == null) continue;
                    // Slot probe goes through the parent (variants are not keyed in the slot table).
                    if (book.GetAvailableForCastSpellCount(spell) == 0) { Reject(rejected, data, DebuffSourceKind.Spell, "no-slot"); continue; }
                    // IsAvailable also carries the material-component gate (IL: IsAvailableForCast -> CanBeCastByCaster
                    // rejects a player-faction caster without the diamond dust), so Restoration needs no own check.
                    if (!spell.IsAvailable) { Reject(rejected, data, DebuffSourceKind.Spell, "engine-unavailable: " + spell.GetUnavailableReason()); continue; }
                    var why = AllyTargetRejection(owner, ally, data);
                    if (why != null) { Reject(rejected, data, DebuffSourceKind.Spell, why); continue; }
                    AddCleanse(into, data, profile, DebuffSourceKind.Spell, level, true, null);
                }
            }
        }

        // Class abilities (Lay on Hands, channel, hexes ...) and item-backed abilities (wands, staves).
        static void AddCleanseAbilities(UnitEntityData owner, UnitEntityData ally, List<CleanseOption> into, List<string> rejected) {
            foreach (var fact in owner.Abilities.RawFacts) {
                var source = fact.Data.SourceItem;
                // Potions are offered from the inventory scan (self only), never through their quickslot ability.
                if (source != null && IsPotion(source.Blueprint as BlueprintItemEquipmentUsable)) continue;
                var kind = source == null ? DebuffSourceKind.ClassAbility : DebuffSourceKind.ItemAbility;
                foreach (var data in Expand(fact.Data)) {
                    var profile = CleanseRelevant(data);
                    if (profile == null) continue;
                    if (source != null && ActionValidator.IsOutOfCharges(source)) { Reject(rejected, data, kind, "no-slot: out of charges"); continue; }
                    if (source == null && !HasResource(owner, data)) { Reject(rejected, data, kind, "no-slot: resource spent"); continue; }
                    if (!data.IsAvailable) { Reject(rejected, data, kind, "engine-unavailable: " + data.GetUnavailableReason()); continue; }
                    var why = AllyTargetRejection(owner, ally, data);
                    if (why != null) { Reject(rejected, data, kind, why); continue; }
                    int level = (source?.Blueprint as BlueprintItemEquipmentUsable)?.SpellLevel ?? 0;
                    AddCleanse(into, data, profile, kind, level, source == null, null);
                }
            }
        }

        static bool IsPotion(BlueprintItemEquipmentUsable usable) {
            return usable != null && usable.Type == UsableItemType.Potion;
        }

        static void AddCleansePotions(UnitEntityData owner, UnitEntityData ally, List<CleanseOption> into, List<string> rejected) {
            var inventory = Game.Instance?.Player?.Inventory;
            if (inventory == null) return;
            foreach (var item in inventory) {
                if (item == null || item.Count <= 0) continue;
                var usable = item.Blueprint as BlueprintItemEquipmentUsable;
                if (!IsPotion(usable) || usable.Ability == null) continue;
                if (!CleanseClassifier.Get(usable.Ability).IsCleanse && usable.Ability.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityVariants>() == null) continue;
                var made = new AbilityData(usable.Ability, owner.Descriptor) {
                    OverrideCasterLevel = usable.CasterLevel,
                    OverrideSpellLevel = usable.SpellLevel,
                };
                foreach (var data in Expand(made)) {
                    var profile = CleanseRelevant(data);
                    if (profile == null) continue;
                    var why = AllyTargetRejection(owner, ally, data);
                    if (why != null) { Reject(rejected, data, DebuffSourceKind.ItemAbility, why); continue; }
                    AddCleanse(into, data, profile, DebuffSourceKind.ItemAbility, usable.SpellLevel, false, item);
                }
            }
        }

        // GUIDs (AssetGuid.ToString(), the format GateFacts uses) of every fact the unit holds: features, buffs and
        // plain facts, as ContextConditionCasterHasFact would find them.
        internal static HashSet<string> OwnerFactGuids(UnitEntityData owner) {
            var set = new HashSet<string>();
            var d = owner.Descriptor;
            foreach (var f in d.Facts.List) if (f?.Blueprint != null) set.Add(f.Blueprint.AssetGuid.ToString());
            foreach (var f in d.Buffs.Enumerable) if (f?.Blueprint != null) set.Add(f.Blueprint.AssetGuid.ToString());
            var features = d.Progression?.Features;
            if (features != null)
                foreach (var f in features.Enumerable) if (f?.Blueprint != null) set.Add(f.Blueprint.AssetGuid.ToString());
            return set;
        }
    }
}
