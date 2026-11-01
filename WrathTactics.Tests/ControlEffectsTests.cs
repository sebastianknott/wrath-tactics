using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using WrathTactics.Engine;
using WrathTactics.Localization;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class ControlEffectsTests {
        [Fact]
        public void Persisted_index_is_pinned() {
            Assert.Equal(36, (int)ConditionProperty.IsVisible);
        }

        [Theory]
        [InlineData(true, 1, true)]    // ToCaster: Dominate Person, Command Undead
        [InlineData(true, 0, false)]   // ToNeutrals: pacify effects
        [InlineData(true, 2, false)]   // ToCustom: polymorph / cutscene
        [InlineData(true, -1, true)]   // field unreadable: err towards "controlled"
        [InlineData(false, 1, false)]  // no ChangeFaction component
        public void Dominating_effect_requires_change_faction_to_caster(bool has, int type, bool expected) {
            Assert.Equal(expected, ControlEffects.IsDominatingEffect(has, type));
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void Charming_effect_is_neutral_to_faction(bool has, bool expected) {
            Assert.Equal(expected, ControlEffects.IsCharmingEffect(has));
        }

        [Fact]
        public void Condition_keys_include_new_members_appended() {
            var keys = EnumLabels.KeysForCondition();
            Assert.Equal("DeathDoor", keys[keys.Count - 3]);
            Assert.Equal("Dominated", keys[keys.Count - 2]);
            Assert.Equal("Charmed", keys[keys.Count - 1]);
        }

        [Theory]
        [InlineData("en_GB")]
        [InlineData("de_DE")]
        [InlineData("fr_FR")]
        [InlineData("ru_RU")]
        [InlineData("zh_CN")]
        public void New_labels_are_localized(string locale) {
            var pack = LoadLocale(locale);
            foreach (var key in new[] { "enum.condition.Dominated", "enum.condition.Charmed", "enum.property.IsVisible" }) {
                Assert.True(pack.TryGetValue(key, out var value), $"{locale} missing {key}");
                Assert.False(string.IsNullOrWhiteSpace(value), $"{locale} empty {key}");
            }
        }

        static Dictionary<string, string> LoadLocale(string locale) {
            var assembly = typeof(ConditionProperty).Assembly;
            using var stream = assembly.GetManifestResourceStream($"WrathTactics.Localization.{locale}.json");
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream);
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(reader.ReadToEnd());
        }
    }
}
