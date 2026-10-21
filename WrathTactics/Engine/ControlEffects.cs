using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.FactLogic;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    /// <summary>
    /// Detects mind-control effects (Dominated / Charmed) on a unit. The game has no
    /// UnitCondition for either: both are buffs whose components flip the unit's faction.
    /// IL/blueprint facts (Assembly-CSharp + blueprints.zip, 1.34.0):
    ///   Dominate Person / Command Undead / Vampire Dominate / Witch Animal Servant:
    ///     buff with ChangeFaction, m_Type = ToCaster (ChangeType: ToNeutrals 0, ToCaster 1, ToCustom 2).
    ///   Charm (9dc29118...): buff with NeutralToFaction(Player) — the unit stops being hostile.
    /// ChangeFaction.m_Type and its ChangeType enum are private, hence one cached reflection read.
    /// The party list (Player.PartyAndPets) keeps a dominated companion: ChangeFaction only
    /// switches faction + group (IL OnTurnOn), it never touches party membership.
    /// </summary>
    internal static class ControlEffects {
        internal const int ChangeTypeToCaster = 1;

        static readonly FieldInfo ChangeTypeField =
            typeof(ChangeFaction).GetField("m_Type", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Pure classification of one buff's faction components. A ChangeFaction buff counts as
        /// Dominated only for ToCaster (the unit fights for the caster); ToNeutrals (pacify)
        /// and ToCustom (polymorph/cutscene/rage effects) do not. changeType is -1 when the
        /// private field could not be read: then any ChangeFaction counts, as the safer
        /// reading of "someone else controls this unit".
        /// </summary>
        internal static bool IsDominatingEffect(bool hasChangeFaction, int changeType) {
            if (!hasChangeFaction) return false;
            return changeType < 0 || changeType == ChangeTypeToCaster;
        }

        internal static bool IsCharmingEffect(bool hasNeutralToFaction) {
            return hasNeutralToFaction;
        }

        internal static bool IsDominated(UnitEntityData unit) {
            if (unit?.Buffs == null) return false;
            foreach (var buff in unit.Buffs.RawFacts) {
                var cf = buff.Blueprint?.GetComponent<ChangeFaction>();
                if (cf == null) continue;
                int type = ChangeTypeField != null ? (int)ChangeTypeField.GetValue(cf) : -1;
                if (!IsDominatingEffect(true, type)) continue;
                Log.Engine.Trace($"ControlEffects: {unit.CharacterName} dominated by '{buff.Blueprint.name}' (ChangeFaction type={type})");
                return true;
            }
            return false;
        }

        internal static bool IsCharmed(UnitEntityData unit) {
            if (unit?.Buffs == null) return false;
            foreach (var buff in unit.Buffs.RawFacts) {
                if (!IsCharmingEffect(buff.Blueprint?.GetComponent<NeutralToFaction>() != null)) continue;
                Log.Engine.Trace($"ControlEffects: {unit.CharacterName} charmed by '{buff.Blueprint.name}' (NeutralToFaction)");
                return true;
            }
            return false;
        }
    }
}
