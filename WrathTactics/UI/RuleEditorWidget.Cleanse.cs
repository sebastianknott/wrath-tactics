using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Engine;
using WrathTactics.Localization;
using WrathTactics.Models;

namespace WrathTactics.UI {
    // RemoveCondition (WTW-30 cleanse): the action row carries the source mask, the kinds are a checkbox grid below it.
    public partial class RuleEditorWidget {
        const int CleanseKindsPerRow = 2;

        static List<AfflictionKinds> CleanseKindList() {
            return new List<AfflictionKinds>(CleanseTable.Bits(CleanseDefaults.All));
        }

        /// <summary>Number of rows SetupCleanseRows renders for a rule of this action type (UpdateHeight mirrors it).</summary>
        static int CleanseRowCount(ActionType type) {
            if (type != ActionType.RemoveCondition) return 0;
            int kinds = CleanseTable.Count(CleanseDefaults.All);
            return (kinds + CleanseKindsPerRow - 1) / CleanseKindsPerRow + 1;   // checkbox rows + all/none link row
        }

        // Source dropdown in the action row. Scrolls are never used for a cleanse, so the list is All / Spell /
        // Potion / Spell+Potion (potions only reach the caster itself).
        void SetupCleanseSelectors(GameObject row) {
            var sourceLabels = new List<string> {
                "source.all".i18n(), "source.spell_only".i18n(), "source.potion_only".i18n(), "source.spell_potion".i18n(),
            };
            var sourceValues = new List<SpellSourceMask> {
                SpellSourceMask.All,
                SpellSourceMask.Spell,
                SpellSourceMask.Potion,
                SpellSourceMask.Spell | SpellSourceMask.Potion,
            };
            int srcIdx = sourceValues.IndexOf(rule.Action.Sources);
            if (srcIdx < 0) srcIdx = 0;
            PopupSelector.CreateInRow(row, "CleanseSources", 0.61f, sourceLabels, srcIdx, idx => {
                rule.Action.Sources = sourceValues[idx];
                PersistEdit();
            });
        }

        // Kinds multi-picker: one checkbox per affliction kind plus "all / none" links. Every edit goes through
        // PersistEdit (never ConfigManager.Save directly, gotchas-persistence.md).
        void SetupCleanseRows(Transform parent) {
            if (rule.Action.Type != ActionType.RemoveCondition) return;
            var kinds = CleanseKindList();
            for (int start = 0; start < kinds.Count; start += CleanseKindsPerRow) {
                var row = Widgets.Row(parent, "CleanseKinds", Theme.InlineRowHeight);
                row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset((int)(Theme.BandPaddingX * 2), 0, 0, 0);
                for (int i = start; i < start + CleanseKindsPerRow; i++) {
                    if (i >= kinds.Count) {
                        var (pad, _) = UIHelpers.Create("Pad", row.transform);
                        Widgets.InRow(pad, 0f, 1f);
                        continue;
                    }
                    var kind = kinds[i];
                    var box = Widgets.Checkbox(row.transform, "Kind_" + kind, EnumLabels.For(kind),
                        (rule.Action.CleanseKinds & kind) != 0, on => {
                            rule.Action.CleanseKinds = on ? rule.Action.CleanseKinds | kind : rule.Action.CleanseKinds & ~kind;
                            PersistEdit();
                        });
                    Widgets.InRow(box, 0f, 1f);
                }
            }

            var links = Widgets.Row(parent, "CleanseKindLinks", Theme.InlineRowHeight);
            links.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset((int)(Theme.BandPaddingX * 2), 0, 0, 0);
            Widgets.InlineLink(links.transform, "CleanseAll", "button.cleanse_all".i18n(), () => {
                rule.Action.CleanseKinds = CleanseDefaults.All;
                PersistEdit();
                RebuildBody();
            });
            Widgets.InlineLink(links.transform, "CleanseNone", "button.cleanse_none".i18n(), () => {
                rule.Action.CleanseKinds = AfflictionKinds.None;
                PersistEdit();
                RebuildBody();
            });
        }
    }
}
