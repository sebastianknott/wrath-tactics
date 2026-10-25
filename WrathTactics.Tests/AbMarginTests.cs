using System.Collections.Generic;
using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class AbMarginTests {
        [Fact]
        public void best_ab_picks_the_highest_value() {
            Assert.Equal(9, AbMargin.BestAb(new[] { 3, 9, 7 }));
        }

        [Fact]
        public void best_ab_handles_negative_values() {
            Assert.Equal(-2, AbMargin.BestAb(new[] { -5, -2, -9 }));
        }

        [Fact]
        public void best_ab_ignores_ineligible_sentinel() {
            Assert.Equal(4, AbMargin.BestAb(new[] { AbMargin.NoAb, 4, AbMargin.NoAb }));
        }

        [Fact]
        public void best_ab_of_nothing_usable_is_the_sentinel() {
            Assert.Equal(AbMargin.NoAb, AbMargin.BestAb(new int[0]));
            Assert.Equal(AbMargin.NoAb, AbMargin.BestAb(new[] { AbMargin.NoAb }));
        }

        [Fact]
        public void margin_is_ab_minus_ac() {
            Assert.Equal(-14f, AbMargin.Margin(7, 21));
            Assert.Equal(-18f, AbMargin.Margin(3, 21));
            Assert.Equal(5f, AbMargin.Margin(26, 21));
        }

        [Fact]
        public void margin_of_sentinel_is_nan() {
            Assert.True(float.IsNaN(AbMargin.Margin(AbMargin.NoAb, 21)));
        }

        [Fact]
        public void cache_computes_once_per_pair() {
            var cache = new Dictionary<(string, string), int>();
            int calls = 0;
            int Compute(string a, string e) { calls++; return 5; }

            Assert.Equal(5, AbMargin.GetOrCompute(cache, "nenio", "incubus", Compute));
            Assert.Equal(5, AbMargin.GetOrCompute(cache, "nenio", "incubus", Compute));
            Assert.Equal(1, calls);
        }

        [Fact]
        public void cache_separates_pairs_by_attacker_and_enemy() {
            var cache = new Dictionary<(string, string), int>();
            int Compute(string a, string e) => a.Length * 100 + e.Length;

            Assert.Equal(402, AbMargin.GetOrCompute(cache, "abcd", "xy", Compute));
            Assert.Equal(204, AbMargin.GetOrCompute(cache, "ab", "wxyz", Compute));
            Assert.Equal(402, AbMargin.GetOrCompute(cache, "abcd", "xy", (a, e) => -1));
            Assert.Equal(2, cache.Count);
        }

        [Fact]
        public void cache_also_stores_the_sentinel() {
            var cache = new Dictionary<(string, string), int>();
            int calls = 0;
            int Compute(string a, string e) { calls++; return AbMargin.NoAb; }

            AbMargin.GetOrCompute(cache, "dead", "orc", Compute);
            AbMargin.GetOrCompute(cache, "dead", "orc", Compute);
            Assert.Equal(1, calls);
        }
    }
}
