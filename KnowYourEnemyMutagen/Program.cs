using System;
using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Newtonsoft.Json.Linq;
using Alphaleonis.Win32.Filesystem;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Newtonsoft.Json;
using Noggog;

namespace KnowYourEnemyMutagen
{
    public static class Program
    {
        static ModKey KnowYourEnemy = ModKey.FromNameAndExtension("know_your_enemy.esp");

        public static int Main(string[] args)
        {
            return SynthesisPipeline.Instance.Patch<ISkyrimMod, ISkyrimModGetter>(
                args,
                RunPatch,
                new UserPreferences
                {
                    ActionsForEmptyArgs = new RunDefaultPatcher
                    {
                        IdentifyingModKey = "know_your_enemy_patcher.esp",
                        //BlockAutomaticExit = true,
                        TargetRelease = GameRelease.SkyrimSE
                    }
                });
        }

        private static float AdjustDamageMod(float magnitude, float scale)
        {
            if (magnitude.EqualsWithin(0))
                return magnitude;
            if (magnitude > 1)
                return (magnitude - 1) * scale + 1;
            return 1 / AdjustDamageMod(1 / magnitude, scale);
        }

        private static float AdjustMagicResist(float magnitude, float scale)
        {
            return magnitude == 0 ? magnitude : magnitude * scale;
        }

        private static readonly (string Keywords, uint Id)[] PerkArray = {
            ("fat", 0x00AA5E),
            ("big", 0x00AA60),
            ("small", 0x00AA61),
            ("armored", 0x00AA62),
            ("undead", 0x00AA63),
            ("plant", 0x00AA64),
            ("skeletal", 0x00AA65),
            ("brittle", 0x00AA66),
            ("dwarven machine", 0x00AA67),
            ("ghostly", 0x02E171),
            ("furred", 0x047680),
            ("supernatural", 0x047681),
            ("venomous", 0x047682),
            ("ice elemental", 0x047683),
            ("fire elemental", 0x047684),
            ("shock elemental", 0x047685),
            ("vile", 0x047686),
            ("troll kin", 0x047687),
            ("weak willed", 0x047688),
            ("strong willed", 0x047689),
            ("cave dwelling", 0x04768A),
            ("vascular", 0x04768B),
            ("aquatic", 0x04768C),
            ("rocky", 0x04C78E)
        };

        private static IEnumerable<string> GetFromJson(string key, JObject jObject)
        {
            return jObject.ContainsKey(key) ? jObject[key]!.Select(x => (string?)x).Where(x => x != null).Select(x => x!).ToList() : new List<string>();
        }

