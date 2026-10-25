using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class DebuffAllySafetyTests {
        [Fact]
        public void Single_target_enemy_spell_is_safe() {
            Assert.True(DebuffMath.IsAllySafe(true, false, false, DebuffAreaKind.Single, false, false));
        }

        [Fact]
        public void Spell_without_enemy_targeting_is_unsafe() {
            Assert.False(DebuffMath.IsAllySafe(false, false, false, DebuffAreaKind.Single, false, false));
        }

        [Fact]
        public void Spawned_area_effect_is_unsafe() {
            Assert.False(DebuffMath.IsAllySafe(true, true, false, DebuffAreaKind.Single, false, false));
        }

        [Fact]
        public void Cone_or_line_is_unsafe_even_with_enemy_filter() {
            Assert.False(DebuffMath.IsAllySafe(true, false, false, DebuffAreaKind.AroundEnemy, true, true));
        }

        [Fact]
        public void Point_targetable_single_spell_is_unsafe() {
            Assert.False(DebuffMath.IsAllySafe(true, false, true, DebuffAreaKind.Single, false, false));
        }

        [Fact]
        public void Area_is_safe_only_when_filtered_to_enemies() {
            Assert.True(DebuffMath.IsAllySafe(true, false, false, DebuffAreaKind.AroundEnemy, true, false));
            Assert.False(DebuffMath.IsAllySafe(true, false, false, DebuffAreaKind.AroundEnemy, false, false));
            Assert.False(DebuffMath.IsAllySafe(true, false, false, DebuffAreaKind.ChainEnemy, false, false));
        }
    }
}
