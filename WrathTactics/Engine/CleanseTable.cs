using System.Collections.Generic;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.UnitLogic.FactLogic;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// Hand table (WTW-30 cleanse): which game spell descriptors and unit conditions make up each
    /// AfflictionKinds bit. Exact enum members only, never name matching. Polymorph, Charm, Sleep and the
    /// other descriptors that are not in the table map to None, so a buff carrying only those is not harmful.
    /// Staggered is its own kind (WTW-30 F1): the stun/daze removers (Heal) do not lift a stagger, so folding it into
    /// StunDaze made them count as remedies that never worked.
    /// </summary>
    internal static class CleanseTable {
        internal static AfflictionKinds KindsFromDescriptor(SpellDescriptor d) {
            var k = AfflictionKinds.None;
            if ((d & SpellDescriptor.Paralysis) != 0) k |= AfflictionKinds.Paralysis;
            if ((d & SpellDescriptor.Confusion) != 0) k |= AfflictionKinds.Confusion;
            if ((d & (SpellDescriptor.Stun | SpellDescriptor.Daze)) != 0) k |= AfflictionKinds.StunDaze;
            if ((d & SpellDescriptor.Staggered) != 0) k |= AfflictionKinds.Staggered;
            if ((d & (SpellDescriptor.Fear | SpellDescriptor.Shaken | SpellDescriptor.Frightened)) != 0) k |= AfflictionKinds.FearShaken;
            if ((d & (SpellDescriptor.Nauseated | SpellDescriptor.Sickened)) != 0) k |= AfflictionKinds.NauseaSickened;
            if ((d & SpellDescriptor.Blindness) != 0) k |= AfflictionKinds.Blindness;
            if ((d & SpellDescriptor.Poison) != 0) k |= AfflictionKinds.Poison;
            if ((d & SpellDescriptor.Disease) != 0) k |= AfflictionKinds.Disease;
            if ((d & SpellDescriptor.Curse) != 0) k |= AfflictionKinds.Curse;
            if ((d & (SpellDescriptor.Fatigue | SpellDescriptor.Exhausted)) != 0) k |= AfflictionKinds.FatigueExhausted;
            if ((d & SpellDescriptor.StatDebuff) != 0) k |= AfflictionKinds.StatDebuffAbilityDamage;
            if ((d & SpellDescriptor.NegativeLevel) != 0) k |= AfflictionKinds.NegativeLevel;
            if ((d & SpellDescriptor.Petrified) != 0) k |= AfflictionKinds.Petrified;
            if ((d & SpellDescriptor.MovementImpairing) != 0) k |= AfflictionKinds.SlowedEntangled;
            return k;
        }

        internal static AfflictionKinds KindsFromCondition(UnitCondition c) {
            switch (c) {
                case UnitCondition.Paralyzed: return AfflictionKinds.Paralysis;
                case UnitCondition.Confusion: return AfflictionKinds.Confusion;
                case UnitCondition.Stunned:
                case UnitCondition.Dazed: return AfflictionKinds.StunDaze;
                case UnitCondition.Staggered: return AfflictionKinds.Staggered;
                case UnitCondition.Shaken:
                case UnitCondition.Frightened:
                case UnitCondition.Cowering: return AfflictionKinds.FearShaken;
                case UnitCondition.Nauseated:
                case UnitCondition.Sickened: return AfflictionKinds.NauseaSickened;
                case UnitCondition.Blindness: return AfflictionKinds.Blindness;
                case UnitCondition.Fatigued:
                case UnitCondition.Exhausted: return AfflictionKinds.FatigueExhausted;
                case UnitCondition.Petrified: return AfflictionKinds.Petrified;
                case UnitCondition.Slowed:
                case UnitCondition.Entangled: return AfflictionKinds.SlowedEntangled;
                default: return AfflictionKinds.None;
            }
        }

        /// <summary>Kinds a buff blueprint stands for: its descriptor bits plus the conditions it applies.</summary>
        internal static AfflictionKinds KindsOfBuff(BlueprintBuff buff) {
            if (buff == null) return AfflictionKinds.None;
            var k = KindsFromDescriptor(buff.SpellDescriptor);
            if (buff.ComponentsArray == null) return k;
            foreach (var comp in buff.ComponentsArray) {
                switch (comp) {
                    case AddCondition ac: k |= KindsFromCondition(ac.Condition); break;
                    case BuffStatusCondition bsc: k |= KindsFromCondition(bsc.Condition); break;
                }
            }
            return k;
        }

        /// <summary>
        /// Kinds a buff component protects against: SuppressBuffs (descriptor and listed buffs),
        /// BuffDescriptorImmunity, AddConditionImmunity. None for every other component.
        /// </summary>
        internal static AfflictionKinds ProtectedKinds(BlueprintComponent comp) {
            switch (comp) {
                case SuppressBuffs sup: {
                    var k = KindsFromDescriptor((SpellDescriptor)sup.Descriptor);
                    if (sup.m_Buffs != null)
                        foreach (var r in sup.m_Buffs) k |= KindsOfBuff(r?.Get());
                    return k;
                }
                case BuffDescriptorImmunity imm: return KindsFromDescriptor((SpellDescriptor)imm.Descriptor);
                case AddConditionImmunity cond: return KindsFromCondition(cond.Condition);
                default: return AfflictionKinds.None;
            }
        }

        /// <summary>Union of what all protective components of a buff protect against.</summary>
        internal static AfflictionKinds ProtectedKinds(BlueprintBuff buff) {
            var k = AfflictionKinds.None;
            if (buff?.ComponentsArray == null) return k;
            foreach (var comp in buff.ComponentsArray) k |= ProtectedKinds(comp);
            return k;
        }

        /// <summary>The single bits set in a mask, in enum order.</summary>
        internal static IEnumerable<AfflictionKinds> Bits(AfflictionKinds kinds) {
            for (int i = 0; i < 31; i++) {
                var bit = (AfflictionKinds)(1 << i);
                if ((kinds & bit) != 0) yield return bit;
            }
        }

        internal static int Count(AfflictionKinds kinds) {
            int n = 0;
            foreach (var _ in Bits(kinds)) n++;
            return n;
        }
    }
}
