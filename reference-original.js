/*
====================
HOW TO USE THIS FILE
====================

Do not modify this file unless you know what you are doing. If you simply wish
to change creature trait assignment see creature_rules.js instead.

*/

/*jshint esversion: 6 */

// Returns the EDID of a record that is referenced by another record.
let edid_of_referenced_record = function(referencing_record, path_to_reference) {
    let ref = xelib.GetLinksTo(referencing_record, path_to_reference);
    if (!ref) return '';
    return xelib.EditorID(ref);
};

// Returns a map linking editor id to form id and handle for all records of type record_type in file
let map_of_editor_id_to_form_id_and_handle = function(file, record_type) {
    let records = xelib.GetRecords(file, record_type);
    record_map = {};
    records.forEach(record => {
        record_map[xelib.EditorID(record)] = {
            "form_id": xelib.GetHexFormID(record),
            "handle": record
        };
    });
    return record_map;
};

// Returns a map linking editor id to form id and handle for all perks in the supplied filenames
let load_perks = function(filenames) {
    let perk_map = {};
    filenames.forEach(filename => {
        let file = xelib.FileByName(filename);
        if (!file) return;
        Object.assign(perk_map, map_of_editor_id_to_form_id_and_handle(file, 'PERK'));
    });
    return perk_map;
};

// returns a perk form id from the perk_map
let resolve_perk = function(trait, locals) {
    let perk_editor_id = 'kye_perk_' + trait.replace(/ /g, '_');
    let perk = locals.perk_map[perk_editor_id];
    if (!perk) return perk;
    return perk.form_id;
};

// adds the specified perks to the passed record
let add_perks = function(record, traits, locals, helpers) {
	let edid = xelib.EditorID(record);
    traits.forEach(trait => {
        if (trait_needs_addon(edid, trait, locals, helpers)) return;
        let perk = resolve_perk(trait, locals);
        if (!perk) {
            helpers.logMessage(`Error: attempted to add trait ${trait} to ${edid}, but trait does not exist.`);
        } else {
            xelib.AddPerk(record, perk, '1');
        }
    });
};

let trait_needs_addon = function(edid, trait, locals, helpers) {
    if (["water elemental", "earth elemental", "wind elemental"].includes(trait) && !locals.know_your_elements_installed) {
        helpers.logMessage(`Warning: ${edid} has been assigned trait ${trait} in creature_rules.js, but this will be missing unless you install Know Your Elements.`);
        return true;
    }
    if (trait === "dark elemental" && !locals.light_and_shadow_installed) { 
        helpers.logMessage(`Warning: ${edid} has been assigned trait ${trait} in creature_rules.js, but this will be missing unless you install KYE light and shadow.`);
        return true;
    }
    return false;
};

// Returns a scaled damage mod multiplier.
let adjust_damage_mod_magnitude = function(magnitude, scale) {
    if (magnitude == 1) return magnitude;
    if (magnitude > 1) {
        return (magnitude-1)*scale + 1;
    } else {
        return 1/adjust_damage_mod_magnitude(1/magnitude, scale);
    }
};

let adjust_magic_resist_magnitude = function(magnitude, scale) {
    if (magnitude == 0) return magnitude;
    return magnitude*scale;
};

let merge_arrays = function(array1, array2) {
    array2.forEach(element => {
        if (array1.includes(element)) return;
        array1.push(element);
    });
};

