using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static partial class ActionValidator {
        /// <summary>
        /// CastConditionOption: casts the option the GroupOption condition rows latched for the matched enemy
        /// (WTW-30 phase b). The rule must target that very enemy (Target: condition target); a mismatch or a
        /// rule without a GroupOption row has nothing to cast. The latched option is re-checked cheaply because
        /// slots and charges move between the condition pass and the execution.
        /// </summary>
        internal static bool CanExecuteConditionOption(UnitEntityData owner, UnitEntityData target, out DebuffChoice choice) {
            choice = default;
            var latched = ConditionEvaluator.LastMatchedChoice;
            if (!latched.HasValue) {
                Log.Engine.Trace($"CastConditionOption: no GroupOption latch for {owner.CharacterName} (rule has no GroupOption row or nothing matched)");
                return false;
            }
            if (target == null || !ReferenceEquals(target, ConditionEvaluator.LastMatchedEnemy)) {
                Log.Engine.Trace($"CastConditionOption: target {target?.CharacterName ?? "none"} is not the matched enemy {ConditionEvaluator.LastMatchedEnemy?.CharacterName ?? "none"}");
                return false;
            }
            var option = latched.Value.Option;
            if (!StillCastable(option, target)) return false;
            choice = latched.Value;
            return true;
        }

        static bool StillCastable(DebuffOption option, UnitEntityData target) {
            if (target.Descriptor?.State?.IsFinallyDead ?? false) {
                Log.Engine.Trace($"CastConditionOption: {target.CharacterName} is dead");
                return false;
            }
            var ability = option.Ability;
            if (option.InventorySource != null && option.InventorySource.Count <= 0) {
                Log.Engine.Trace($"CastConditionOption: '{option.Name}' [{option.Kind}] gone from the inventory");
                return false;
            }
            if (!ability.IsAvailable) {
                Log.Engine.Trace($"CastConditionOption: '{option.Name}' [{option.Kind}] no longer available ({ability.GetUnavailableReason()})");
                return false;
            }
            if (!ability.CanTargetPoint && !ability.CanTarget(new TargetWrapper(target))) {
                Log.Engine.Trace($"CastConditionOption: '{option.Name}' [{option.Kind}] cannot target {target.CharacterName} (engine CanTarget)");
                return false;
            }
            return true;
        }
    }
}
