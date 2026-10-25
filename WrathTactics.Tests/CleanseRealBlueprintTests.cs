using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;
using Xunit.Abstractions;

namespace WrathTactics.Tests {
    // WTW-30 cleanse against the game's REAL blueprints (<GameInstall>/blueprints.zip, see RealBlueprints):
    // real BlueprintAbility / BlueprintBuff objects, deserialized by the game's own JSON system, fed to the
    // unchanged CleanseClassifier / CleanseTable / CleanseAfflictionReader. Skipped when the zip is absent.
    public class CleanseRealBlueprintTests {
        readonly ITestOutputHelper m_out;
        public CleanseRealBlueprintTests(ITestOutputHelper output) { m_out = output; }

        static BlueprintAbility Ability(string path, string guid) {
            var bp = RealBlueprints.ByPath<BlueprintAbility>(path + ".jbp");
            Assert.Equal(guid, bp.AssetGuid.ToString());
            return bp;
        }

        static CleanseProfile Profile(string path, string guid) { return CleanseClassifier.Get(Ability(path, guid)); }

        static AfflictionKinds BuffKinds(string path) { return CleanseTable.KindsOfBuff(RealBlueprints.ByPath<BlueprintBuff>(path + ".jbp")); }

        /// <summary>Union of the kinds of all effects, ignoring gates (a caster-independent view for reports).</summary>
        static AfflictionKinds EveryKind(CleanseProfile p) {
            var k = AfflictionKinds.None;
            foreach (var e in p.Effects) k |= e.Kinds;
            return k;
        }

        // ---- spells ----

        [RealBlueprintFact]
        public void Heal_is_the_tier2_last_resort_covering_many_kinds() {
            var p = Profile("Spells/Level6/Heal", "ff8f1534f66559c478448723e16b6624");
            Assert.True(p.HealsHp);
            Assert.Equal(CleanseRanking.TierHeal, p.Tier);
            var all = p.Covers(null);
            foreach (var k in new[] { AfflictionKinds.Blindness, AfflictionKinds.Confusion, AfflictionKinds.Disease,
                AfflictionKinds.Poison, AfflictionKinds.StunDaze, AfflictionKinds.NauseaSickened,
                AfflictionKinds.FatigueExhausted, AfflictionKinds.StatDebuffAbilityDamage })
                Assert.True((all & k) != 0, "Heal should cover " + k);
            Assert.Equal(0, (int)(all & AfflictionKinds.Curse));
            Assert.All(p.Effects, e => Assert.False(e.Suppress));
        }

        [RealBlueprintTheory]
        [InlineData("Spells/Level3/RemoveCurse", "b48674cef2bff5e478a007cf57d8345b", AfflictionKinds.Curse)]
        [InlineData("Spells/Level3/RemoveDisease", "4093d5a0eb5cae94e909eb1e0e1a6b36", AfflictionKinds.Disease)]
        [InlineData("Spells/Level3/NeutralizePoison", "e7240516af4241b42b2cd819929ea9da", AfflictionKinds.Poison)]
        public void Dice_remedies_remove_one_kind_by_DC_check(string path, string guid, AfflictionKinds kind) {
            var p = Profile(path, guid);
            Assert.Equal(CleanseRanking.TierNormal, p.Tier);
            Assert.False(p.HealsHp);
            Assert.Equal(kind, p.Covers(null));
            Assert.Equal(kind, p.Removes(null));
            Assert.All(p.Effects, e => Assert.Equal(CleanseCheckType.DC, e.Check));
        }