registerPatcher({
    info: info,
    gameModes: [xelib.gmSSE, xelib.gmTES5],
    settings: {
        label: 'Know Your Enemy Patcher',
        hide: false,
        templateUrl: `${patcherUrl}/partials/settings.html`,
        controller: function($scope) {
            let patcherSettings = $scope.settings.KnowYourEnemyPatcher;
        },
        defaultSettings: {
            patchFileName: 'know_your_enemy_patch.esp',
            silverPerk: 'false',
            effectIntensity: 1.00
        }
    },
    requiredFiles: ['know_your_enemy.esp'],
    execute: (patchFile, helpers, settings, locals) => ({
        initialize: function() {
            locals.creature_rules = require(`${patcherPath}\\creature_rules.js`);
            locals.misc_rules = require(`${patcherPath}\\misc.js`);
            locals.know_your_elements_installed = xelib.HasElement(0, 'Know Your Elements.esp');
            locals.light_and_shadow_installed = xelib.HasElement(0, 'KYE Light and Shadow.esp');
            locals.CACO_installed = xelib.HasElement(0, 'Complete Alchemy & Cooking Overhaul.esp');
            locals.SIC_installed = xelib.HasElement(0, 'Skyrim Immersive Creatures.esp');
            locals.perk_map = load_perks([
                'KYE Light and Shadow.esp', 
                'Know Your Elements.esp',
                'know_your_enemy.esp', 
            ]);
        },
        process: [{
            // ***** PART 1a *****
            // Remove other magical resistance/weakness systems
            load: {
                signature: 'SPEL',
                filter: function(record) {
                    return locals.misc_rules.abilities_to_clean.includes(xelib.EditorID(record));
                }
            },
            patch: function(record) {
                xelib.GetElements(record, 'Effects').forEach(effect => {
                    if (locals.misc_rules.resistances_and_weaknesses.includes(edid_of_referenced_record(effect, 'EFID'))) {
                        xelib.SetValue(effect, 'EFIT\\Magnitude', '0.000000');
                    }
                });
            }
        }, {
            // ***** PART 1b *****
            // Remove other weapon resistance systems.
            load: {
                signature: 'PERK',
                filter: function (record) {
                    return locals.misc_rules.perks_to_clean.includes(xelib.EditorID(record));
                }
            },
            patch : function (record) {
                xelib.GetElements(record, 'Effects').forEach(effect => {
                    if (xelib.GetValue(effect, 'DATA\\Entry Point\\Entry Point') !== 'Mod Incoming Damage') return;
                    xelib.SetValue(effect, 'Function Parameters\\EPFD\\Float', '1.00');
                });
            }
        }, {
            // ***** PART 2a *****
            // Adjust KYE's physical effects according to effectIntensity.
            load: {
                signature: 'PERK',
                filter: function (record) {
                    if (settings.effectIntensity == 1.00) return false;
                    if (!xelib.HasElement(record, 'Effects')) return false;
                    return locals.misc_rules.kye_perk_names.includes(xelib.EditorID(record));
                }
            },
            patch : function (record) {
                xelib.GetElements(record, 'Effects').forEach(effect => {
                	if (xelib.GetValue(effect, 'PRKE\\Type') != 'Entry Point') return;
                    let entry_point = xelib.GetValue(effect, 'DATA\\Entry Point\\Entry Point');
                    if (!['Mod Incoming Damage', 'Mod Attack Damage'].includes(entry_point)) return;
                    let current_magnitude = xelib.GetFloatValue(effect, 'Function Parameters\\EPFD\\Float');
                    let new_magnitude = adjust_damage_mod_magnitude(current_magnitude, settings.effectIntensity);
                    xelib.SetFloatValue(effect, 'Function Parameters\\EPFD\\Float', new_magnitude);
                });
            }
        }, {
            // ***** PART 2b *****
            // Adjust KYE's magical effects according to effectIntensity.
            load: {
                signature: 'SPEL',
                filter: function (record) {
                    if (settings.effectIntensity == 1.00) return false;
                    return (locals.misc_rules.kye_ability_names.includes(xelib.EditorID(record)) &&
                            xelib.HasElement(record, 'Effects'));
                }
            },
            patch : function (record) {
                xelib.GetElements(record, 'Effects').forEach(effect => {
                    if (locals.misc_rules.resistances_and_weaknesses.includes(edid_of_referenced_record(effect, 'EFID'))) {
                        let current_magnitude = xelib.GetFloatValue(effect, 'EFIT\\Magnitude');
                        let new_magnitude = adjust_magic_resist_magnitude(current_magnitude, settings.effectIntensity);
                        xelib.SetFloatValue(effect, 'EFIT\\Magnitude', new_magnitude);
                    }
                });
            }
         }, {
            // ***** PART 3 *****
            // Edit the effect of silver weapons
            records: () => {
				let silverPerk = xelib.GetElement(0, "Skyrim.esm\\PERK\\SilverPerk");
				return [silverPerk].map(rec => xelib.GetWinningOverride(rec));
            },
            patch: silver_perk => {
                if (settings.silverPerk !== "true") return;
                if (locals.SIC_installed) {
                    helpers.logMessage("Warning: Silver perk is being patched, but Skyrim Immersive Creatures detected in load order. Know Your Enemy's silver weapon effects will NOT work against new races added by SIC.");
                }
                let vanilla_effects = xelib.GetElement(silver_perk, 'Effects');
                let kye_effects = xelib.GetElement(locals.perk_map.DummySilverPerk.handle, 'Effects');
                xelib.SetElement(vanilla_effects, kye_effects);
            }
        }, {
            // ***** PART 4 *****
            // Adjust traits to accommodate CACO if present.
            load: {
                signature: 'SPEL',
                filter: function(record) {
                    if (!locals.CACO_installed) return false;
                    return ['kye_ab_undead', 'kye_ab_ghostly'].includes(xelib.EditorID(record));
                }
            },
            patch: function(record) {
                xelib.GetElements(record, 'Effects').forEach(effect => {
                    if (edid_of_referenced_record(effect, 'EFID') =='AbResistPoison') {
                        xelib.SetValue(effect, 'EFIT\\Magnitude', '0.000000');
                    }
                });
            }
        }, {
            // ***** PART 5 *****
            // Add the traits to NPCs
            load: {
                signature: 'NPC_',
                filter: function(record) {
                    if (xelib.HasElement(record, 'ACBS\\Template Flags')) {
                        let flags = xelib.GetEnabledFlags(record, 'ACBS\\Template Flags');
                        if (flags.includes("Use Spell List")) return false;
                    }

                    if (xelib.HasKeyword(record, 'ActorTypeGhost')) return true;

                    let race_name = edid_of_referenced_record(record, 'RNAM');
                    if (locals.creature_rules[race_name] != undefined) return true;

                    let record_name = xelib.GetValue(record, 'FULL');
                    if (locals.creature_rules[record_name] != undefined) return true;

                    let record_id = xelib.GetValue(record, 'EDID');
                    if (locals.creature_rules[record_id] != undefined) return true;

                    return false;
                }
            },
            patch: function(record) {
                let traits = [];

                let race_traits = locals.creature_rules[edid_of_referenced_record(record, 'RNAM')];
                if (race_traits) merge_arrays(traits, race_traits);

                let name_traits = locals.creature_rules[xelib.GetValue(record, 'FULL')];
                if (name_traits) merge_arrays(traits, name_traits);

                let edid_traits = locals.creature_rules[xelib.EditorID(record)];
                if (edid_traits) merge_arrays(traits, edid_traits);

                if (xelib.HasKeyword(record, 'ActorTypeGhost') && xelib.GetValue(record, 'FULL') != 'Ice Wraith') {
                    merge_arrays(traits, ["ghostly"]);
                }
                add_perks(record, traits, locals, helpers);
            }
        }],
    })
});