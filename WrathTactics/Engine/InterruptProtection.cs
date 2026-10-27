using Kingmaker.UnitLogic.Commands.Base;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// Pure core of the per-rule "Uninterruptible" flag (TacticsRule.Uninterruptible). While
    /// the unit's tracked command was issued by a flagged rule, no other rule may replace it:
    /// the ActiveRuleTracker gate closes for the whole Standard slot (not just rules below),
    /// and the two remaining ways to destroy a Standard command are shut explicitly — a Move
    /// command (paired slot) and a toggle (starts a Standard command). Swift and Free stay
    /// open: Run(Swift) never removes a Standard command, and ActionSlots.CheckConflict
    /// already makes a Swift wait for a pending or approaching Standard command.
    /// </summary>
    internal static class InterruptProtection {
        /// <summary>
        /// Upper bound for the protection, counted from the moment the command was issued.
        /// Stops a command that never starts (walks forever, target unreachable) from locking
        /// the unit out for the rest of the combat. Three rounds covers approach + wind-up.
        /// </summary>
        internal const float MaxProtectedSeconds = 18f;

        internal static bool IsActive(bool ruleFlag, float recordedAtSec, float nowSec) {
            return ruleFlag && nowSec - recordedAtSec <= MaxProtectedSeconds;
        }

        /// <summary>
        /// Rule kinds that would replace the protected Standard command without going
        /// through the Standard priority gate: Move commands and toggles.
        /// </summary>
        internal static bool Blocks(ActionType type, UnitCommand.CommandType? slot) {
            return type == ActionType.ToggleActivatable || slot == UnitCommand.CommandType.Move;
        }
    }
}