        [RealBlueprintFact]
        public void Restoration_family_removes_without_a_check() {
            var greater = Profile("Spells/Level7/RestorationGreater", "fafd77c6bfa85c04ba31fdc1c962c914");
            var normal = Profile("Spells/Level4/Restoration", "f2115ac1148256b4ba20788f7e966830");
            var lesser = Profile("Spells/Level2/RestorationLesser", "e84fc922ccf952943b5240293669b171");
            foreach (var p in new[] { greater, normal, lesser }) {
                Assert.Equal(CleanseRanking.TierNormal, p.Tier);
                Assert.All(p.Effects, e => Assert.Equal(CleanseCheckType.None, e.Check));
                Assert.True((p.Covers(null) & AfflictionKinds.StatDebuffAbilityDamage) != 0);
                Assert.True((p.Covers(null) & AfflictionKinds.FatigueExhausted) != 0);
            }
            foreach (var p in new[] { greater, normal })
                Assert.True((p.Covers(null) & AfflictionKinds.NegativeLevel) != 0);
            Assert.True((greater.Covers(null) & AfflictionKinds.Confusion) != 0);
            // Lesser Restoration restores ability damage and fatigue, nothing else.
            Assert.Equal(AfflictionKinds.StatDebuffAbilityDamage | AfflictionKinds.FatigueExhausted, lesser.Covers(null));
            Assert.True(CleanseTable.Count(greater.Covers(null)) > CleanseTable.Count(normal.Covers(null)));
            Assert.True(CleanseTable.Count(normal.Covers(null)) > CleanseTable.Count(lesser.Covers(null)));
        }

        [RealBlueprintFact]
        public void Remove_paralysis_is_a_sure_single_kind_remedy() {
            var p = Profile("Spells/Level2/RemoveParalysis", "f8bce986adfc88544a42bf4ab7ae75b2");
            Assert.Equal(AfflictionKinds.Paralysis, p.Covers(null));
            Assert.Equal(CleanseRanking.TierNormal, p.Tier);
            var e = p.EffectFor(AfflictionKinds.Paralysis, null);
            Assert.Equal(CleanseCheckType.None, e.Check);
            Assert.False(e.Suppress);
        }

        [RealBlueprintTheory]
        [InlineData("Spells/Level1/RemoveFear", "55a037e514c0ee14a8e3ed14b47061de", AfflictionKinds.FearShaken)]
        [InlineData("Spells/Level2/DelayPoison", "b48b4c5ffb4eab0469feba27fc86a023", AfflictionKinds.Poison)]
        public void Suppressors_are_tier1_and_remove_nothing(string path, string guid, AfflictionKinds kind) {
            var p = Profile(path, guid);
            Assert.Equal(CleanseRanking.TierSuppressor, p.Tier);
            Assert.Equal(kind, p.Covers(null));
            Assert.Equal(AfflictionKinds.None, p.Removes(null));
            Assert.All(p.Effects, e => Assert.True(e.Suppress));
        }

        [RealBlueprintFact]
        public void Break_enchantment_uses_the_caster_level_check() {
            var p = Profile("Spells/Level5/BreakEnchantment", "7792da00c85b9e042a0fdfc2b66ec9a8");
            Assert.Equal(CleanseRanking.TierNormal, p.Tier);
            Assert.True((p.Covers(null) & AfflictionKinds.Curse) != 0);
            Assert.All(p.Effects, e => Assert.Equal(CleanseCheckType.CasterLevel, e.Check));
        }

        // Harm and Channel Negative Energy heal only undead (the NegativeEnergyAffinity branch) and hurt everyone else;
        // the branch a living ally takes is the one the classifier must read.
        [RealBlueprintTheory]
        [InlineData("Spells/Level6/Harm")]
        [InlineData("Spells/Level6/HarmCast")]
        public void Harm_cures_nobody_a_living_ally_could_be(string path) {
            var bp = RealBlueprints.ByPath<BlueprintAbility>(path + ".jbp");
            Assert.False(CleanseClassifier.ReadUncached(bp).IsCleanse);
        }

        [RealBlueprintTheory]
        [InlineData("Spells/Level6/HealCast")]
        [InlineData("Spells/Level9/HealMass")]
        public void Other_heal_family_spells_stay_tier2(string path) {
            var bp = RealBlueprints.ByPath<BlueprintAbility>(path + ".jbp");
            var p = CleanseClassifier.ReadUncached(bp);
            Assert.Equal(CleanseRanking.TierHeal, p.Tier);
            Assert.True((p.Covers(null) & AfflictionKinds.Poison) != 0);
        }

        // ---- Lay on Hands / Mercy ----

