using System;
using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Newtonsoft.Json.Linq;
using Alphaleonis.Win32.Filesystem;
using Wabbajack.Common;

namespace KnowYourEnemyMutagen
{
    public class Program
    {
        public static int Main(string[] args)
        {
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
            // ***** Part 0 *****
            // Reading JSON and converting it to a normal list because .Contains() is weird in Newtonsoft.JSON
            JObject creature_rules = JObject.Parse(File.ReadAllText("creature_rules.json"));
            JObject misc = JObject.Parse(File.ReadAllText("misc.json"));

            List<string> resistances_and_weaknesses = new List<string>();
            List<string> abilities_to_clean = new List<string>();
            List<string> perks_to_clean = new List<string>();
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

            // ***** PART 1a *****
            // Removing other magical resistance/weakness systems
            foreach (var spell in state.LoadOrder.PriorityOrder.WinningOverrides<ISpellGetter>())
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

                //if (perk.EditorID != null && perks_to_clean.Contains(perk.EditorID))
                {
                    foreach (IAPerkEntryPointEffect ap in perk.Effects)
                    {
                        try
                        {
                            //Console.WriteLine(ap.EntryPoint.ToDescriptionString());
                        }
                        catch (Exception) { }
                    }
                }

            }
        }
    }
}
