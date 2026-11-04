using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class DebuffSelectionTests {
        static DebuffChoice Choice(float ev) => new DebuffChoice { Ev = ev };

        [Fact]
        public void Take_returns_the_remembered_choice_once() {
            DebuffSelection.Clear();
            DebuffSelection.Remember(null, null, Choice(7f));
            var first = DebuffSelection.Take(null, null);
            Assert.True(first.HasValue);
            Assert.Equal(7f, first.Value.Ev);
            Assert.False(DebuffSelection.Take(null, null).HasValue);
        }

        [Fact]
        public void Take_without_remember_yields_nothing() {
            DebuffSelection.Clear();
            Assert.False(DebuffSelection.Take(null, null).HasValue);
        }

        [Fact]
        public void Clear_drops_the_choice() {
            DebuffSelection.Remember(null, null, Choice(1f));
            DebuffSelection.Clear();
            Assert.False(DebuffSelection.Take(null, null).HasValue);
        }

        [Fact]
        public void Remember_overwrites_an_earlier_choice() {
            DebuffSelection.Remember(null, null, Choice(1f));
            DebuffSelection.Remember(null, null, Choice(2f));
            Assert.Equal(2f, DebuffSelection.Take(null, null).Value.Ev);
        }
    }
}