        private static void RunPatch(SynthesisState<ISkyrimMod, ISkyrimModGetter> state)
        {
            if (!state.LoadOrder.ContainsKey(KnowYourEnemy))
                throw new Exception("ERROR: Know Your Enemy not detected in load order. You need to install KYE prior to running this patcher!");

            string[] requiredFiles = { "creature_rules.json", "misc.json", "settings.json" };
            foreach (string file in requiredFiles)
            {
                if (!File.Exists(file)) throw new Exception("Required file " + file + " does not exist! Make sure to copy all files over when installing the patcher, and don't run it from within an archive.");
            }

            // Retrieve all the perks that are going to be applied to NPCs in part 5
            Dictionary<string, FormKey> perks = PerkArray
                .Select(tuple =>
                {
                    var (key, id) = tuple;
                    if (state.LinkCache.TryLookup<IPerkGetter>(KnowYourEnemy.MakeFormKey(id), out var perk))
                    {
                        return (key, perk: perk.FormKey);
                    }
                    else
                    {
                        throw new Exception("Failed to find perk with key: " + key + " and id " + id);
                    }
                })
                .ToDictionary(x => x.key, x => x.perk, StringComparer.OrdinalIgnoreCase);

            // Reading JSON and converting it to a normal list because .Contains() is weird in Newtonsoft.JSON
            JObject misc = JObject.Parse(File.ReadAllText("misc.json"));
            JObject settings = JObject.Parse(File.ReadAllText("settings.json"));
            var effectIntensity = (float)settings["effect_intensity"]!;
            var patchSilverPerk = (bool)settings["patch_silver_perk"]!;
            Console.WriteLine("*** DETECTED SETTINGS ***");
            Console.WriteLine("patch_silver_perk: " + patchSilverPerk);
            Console.WriteLine("effect_intensity: " + effectIntensity);
            Console.WriteLine("*************************");

            List<string> resistancesAndWeaknesses = GetFromJson("resistances_and_weaknesses", misc).ToList();
            List<string> abilitiesToClean = GetFromJson("abilities_to_clean", misc).ToList();
            List<string> perksToClean = GetFromJson("perks_to_clean", misc).ToList();
            List<string> kyePerkNames = GetFromJson("kye_perk_names", misc).ToList();
            List<string> kyeAbilityNames = GetFromJson("kye_ability_names", misc).ToList();

            Dictionary<string, string[]> creatureRules = JsonConvert.DeserializeObject<Dictionary<string, string[]>>(File.ReadAllText("creature_rules.json"));

            // Part 1a
            // Removing other magical resistance/weakness systems
            foreach (var spell in state.LoadOrder.PriorityOrder.WinningOverrides<ISpellGetter>())
            {
                if (spell.EditorID == null || !abilitiesToClean.Contains(spell.EditorID)) continue;
                var modifiedSpell = state.PatchMod.Spells.GetOrAddAsOverride(spell);
                foreach (var effect in modifiedSpell.Effects)
                {
                    effect.BaseEffect.TryResolve(state.LinkCache, out var baseEffect);
                    if (baseEffect?.EditorID == null) continue;
                    if (!resistancesAndWeaknesses.Contains(baseEffect.EditorID)) continue;
                    if (effect.Data != null)
                        effect.Data.Magnitude = 0;
                    else
                        Console.WriteLine("Error setting Effect Magnitude - DATA was null!");
                }
            }

            // Part 1b
            // Remove other weapon resistance systems
            foreach (var perk in state.LoadOrder.PriorityOrder.WinningOverrides<IPerkGetter>())
            {
                if (perk.EditorID == null || !perksToClean.Contains(perk.EditorID)) continue;
                foreach (var eff in perk.Effects)
                {
                    if (!(eff is PerkEntryPointModifyValue modValue)) continue;
                    if (modValue.EntryPoint != APerkEntryPointEffect.EntryType.ModIncomingDamage) continue;
                    modValue.Value = 1f;
                    modValue.Modification = PerkEntryPointModifyValue.ModificationType.Set;
                }
            }

            // Part 2a
            // Adjust KYE's physical effects according to effect_intensity
            if (!effectIntensity.EqualsWithin(1))
            {
                foreach (var perk in state.LoadOrder.PriorityOrder.WinningOverrides<IPerkGetter>())
                {
                    if (perk.EditorID == null || !kyePerkNames.Contains(perk.EditorID) || !perk.Effects.Any()) continue;
                    foreach (var eff in perk.Effects)
                    {
                        if (!(eff is PerkEntryPointModifyValue modValue)) continue;
                        if (modValue.EntryPoint != APerkEntryPointEffect.EntryType.ModIncomingDamage) continue;
                        var currentMagnitude = modValue.Value;
                        modValue.Value = AdjustDamageMod(currentMagnitude, effectIntensity);
                        modValue.Modification = PerkEntryPointModifyValue.ModificationType.Set;
                    }
                }

                // Part 2b
                // Adjust KYE's magical effects according to effect_intensity

                foreach (var spell in state.LoadOrder.PriorityOrder.WinningOverrides<ISpellGetter>())
                {
                    if (spell.EditorID == null || !kyeAbilityNames.Contains(spell.EditorID)) continue;
                    Spell s = spell.DeepCopy();
                    foreach (var eff in s.Effects)
                    {
                        eff.BaseEffect.TryResolve(state.LinkCache, out var baseEffect);
                        if (baseEffect?.EditorID == null
                            || !resistancesAndWeaknesses.Contains(baseEffect.EditorID)
                            || eff.Data == null) continue;
                        var currentMagnitude = eff.Data.Magnitude;
                        eff.Data.Magnitude = AdjustMagicResist(currentMagnitude, effectIntensity);
                        state.PatchMod.Spells.Set(s);
                    }
                }
            }

            // Part 3
            // Edit the effect of silver weapons

            if (patchSilverPerk)
            {
                if (state.LoadOrder.ContainsKey(ModKey.FromNameAndExtension("Skyrim Immersive Creatures.esp")))
                    Console.WriteLine("WARNING: Silver Perk is being patched, but Skyrim Immersive Creatures has been detected in your load order. Know Your Enemy's silver weapon effects will NOT work against new races added by SIC.");

                FormKey silverKey = Skyrim.Perk.SilverPerk;
                FormKey dummySilverKey = KnowYourEnemy.MakeFormKey(0x0BBE10);
                if (state.LinkCache.TryLookup<IPerkGetter>(silverKey, out var silverPerk))
                {
                    if (state.LinkCache.TryLookup<IPerkGetter>(dummySilverKey, out var dummySilverPerk))
                    {
                        Perk kyePerk = silverPerk.DeepCopy();
                        kyePerk.Effects.Clear();
                        foreach (var aPerkEffectGetter in dummySilverPerk.Effects)
                        {
                            var eff = (APerkEffect)aPerkEffectGetter;
                            kyePerk.Effects.Add(eff);
                        }

                        state.PatchMod.Perks.GetOrAddAsOverride(kyePerk);
                    }
                }
            }

            // Part 4
            // Adjust traits to accommodate CACO if present
            if (state.LoadOrder.ContainsKey(ModKey.FromNameAndExtension("Complete Alchemy & Cooking Overhaul.esp")))
            {
                Console.WriteLine("CACO detected! Adjusting kye_ab_undead and kye_ab_ghostly spells.");
                var kyeAbGhostlyKey = KnowYourEnemy.MakeFormKey(0x060B93);
                var kyeAbUndeadKey = KnowYourEnemy.MakeFormKey(0x00AA43);
                if (state.LinkCache.TryLookup<ISpellGetter>(kyeAbGhostlyKey, out var kyeAbGhostly))
                {
                    Spell kyeAbGhostlyCaco = kyeAbGhostly.DeepCopy();
                    foreach (var eff in kyeAbGhostlyCaco.Effects)
                    {
                        if (eff.Data == null) continue;
                        if (!eff.BaseEffect.TryResolve(state.LinkCache, out var baseEffect)) continue;
                        if (baseEffect.FormKey != Skyrim.MagicEffect.AbResistPoison) continue;
                        eff.Data.Magnitude = 0;
                        state.PatchMod.Spells.GetOrAddAsOverride(kyeAbGhostlyCaco);
                    }
                }
                else
                {
                    Console.WriteLine($"WARNING! CACO detected but failed to patch kye_ab_ghostly_caco spell. Do you have {KnowYourEnemy} active in the load order?");
                }

                if (state.LinkCache.TryLookup<ISpellGetter>(kyeAbUndeadKey, out var kyeAbUndead))
                {
                    Spell kyeAbUndeadCaco = kyeAbUndead.DeepCopy();
                    foreach (var eff in kyeAbUndeadCaco.Effects)
                    {
                        if (eff.Data == null) continue;
                        if (!eff.BaseEffect.TryResolve(state.LinkCache, out var baseEffect)) continue;
                        if (baseEffect.FormKey != Skyrim.MagicEffect.AbResistPoison) continue;
                        eff.Data.Magnitude = 0;
                        state.PatchMod.Spells.GetOrAddAsOverride(kyeAbUndeadCaco);
                    }
                }
                else
                {
                    Console.WriteLine($"WARNING! CACO detected but failed to patch kye_ab_undead_caco spell. Do you have {KnowYourEnemy} active in the load order?");
                }
            }

            // Part 5
            // Add the traits to NPCs

            foreach (var npc in state.LoadOrder.PriorityOrder.WinningOverrides<INpcGetter>())
            {
                // Skip if npc has spell list
                if (npc.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.SpellList)) continue;

                var traits = new List<string>();

                // If ghost
                if (npc.Keywords?.Contains(Skyrim.Keyword.ActorTypeGhost) ?? false)
                {
                    if (!traits.Contains("ghostly"))
                        traits.Add("ghostly");
                }

                // If npc race is in creature_rules
                if (npc.Race.TryResolve(state.LinkCache, out var race) && race.EditorID != null && creatureRules.ContainsKey(race.EditorID))
                {
                    foreach (string trait in creatureRules[race.EditorID])
                    {
                        if (!traits.Contains(trait))
                            traits.Add(trait);
                    }
                }

                // If npc name is in creature_rules
                if (npc.Name != null && creatureRules.ContainsKey(npc.Name.ToString()!))
                {
                    foreach (string trait in creatureRules[npc.Name.ToString()!])
                    {
                        if (!traits.Contains(trait))
                            traits.Add(trait);
                    }
                }

                // If npc EDID is in creature_rules
                if (npc.EditorID != null && creatureRules.ContainsKey(npc.EditorID))
                {
                    foreach (string trait in creatureRules[npc.EditorID])
                    {
                        if (!traits.Contains(trait))
                            traits.Add(trait);
                    }
                }

                // If Ice Wraith add ghostly
                if (npc.Name != null && npc.Name.ToString() == "Ice Wraith")
                {
                    if (!traits.Contains("ghostly"))
                        traits.Add("ghostly");
                }

                // Add perks
                if (traits.Any())
                {
                    Npc kyeNpc = state.PatchMod.Npcs.GetOrAddAsOverride(npc);
                    if (kyeNpc.Perks == null)
                        kyeNpc.Perks = new ExtendedList<PerkPlacement>();
                    foreach (string trait in traits)
                    {
                        PerkPlacement p = new PerkPlacement() { Perk = perks[trait], Rank = 1 };
                        kyeNpc.Perks.Add(p);
                    }
                    /* For debugging purposes
                    if (npc.Name != null && traits.Any())
                    {
                        Console.WriteLine("NPC " + npc.Name! + " receives traits: " + traits.Count);
                        foreach (string t in traits)
                        {
                            Console.WriteLine(t);
                        }
                    }
                    */
                }
            }
        }
    }
}