        static readonly Dictionary<string, AfflictionKinds> MercyKinds = new Dictionary<string, AfflictionKinds> {
            { "MercyFatigued", AfflictionKinds.FatigueExhausted }, { "MercyExhausted", AfflictionKinds.FatigueExhausted },
            { "MercyDazed", AfflictionKinds.StunDaze }, { "MercyStaggered", AfflictionKinds.StunDaze },
            { "MercyStunned", AfflictionKinds.StunDaze },
            { "MercySickened", AfflictionKinds.NauseaSickened }, { "MercyNauseated", AfflictionKinds.NauseaSickened },
            { "MercyShaken", AfflictionKinds.FearShaken }, { "MercyFrightened", AfflictionKinds.FearShaken },
            { "MercyParalyzed", AfflictionKinds.Paralysis }, { "MercyConfused", AfflictionKinds.Confusion },
            { "MercyBlinded", AfflictionKinds.Blindness }, { "MercyDiseased", AfflictionKinds.Disease },
            { "MercyPoisoned", AfflictionKinds.Poison }, { "MercyCursed", AfflictionKinds.Curse },
        };

        [RealBlueprintTheory]
        [InlineData("Classes/Paladin/LayOnHands/LayOnHandsSelf", "8d6073201e5395d458b8251386d72df1")]
        [InlineData("Classes/Paladin/LayOnHands/LayOnHandsOthers", "caae1dc6fcf7b37408686971ee27db13")]
        public void Lay_on_hands_is_a_regular_cleanse_gated_by_the_mercy_features(string path, string guid) {
            var p = Profile(path, guid);
            Assert.False(p.HealsHp);
            Assert.Equal(CleanseRanking.TierNormal, p.Tier);

            // A paladin without any mercy cures nothing: every cleansing branch hangs on a caster fact.
            Assert.Equal(AfflictionKinds.None, p.Covers(null));
            Assert.Equal(AfflictionKinds.None, p.Covers(f => false));
            var ungated = p.Effects.Where(e => e.GateFacts.Length == 0).Select(e => e.Kinds.ToString()).ToArray();
            Assert.True(ungated.Length == 0, "ungated cleanse effects: " + string.Join("; ", ungated));

            foreach (var kv in MercyKinds) {
                var guidOfMercy = RealBlueprints.GuidOf("Classes/Paladin/Mercy/" + kv.Key + ".jbp");
                Assert.NotNull(guidOfMercy);
                Assert.Equal(kv.Value, p.Covers(f => f == guidOfMercy));
            }
        }

        [RealBlueprintFact]
        public void Lay_on_hands_mercy_poisoned_gates_the_DC_poison_cure() {
            var p = Profile("Classes/Paladin/LayOnHands/LayOnHandsOthers", "caae1dc6fcf7b37408686971ee27db13");
            const string mercyPoisoned = "19f936eb38c3d224988e2a02cbe12878";
            var effect = p.Effects.Single(e => e.Kinds == AfflictionKinds.Poison && e.GateFacts.Contains(mercyPoisoned));
            Assert.Equal(CleanseCheckType.DC, effect.Check);
            Assert.Equal(new[] { mercyPoisoned }, effect.GateFacts);
            Assert.Equal("Classes/Paladin/Mercy/MercyPoisoned.jbp", RealBlueprints.PathOf(mercyPoisoned));
        }

        // ---- descriptor ints in the raw JSON map to SpellDescriptor members ----

        [RealBlueprintTheory]
        [InlineData("Spells/Level3/RemoveCurse", SpellDescriptor.Curse)]
        [InlineData("Spells/Level3/RemoveDisease", SpellDescriptor.Disease)]
        [InlineData("Spells/Level3/NeutralizePoison", SpellDescriptor.Poison)]
        public void Raw_descriptor_int_of_the_dispel_is_the_expected_SpellDescriptor(string path, SpellDescriptor expected) {
            var m = Regex.Match(RealBlueprints.Text(path + ".jbp"), "\"Descriptor\": \\{\\s*\"m_IntValue\": (\\d+)");
            Assert.True(m.Success);
            var raw = (SpellDescriptor)long.Parse(m.Groups[1].Value);
            Assert.Equal(expected, raw);
        }

        // ---- afflictions on real buffs ----

