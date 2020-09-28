using System;
using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Newtonsoft.Json.Linq;
using Alphaleonis.Win32.Filesystem;
using Wabbajack.Common;
using Mutagen.Bethesda.Oblivion;
using Noggog;
using HtmlAgilityPack;

namespace KnowYourEnemyMutagen
{
    public class Program
    {
        public static int Main(string[] args)
        {
            foreach(string x in args)
            {
                Console.WriteLine(x);
            }
            return SynthesisPipeline.Instance.Patch<ISkyrimMod, ISkyrimModGetter>(
                args: args,
                patcher: RunPatch,
                new UserPreferences()
                {
                    ActionsForEmptyArgs = new RunDefaultPatcher()
                    {
                        IdentifyingModKey = "know_your_enemy_patcher.esp",
                        //BlockAutomaticExit = true,
                        TargetRelease = GameRelease.SkyrimSE
                    }
                });
        }


        public static void RunPatch(SynthesisState<ISkyrimMod, ISkyrimModGetter> state)
        {
            float adjust_damage_mod_magnitude(float magnitude, float scale)
            {
                if (magnitude == 1) return magnitude;
                if (magnitude > 1)
                {
                    return (magnitude - 1) * scale + 1;
                }
                else
                {
                    return 1 / adjust_damage_mod_magnitude(1 / magnitude, scale);
                }
            }
            // ***** Part 0 *****
            // Reading JSON and converting it to a normal list because .Contains() is weird in Newtonsoft.JSON
            JObject creature_rules = JObject.Parse(File.ReadAllText("creature_rules.json"));
            JObject misc = JObject.Parse(File.ReadAllText("misc.json"));
            JObject e = JObject.Parse(File.ReadAllText("effect_intensity.json"));
            float effect_intensity = (float)e["effect_intensity"]!;
            Console.WriteLine("Effect Intensity: " + effect_intensity);

            List<string> resistances_and_weaknesses = new List<string>();
            List<string> abilities_to_clean = new List<string>();
            List<string> perks_to_clean = new List<string>();
            List<string> kye_ability_names = new List<string>();
            foreach (string? rw in misc["resistances_and_weaknesses"]!)
            {
                if (rw != null) resistances_and_weaknesses.Add(rw);
            }
            foreach (string? ab in misc["abilities_to_clean"]!)
            {
                if (ab != null) abilities_to_clean.Add(ab);
            }
            foreach(string? pe in misc["perks_to_clean"]!)
            {
                if (pe != null) perks_to_clean.Add(pe);
            }
            foreach(string? ab in misc["kye_ability_names"]!)
            {
                if (ab != null) kye_ability_names.Add(ab);
            }
            
            

            // ***** PART 1a *****
            // Removing other magical resistance/weakness systems
            foreach (var spell in state.LoadOrder.PriorityOrder.WinningOverrides<Mutagen.Bethesda.Skyrim.ISpellGetter>())
            {
                if (spell.EditorID != null && abilities_to_clean.Contains(spell.EditorID))
                {
                    var modifiedSpell = spell.DeepCopy();
                    foreach (var effect in modifiedSpell.Effects)
                    {
                        effect.BaseEffect.TryResolve(state.LinkCache, out var baseEffect);
                        if (baseEffect != null && baseEffect.EditorID != null)
                        {
                            if (resistances_and_weaknesses.Contains(baseEffect.EditorID))
                            {
                                if (effect.Data != null)
                                {
                                    effect.Data.Magnitude = 0;
                                    state.PatchMod.Spells.GetOrAddAsOverride(modifiedSpell);
                                }
                                else
                                {
                                    Console.WriteLine("Error setting Effect Magnitude - DATA was null!");
                                }
                            }
                        }
                    }
                }
            }
            // ***** PART 1b *****
            // Remove other weapon resistance systems
            foreach (var perk in state.LoadOrder.PriorityOrder.WinningOverrides<IPerkGetter>())
            {
                if (perk.EditorID != null && perks_to_clean.Contains(perk.EditorID))
                {
                    foreach (var eff in perk.Effects)
                    {
                        if (!(eff is PerkModifyValue modValue)) continue;
                        if (modValue.EntryPoint != APerkEntryPointEffect.EntryType.ModIncomingDamage) continue;
                        modValue.Value = 1f;
                        modValue.Modification = PerkModifyValue.ModificationType.Set;
                    }
                }
            }

            // ***** PART 2a *****
            // Adjust KYE's magical effects according to effect_intensity
            if (effect_intensity != 1)
            {
                foreach (var perk in state.LoadOrder.PriorityOrder.WinningOverrides<IPerkGetter>())
                {
                    if (perk.EditorID != null && kye_ability_names.Contains(perk.EditorID) && perk.Effects.Any())
                    {
                        foreach(var eff in perk.Effects)
                        {
                            if (!(eff is PerkModifyValue modValue)) continue;
                            if (modValue.EntryPoint != APerkEntryPointEffect.EntryType.ModIncomingDamage) continue;
                            float current_magnitude = modValue.Value;
                            modValue.Value = adjust_damage_mod_magnitude(current_magnitude, effect_intensity);
                            modValue.Modification = PerkModifyValue.ModificationType.Set;
                        }
                    }
                }
            }
        }
    }
}
