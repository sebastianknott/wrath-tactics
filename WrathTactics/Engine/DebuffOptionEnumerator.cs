using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>One thing a character can cast right now to debuff an enemy (WTW-30).</summary>
    internal sealed class DebuffOption {
        public AbilityData Ability;
        /// <summary>Set for inventory-backed options (scrolls, thrown items): the stack to consume.</summary>
        public ItemEntity InventorySource;
        public DebuffSourceKind Kind;
        public int SpellLevel;
        public DebuffProfile Profile;
        /// <summary>Stable within a tick: blueprint, metamagic, source kind and item.</summary>
        public string Key;

        public string Name => Ability.Name ?? Ability.Blueprint.name;
    }

    /// <summary>
    /// Lists every ally-safe debuff option of a group that the owner can cast right now, across spellbooks,
    /// class abilities, equipped item abilities, scrolls and thrown items (WTW-30). Modelled on FindBestHealEx
    /// but returns candidates instead of a pick; slot, charge and UMD gates are applied here, ranking is not.
    /// Rebuilt every call: slot and charge counts change by the second, and the blueprint walk behind each
    /// candidate is cached by DebuffClassifier.
    /// </summary>
    internal static partial class DebuffOptionEnumerator {
        internal static List<DebuffOption> Enumerate(UnitEntityData owner, DebuffGroup group, SpellSourceMask mask) {
            var result = new List<DebuffOption>();
            bool wantSpell = (mask & SpellSourceMask.Spell) != 0;
            bool wantScroll = (mask & SpellSourceMask.Scroll) != 0;

            if (wantSpell) {
                AddSpellbookOptions(owner, group, result);
                AddAbilityOptions(owner, group, result);
            }
            if (wantScroll) AddInventoryOptions(owner, group, result);
            return result;
        }

        // Expands a variant container into its variants; every other ability stands for itself.
        static IEnumerable<AbilityData> Expand(AbilityData data) {
            var variants = data.Blueprint.GetComponent<AbilityVariants>();
            if (variants?.m_Variants == null) {
                yield return data;
                yield break;
            }
            foreach (var v in variants.Variants)
                if (v != null) yield return ActionValidator.MakeVariantData(data, v);
        }

        // Classification gate shared by every source: a candidate is only interesting (and only worth a
        // rejection log line) when it is an ally-safe debuff of the wanted group.
        static DebuffProfile Relevant(AbilityData data, DebuffGroup group) {
            var profile = DebuffClassifier.Get(data.Blueprint);
            return profile.IsDebuff && profile.AllySafe && profile.HasGroup(group) ? profile : null;
        }

        // Cheap blueprint-level pre-check for inventory items, before an AbilityData is built per stack and per tick:
        // a variant container stands for its variants (classified after Expand), anything else is classified now.
        static bool MayBeRelevant(BlueprintAbility ability, DebuffGroup group) {
            if (ability.GetComponent<AbilityVariants>() != null) return true;
            var profile = DebuffClassifier.Get(ability);
            return profile.IsDebuff && profile.AllySafe && profile.HasGroup(group);
        }

        static string KeyOf(AbilityData data, DebuffSourceKind kind, ItemEntity item) {
            int meta = data.MetamagicData != null ? (int)data.MetamagicData.MetamagicMask : 0;
            // The same spell sits in several spellbooks and at several levels with separate slot pools.
            string book = data.Spellbook?.Blueprint?.AssetGuid.ToString() ?? "";
            return data.Blueprint.AssetGuid + "|" + meta + "|" + (int)kind + "|" + (item?.UniqueId ?? "")
                + "|" + book + "|" + data.SpellLevelInSpellbook;
        }

        static void Add(List<DebuffOption> into, AbilityData data, DebuffProfile profile, DebuffSourceKind kind,
                        int level, ItemEntity item) {
            into.Add(new DebuffOption {
                Ability = data, InventorySource = item, Kind = kind, SpellLevel = level,
                Profile = profile, Key = KeyOf(data, kind, item),
            });
        }

        static void AddSpellbookOptions(UnitEntityData owner, DebuffGroup group, List<DebuffOption> into) {
            foreach (var book in owner.Spellbooks) {
                int maxLevel = book.MaxSpellLevel;
                for (int level = 0; level <= maxLevel; level++) {
                    AddBookList(owner, group, book, level, book.GetKnownSpells(level), into);
                    AddBookList(owner, group, book, level, book.GetSpecialSpells(level), into);
                    // Prepared metamagic variants live in the custom list, not in the known one.
                    AddBookList(owner, group, book, level, book.GetCustomSpells(level), into);
                }
            }
        }

        static void AddBookList(UnitEntityData owner, DebuffGroup group, Kingmaker.UnitLogic.Spellbook book,
                                int level, IEnumerable<AbilityData> spells, List<DebuffOption> into) {
            foreach (var spell in spells) {
                foreach (var data in Expand(spell)) {
                    var profile = Relevant(data, group);
                    if (profile == null) continue;
                    // Slot probe goes through the parent (variants are not keyed in the slot table).
                    if (book.GetAvailableForCastSpellCount(spell) == 0) {
                        Log.Engine.Trace($"DebuffGroup {group}: '{data.Name}' [{DebuffSourceKind.Spell}] rejected (no-slot)");
                        continue;
                    }
                    if (!spell.IsAvailable) {
                        Log.Engine.Trace($"DebuffGroup {group}: '{data.Name}' [{DebuffSourceKind.Spell}] rejected (no-slot: engine-unavailable, {spell.GetUnavailableReason()})");
                        continue;
                    }
                    Add(into, data, profile, DebuffSourceKind.Spell, level, null);
                }
            }
        }

        // Class, hex, domain and maneuver abilities (no source item) and item-backed abilities (wands, equipment).
        static void AddAbilityOptions(UnitEntityData owner, DebuffGroup group, List<DebuffOption> into) {
            foreach (var fact in owner.Abilities.RawFacts) {
                var source = fact.Data.SourceItem;
                var kind = source == null ? DebuffSourceKind.ClassAbility : DebuffSourceKind.ItemAbility;
                foreach (var data in Expand(fact.Data)) {
                    var profile = Relevant(data, group);
                    if (profile == null) continue;
                    if (source != null && ActionValidator.IsOutOfCharges(source)) {
                        Log.Engine.Trace($"DebuffGroup {group}: '{data.Name}' [{kind}] rejected (no-slot: out of charges)");
                        continue;
                    }
                    if (source == null && !HasResource(owner, data)) {
                        Log.Engine.Trace($"DebuffGroup {group}: '{data.Name}' [{kind}] rejected (no-slot: resource spent)");
                        continue;
                    }
                    if (!data.IsAvailable) {
                        Log.Engine.Trace($"DebuffGroup {group}: '{data.Name}' [{kind}] rejected (no-slot: engine-unavailable, {data.GetUnavailableReason()})");
                        continue;
                    }
                    Add(into, data, profile, kind, 0, null);
                }
            }
        }

        static bool HasResource(UnitEntityData owner, AbilityData data) {
            // Mirrors the class-ability path of FindCastSpellSource: only a spending component counts, and the
            // ability may override the resource it draws from.
            var resource = data.Blueprint.GetComponent<AbilityResourceLogic>();
            if (resource == null || !resource.IsSpendResource) return true;
            var required = (BlueprintScriptableObject)data.OverrideRequiredResource ?? resource.RequiredResource;
            if (required == null) return true;
            // Engine-authoritative cost, honours cost-modifying facts (same as FindBestHealEx).
            return owner.Resources.GetResourceAmount(required) >= resource.CalculateCost(data);
        }

        // Scrolls (UMD-gated) and thrown items (usables that are neither potion, scroll nor wand).
        // Potions never qualify: they only affect the drinker.
        static void AddInventoryOptions(UnitEntityData owner, DebuffGroup group, List<DebuffOption> into) {
            var inventory = Game.Instance?.Player?.Inventory;
            if (inventory == null) return;
            foreach (var item in inventory) {
                if (item == null || item.Count <= 0) continue;
                var usable = item.Blueprint as BlueprintItemEquipmentUsable;
                if (usable?.Ability == null) continue;
                if (!MayBeRelevant(usable.Ability, group)) continue;
                bool isScroll = usable.Type == UsableItemType.Scroll;
                bool thrown = usable.Type == UsableItemType.Other || usable.Type == UsableItemType.Utility;
                if (!isScroll && !thrown) continue;

                var made = new AbilityData(usable.Ability, owner.Descriptor) {
                    OverrideCasterLevel = usable.CasterLevel,
                    OverrideSpellLevel = usable.SpellLevel,
                };
                var kind = isScroll ? DebuffSourceKind.Scroll : DebuffSourceKind.ThrownItem;
                foreach (var data in Expand(made)) {
                    var profile = Relevant(data, group);
                    if (profile == null) continue;
                    if (isScroll && !ScrollUsable(owner, usable)) {
                        Log.Engine.Trace($"DebuffGroup {group}: '{data.Name}' [{kind}] rejected (no-slot: UMD too low)");
                        continue;
                    }
                    Add(into, data, profile, kind, usable.SpellLevel, item);
                }
            }
        }

        // UMD gate as in FindCastSpellSource: skip when the unit would fail the activation check more than half
        // the time, unless it can cast the spell natively.
        static bool ScrollUsable(UnitEntityData owner, BlueprintItemEquipmentUsable usable) {
            if (ActionValidator.CanCastSpellFromSpellbook(owner, usable.Ability)) return true;
            int dc = 20 + usable.CasterLevel;
            int umd = owner.Stats.SkillUseMagicDevice.ModifiedValue;
            return umd + 11 >= dc;
        }
    }
}