        [RealBlueprintTheory]
        [InlineData("Spells/Level3/HoldPersonBuff", AfflictionKinds.Paralysis)]
        [InlineData("Spells/Level5/IcyPrisonParalyzedBuff", AfflictionKinds.Paralysis)]
        [InlineData("Spells/Level3/PoisonBuff", AfflictionKinds.Poison)]
        [InlineData("Spells/Level2/PerniciousPoisonBuff", AfflictionKinds.Poison)]
        [InlineData("Buffs/DiseaseShakesBuff", AfflictionKinds.Disease)]
        [InlineData("Spells/DLC5NewSpells/OracleBurdenLameCurseBuff", AfflictionKinds.Curse)]
        [InlineData("Spells/Level8/FrightfulAspectShakenBuff", AfflictionKinds.FearShaken)]
        [InlineData("Spells/Level6/WordOfChaosConfusionBuff", AfflictionKinds.Confusion)]
        [InlineData("Spells/Level2/BlindnessBuff", AfflictionKinds.Blindness)]
        [InlineData("Buffs/Sickened", AfflictionKinds.NauseaSickened)]
        [InlineData("Buffs/Fatigued", AfflictionKinds.FatigueExhausted)]
        [InlineData("Buffs/Daze", AfflictionKinds.StunDaze)]
        [InlineData("Buffs/Stunned", AfflictionKinds.StunDaze)]
        [InlineData("Root/SystemMechanics/Conditions/PetrifiedBuff", AfflictionKinds.Petrified)]
        [InlineData("Spells/Level1/EntangleBuff", AfflictionKinds.SlowedEntangled)]
        [InlineData("Buffs/TemporaryNegativeLevel", AfflictionKinds.NegativeLevel)]
        public void Real_harmful_buff_carries_its_kind(string path, AfflictionKinds expected) {
            Assert.True((BuffKinds(path) & expected) == expected, path + " -> " + BuffKinds(path));
        }

        [RealBlueprintTheory]
        [InlineData("Spells/Level3/HoldPersonBuff")]
        [InlineData("Spells/Level3/PoisonBuff")]
        [InlineData("Spells/Level2/BlindnessBuff")]
        [InlineData("Spells/Level8/FrightfulAspectShakenBuff")]
        [InlineData("Spells/Level5/IcyPrisonParalyzedBuff")]
        public void Real_harmful_buff_is_not_dropped_as_protective(string path) {
            var bp = RealBlueprints.ByPath<BlueprintBuff>(path + ".jbp");
            Assert.False(CleanseAfflictionReader.IsProtective(bp), path);
            Assert.NotEqual(AfflictionKinds.None, CleanseAfflictionReader.Mask(new[] { Reading(bp) }, AfflictionKinds.None));
        }

        [RealBlueprintTheory]
        [InlineData("Spells/Level4/FreedomOfMovementBuff")]
        [InlineData("Spells/Level2/DelayPoisonBuff")]
        [InlineData("Mythic/Angel/AngelSpells/AngelWardOfDiseaseBuff")]
        public void Real_protective_buff_is_not_an_affliction(string path) {
            var bp = RealBlueprints.ByPath<BlueprintBuff>(path + ".jbp");
            Assert.True(CleanseAfflictionReader.IsProtective(bp), path);
            Assert.Equal(AfflictionKinds.None, CleanseAfflictionReader.Mask(new[] { Reading(bp) }, AfflictionKinds.None));
        }

        [RealBlueprintFact]
        public void Death_ward_buff_is_not_an_affliction() {
            var bp = RealBlueprints.ByPath<BlueprintBuff>("Spells/Level4/DeathWardBuff.jbp");
            Assert.Equal(AfflictionKinds.None, CleanseAfflictionReader.Mask(new[] { Reading(bp) }, AfflictionKinds.None));
        }

        static BuffReading Reading(BlueprintBuff bp) {
            return new BuffReading {
                Kinds = CleanseTable.KindsOfBuff(bp),
                Polymorph = (bp.SpellDescriptor & SpellDescriptor.Polymorph) != 0,
                Protective = CleanseAfflictionReader.IsProtective(bp),
            };
        }

