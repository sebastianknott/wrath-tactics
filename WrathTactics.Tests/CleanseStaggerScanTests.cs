using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.ElementsSystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;
using Xunit.Abstractions;

namespace WrathTactics.Tests {
    // WTW-30 F1: which real abilities could touch an active Staggered condition (scan over the game's blueprints).
    public class CleanseStaggerScanTests {
        readonly ITestOutputHelper m_out;
        public CleanseStaggerScanTests(ITestOutputHelper output) { m_out = output; }

        // The game's m_* reference fields are private in the non-publicized test reference.
        static BlueprintBuff BuffField(object owner, string field) {
            var f = owner.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var r = f?.GetValue(owner);
            return r == null ? null : r.GetType().GetMethod("Get", Type.EmptyTypes)?.Invoke(r, null) as BlueprintBuff;
        }

        static IEnumerable<BlueprintBuff> BuffList(object owner, string field) {
            var f = owner.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var arr = f?.GetValue(owner) as System.Collections.IEnumerable;
            if (arr == null) yield break;
            foreach (var r in arr)
                yield return r?.GetType().GetMethod("Get", Type.EmptyTypes)?.Invoke(r, null) as BlueprintBuff;
        }

        static bool BuffIsStagger(BlueprintBuff b) {
            if (b == null) return false;
            if ((b.SpellDescriptor & SpellDescriptor.Staggered) != 0) return true;
            if (b.ComponentsArray == null) return false;
            foreach (var c in b.ComponentsArray) {
                if (c is AddCondition ac && ac.Condition == UnitCondition.Staggered) return true;
                if (c is BuffStatusCondition bs && bs.Condition == UnitCondition.Staggered) return true;
            }
            return false;
        }

        static string Protects(BlueprintComponent c) {
            switch (c) {
                case AddConditionImmunity ci when ci.Condition == UnitCondition.Staggered: return "AddConditionImmunity(Staggered)";
                case BuffDescriptorImmunity di when ((SpellDescriptor)di.Descriptor & SpellDescriptor.Staggered) != 0: return "BuffDescriptorImmunity(Staggered)";
                case SuppressBuffs sb: {
                    if (((SpellDescriptor)sb.Descriptor & SpellDescriptor.Staggered) != 0) return "SuppressBuffs(descriptor Staggered)";
                    foreach (var b in BuffList(sb, "m_Buffs")) if (BuffIsStagger(b)) return "SuppressBuffs(buff " + b.name + ")";
                    return null;
                }
                default: return null;
            }
        }

        static IEnumerable<string> Touch(ActionList list) {
            if (list?.Actions == null) yield break;
            foreach (var a in list.Actions) {
                switch (a) {
                    case Conditional c:
                        foreach (var s in Touch(c.IfTrue)) yield return s;
                        foreach (var s in Touch(c.IfFalse)) yield return s;
                        break;
                    case ContextActionSavingThrow st:
                        foreach (var s in Touch(st.Actions)) yield return s;
                        break;
                    case ContextActionConditionalSaved cs:
                        foreach (var s in Touch(cs.Failed)) yield return s;
                        foreach (var s in Touch(cs.Succeed)) yield return s;
                        break;
                    case ContextActionRemoveBuffsByDescriptor rd when ((SpellDescriptor)rd.SpellDescriptor & SpellDescriptor.Staggered) != 0:
                        yield return "RemoveBuffsByDescriptor";
                        break;
                    case ContextActionDispelMagic dm when ((SpellDescriptor)dm.Descriptor & SpellDescriptor.Staggered) != 0:
                        yield return "DispelMagic";
                        break;
                    case ContextActionRemoveBuff rb when !rb.ToCaster && BuffIsStagger(BuffField(rb, "m_Buff")):
                        yield return "RemoveBuff " + BuffField(rb, "m_Buff").name;
                        break;
                    case ContextActionRemoveBuffSingleStack rs when BuffIsStagger(BuffField(rs, "m_TargetBuff")):
                        yield return "RemoveBuffSingleStack " + BuffField(rs, "m_TargetBuff").name;
                        break;
                    case ContextActionApplyBuff ap when ap.Buff?.ComponentsArray != null:
                        foreach (var comp in ap.Buff.ComponentsArray) {
                            var p = Protects(comp);
                            if (p != null) yield return "ApplyBuff " + ap.Buff.name + " -> " + p;
                        }
                        break;
                }
            }
        }

        static IEnumerable<string> TouchAbility(BlueprintAbility bp) {
            var run = bp.ComponentsArray?.OfType<AbilityEffectRunAction>().FirstOrDefault();
            if (run != null) foreach (var s in Touch(run.Actions)) yield return s;
            var touch = bp.ComponentsArray?.OfType<AbilityEffectStickyTouch>().FirstOrDefault()?.TouchDeliveryAbility;
            if (touch != null) {
                var r2 = touch.ComponentsArray?.OfType<AbilityEffectRunAction>().FirstOrDefault();
                if (r2 != null) foreach (var s in Touch(r2.Actions)) yield return s;
            }
        }

        [RealBlueprintFact]
        public void Scan_every_ability_that_touches_staggered() {
            var hits = new List<string>();
            foreach (var path in RealBlueprints.PathsOfType("BlueprintAbility")) {
                BlueprintAbility bp;
                try { bp = RealBlueprints.ByPath<BlueprintAbility>(path); } catch (Exception) { continue; }
                foreach (var s in TouchAbility(bp).Distinct()) hits.Add(path + " :: " + s);
            }
            m_out.WriteLine("abilities touching Staggered: " + hits.Count);
            foreach (var h in hits) m_out.WriteLine("  " + h);
            var buffHits = new List<string>();
            foreach (var path in RealBlueprints.PathsOfType("BlueprintBuff")) {
                BlueprintBuff bp;
                try { bp = RealBlueprints.ByPath<BlueprintBuff>(path); } catch (Exception) { continue; }
                if (bp.ComponentsArray == null) continue;
                foreach (var c in bp.ComponentsArray) { var p = Protects(c); if (p != null) buffHits.Add(path + " :: " + p); }
            }
            m_out.WriteLine("buffs protecting against Staggered: " + buffHits.Count);
            foreach (var h in buffHits) m_out.WriteLine("  " + h);
            var heal = RealBlueprints.ByPath<BlueprintAbility>("Spells/Level6/Heal.jbp");
            m_out.WriteLine("Heal touches: " + string.Join("; ", TouchAbility(heal)));
            var fom = RealBlueprints.ByPath<BlueprintAbility>("Spells/Level4/FreedomOfMovement.jbp");
            m_out.WriteLine("FoM touches: " + string.Join("; ", TouchAbility(fom)));
        }
    }
}
