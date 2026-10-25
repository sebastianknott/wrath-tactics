using System;
using System.Collections.Generic;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>One active buff reduced to what the affliction mask needs (WTW-30 cleanse).</summary>
    internal struct BuffReading {
        public AfflictionKinds Kinds;
        /// <summary>Polymorph descriptor: a shape change is never an affliction.</summary>
        public bool Polymorph;
        /// <summary>The buff grants immunity or suppression (Freedom of Movement, Death Ward ...): it carries
        /// descriptor bits for what it prevents, it is not the harm itself.</summary>
        public bool Protective;
        /// <summary>Cast by the afflicted unit itself.</summary>
        public bool SelfCast;
        /// <summary>Cast by a friendly unit (buff from a party member, not an attack).</summary>
        public bool AllySourced;
    }

    /// <summary>
    /// Which harmful afflictions an ally currently carries (WTW-30 cleanse): a pure mask over its buffs and unit
    /// conditions, with the exclusions CleanseTable does not apply (Polymorph, self-cast buffs, ally-sourced
    /// buffs, protective buffs such as Freedom of Movement). Exact descriptor/condition members only.
    /// </summary>
    internal static class CleanseAfflictionReader {
        /// <summary>Pure core: union of the harmful kinds of the counted buffs plus the condition kinds.</summary>
        internal static AfflictionKinds Mask(IEnumerable<BuffReading> buffs, AfflictionKinds conditionKinds) {
            var mask = conditionKinds;
            if (buffs != null)
                foreach (var b in buffs) if (Counts(b)) mask |= b.Kinds;
            return mask;
        }

        internal static bool Counts(BuffReading b) {
            return !b.Polymorph && !b.Protective && !b.SelfCast && !b.AllySourced;
        }

        /// <summary>Mask of a unit right now (None for null).</summary>
        internal static AfflictionKinds Read(UnitEntityData unit) {
            if (unit == null) return AfflictionKinds.None;
            var readings = new List<BuffReading>();
            foreach (var buff in unit.Buffs.RawFacts) {
                var bp = buff.Blueprint;
                if (bp == null) continue;
                var caster = buff.Context?.MaybeCaster;
                readings.Add(new BuffReading {
                    Kinds = CleanseTable.KindsOfBuff(bp),
                    Polymorph = (bp.SpellDescriptor & SpellDescriptor.Polymorph) != 0,
                    Protective = IsProtective(bp),
                    SelfCast = caster != null && caster == unit,
                    AllySourced = caster != null && caster != unit && !caster.IsPlayersEnemy,
                });
            }
            var conditions = AfflictionKinds.None;
            foreach (UnitCondition c in Enum.GetValues(typeof(UnitCondition))) {
                var k = CleanseTable.KindsFromCondition(c);
                if (k != AfflictionKinds.None && unit.State.HasCondition(c)) conditions |= k;
            }
            return Mask(readings, conditions);
        }

        static bool IsProtective(BlueprintBuff bp) {
            if (bp.ComponentsArray == null) return false;
            foreach (var comp in bp.ComponentsArray)
                if (comp is AddConditionImmunity || comp is BuffDescriptorImmunity || comp is SuppressBuffs) return true;
            return false;
        }

        /// <summary>
        /// Value of a HasAffliction row -> kinds to test. "Any" (or empty) = every kind; otherwise one
        /// AfflictionKinds member name (exact, case-sensitive). Unknown names test nothing (None).
        /// </summary>
        internal static AfflictionKinds ParseValue(string value) {
            if (string.IsNullOrEmpty(value) || value == "Any") return AllKinds;
            AfflictionKinds k;
            return Enum.TryParse(value, false, out k) && k != AfflictionKinds.None ? k : AfflictionKinds.None;
        }

        internal const AfflictionKinds AllKinds = (AfflictionKinds)((1 << 14) - 1);

        /// <summary>
        /// Condition semantics: Equal = the ally carries at least one of the chosen kinds, NotEqual = none of them
        /// (same shape as HasDescriptorEffect / HasCondition). An unparsable value never matches under Equal.
        /// </summary>
        internal static bool Matches(AfflictionKinds mask, string value, ConditionOperator op) {
            var want = ParseValue(value);
            bool has = (mask & want) != 0;
            return op == ConditionOperator.NotEqual ? !has : has;
        }
    }
}