        /// <summary>
        /// The reader drops a buff that carries any immunity/suppression component. Count the buffs with a harmful
        /// descriptor that the reader would drop although the buff is an affliction (its harmful kinds are NOT what
        /// its immunity components protect against). Must be zero.
        /// </summary>
        [RealBlueprintFact]
        public void No_real_affliction_buff_is_dropped_as_protective() {
            int harmful = 0, withProtectiveComponent = 0, dropped = 0, unloadable = 0, mixed = 0;
            var examples = new List<string>();
            foreach (var path in RealBlueprints.PathsOfType("BlueprintBuff")) {
                BlueprintBuff bp;
                try { bp = RealBlueprints.ByPath<BlueprintBuff>(path); } catch (Exception) { unloadable++; continue; }
                var kinds = CleanseTable.KindsOfBuff(bp);
                if (kinds == AfflictionKinds.None) continue;
                harmful++;
                if (CleanseTable.ProtectedKinds(bp) == AfflictionKinds.None
                    && bp.ComponentsArray.Any(c => c is AddConditionImmunity || c is BuffDescriptorImmunity || c is SuppressBuffs)) withProtectiveComponent++;
                var shielded = CleanseTable.ProtectedKinds(bp);
                if (shielded != AfflictionKinds.None && (kinds & ~shielded) != AfflictionKinds.None) mixed++;
                if (CleanseAfflictionReader.IsProtective(bp)) {
                    dropped++;
                    examples.Add(path + " [" + kinds + "]");
                }
            }
            m_out.WriteLine("harmful buffs=" + harmful + ", unloadable=" + unloadable
                + ", with an immunity component that protects no harmful kind=" + withProtectiveComponent
                + ", immune to some kinds but harmful in others (kept as afflictions)=" + mixed
                + ", dropped as protective=" + dropped);
            foreach (var e in examples) m_out.WriteLine("  dropped: " + e);
            Assert.True(harmful > 100, "harmful buffs found: " + harmful);
            // Every dropped buff must actually protect against what it carries (FoM-style descriptor markers).
            foreach (var path in RealBlueprints.PathsOfType("BlueprintBuff")) {
                BlueprintBuff bp;
                try { bp = RealBlueprints.ByPath<BlueprintBuff>(path); } catch (Exception) { continue; }
                if (!CleanseAfflictionReader.IsProtective(bp)) continue;
                var carried = CleanseTable.KindsOfBuff(bp);
                Assert.True((carried & ~CleanseTable.ProtectedKinds(bp)) == AfflictionKinds.None, path);
            }
        }

        // ---- smoke: every ability in the game ----

        [RealBlueprintFact]
        public void Every_ability_classifies_without_exception_and_cures_are_plausible() {
            var tiers = new SortedDictionary<int, int>();
            var kindCounts = new SortedDictionary<string, int>();
            var checks = new SortedDictionary<string, int>();
            var suspicious = new List<string>();
            var unloadable = new List<string>();
            int total = 0, cleanses = 0;
            foreach (var path in RealBlueprints.PathsOfType("BlueprintAbility")) {
                BlueprintAbility bp;
                try { bp = RealBlueprints.ByPath<BlueprintAbility>(path); } catch (Exception) { unloadable.Add(path); continue; }
                total++;
                var p = CleanseClassifier.ReadUncached(bp);
                var again = CleanseClassifier.Get(bp);
                Assert.Equal(p.IsCleanse, again.IsCleanse);
                if (!p.IsCleanse) continue;
                cleanses++;
                int t = p.Tier;
                tiers[t] = tiers.ContainsKey(t) ? tiers[t] + 1 : 1;
                foreach (var bit in CleanseTable.Bits(EveryKind(p))) {
                    var k = bit.ToString();
                    kindCounts[k] = kindCounts.ContainsKey(k) ? kindCounts[k] + 1 : 1;
                }
                foreach (var e in p.Effects) {
                    var c = e.Check.ToString() + (e.Suppress ? "/suppress" : "");
                    checks[c] = checks.ContainsKey(c) ? checks[c] + 1 : 1;
                }
                if (t == CleanseRanking.TierHeal && bp.Type != AbilityType.Spell) suspicious.Add("tier2 non-spell: " + path);
                if (p.HealsHp && !p.Effects.Any()) suspicious.Add("heal without effects: " + path);
                if (DealsDamage(bp)) suspicious.Add("damage + cleanse: " + path + " [" + EveryKind(p) + "]");
            }
            m_out.WriteLine("abilities loaded=" + total + ", unloadable=" + unloadable.Count + ", classified as cleanse=" + cleanses);
            m_out.WriteLine("tier 0/1/2: " + string.Join(", ", tiers.Select(kv => kv.Key + "=" + kv.Value)));
            m_out.WriteLine("kinds covered (abilities): " + string.Join(", ", kindCounts.Select(kv => kv.Key + "=" + kv.Value)));
            m_out.WriteLine("effects by check: " + string.Join(", ", checks.Select(kv => kv.Key + "=" + kv.Value)));
            foreach (var s in suspicious) m_out.WriteLine("  suspicious: " + s);
            foreach (var u in unloadable.Take(20)) m_out.WriteLine("  unloadable: " + u);
            Assert.True(total > 3000, "abilities loaded: " + total);
            Assert.True(cleanses > 20);
            Assert.Empty(suspicious.Where(s => s.StartsWith("tier2 non-spell") || s.StartsWith("heal without")));
        }

