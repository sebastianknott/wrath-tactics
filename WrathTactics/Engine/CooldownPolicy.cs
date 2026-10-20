namespace WrathTactics.Engine {
    internal static class CooldownPolicy {
        /// <summary>
        /// Pure decision for the "once per combat" rule flag. Blocks only while in combat, when the
        /// flag is set and the rule already fired for this unit at or after the current combat's
        /// start. A firing from before the combat started (previous combat, out of combat) does not
        /// count, so the rule is armed again for every new combat.
        /// </summary>
        public static bool BlockedByOncePerCombat(bool oncePerCombat, bool inCombat, bool hasFired, float lastFiredSec, float combatStartSec) {
            return oncePerCombat && inCombat && hasFired && lastFiredSec >= combatStartSec;
        }
    }
}