        [RealBlueprintFact]
        public void Classification_report_lists_every_cleanse_ability() {
            var lines = new List<string>();
            var unloadable = new List<string>();
            foreach (var path in RealBlueprints.PathsOfType("BlueprintAbility")) {
                BlueprintAbility bp;
                try { bp = RealBlueprints.ByPath<BlueprintAbility>(path); } catch (Exception) { unloadable.Add(path); continue; }
                var p = CleanseClassifier.ReadUncached(bp);
                if (!p.IsCleanse) continue;
                lines.Add("T" + p.Tier + " " + path + " [" + EveryKind(p) + "] gated=" + p.Effects.Count(e => e.GateFacts.Length + e.ForbiddenFacts.Length > 0) + "/" + p.Effects.Count);
            }
            foreach (var l in lines) m_out.WriteLine(l);
            // Abilities the loader cannot build (Unity-native AnimationCurve in polymorph components) must not
            // be cure-like: none of them may carry a cleansing name.
            var cureWords = new Regex("Heal|Cure|Remove|Restor|Mercy|Dispel|Neutraliz|Panacea|Cleanse|LayOnHands|Antidote|Break", RegexOptions.IgnoreCase);
            var hidden = unloadable.Where(u => {
                var name = System.IO.Path.GetFileNameWithoutExtension(u);
                return cureWords.IsMatch(name) && !name.Contains("Polymorf") && !name.Contains("Polymorph");
            }).ToList();
            foreach (var u in hidden) m_out.WriteLine("UNLOADABLE cure-like: " + u);
            Assert.Empty(hidden);
        }

        static bool DealsDamage(BlueprintAbility bp) {
            var run = bp.ComponentsArray.OfType<Kingmaker.UnitLogic.Abilities.Components.AbilityEffectRunAction>().FirstOrDefault();
            return run != null && ContainsDamage(run.Actions);
        }

        static bool ContainsDamage(Kingmaker.ElementsSystem.ActionList list) {
            if (list?.Actions == null) return false;
            foreach (var a in list.Actions) {
                if (a is Kingmaker.UnitLogic.Mechanics.Actions.ContextActionDealDamage) return true;
                var c = a as Kingmaker.Designers.EventConditionActionSystem.Actions.Conditional;
                if (c != null && (ContainsDamage(c.IfTrue) || ContainsDamage(c.IfFalse))) return true;
                var st = a as Kingmaker.UnitLogic.Mechanics.Actions.ContextActionSavingThrow;
                if (st != null && ContainsDamage(st.Actions)) return true;
                var cs = a as Kingmaker.UnitLogic.Mechanics.Actions.ContextActionConditionalSaved;
                if (cs != null && (ContainsDamage(cs.Failed) || ContainsDamage(cs.Succeed))) return true;
            }
            return false;
        }
    }
}
