namespace Source1;

static partial class SourceEntities
{
	static readonly SourceEntity[] Table =
	[
		new( "css", SourceEntityKind.Weapon, "ak47", "AK-47", "models/weapons/w_rif_ak47.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 32, "Pellets": 1, "Range": 8192, "Radius": 1, "Force": 300, "SpreadBase": "0.25,0.25", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "DisplayName": "CS:S AK-47", "HoldType": "rifle", "Npc": {"MinRange": 65, "MaxRange": 2048, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "ReloadTime": 2.5, "StartingAmmo": 90, "ViewModelPrefab": {"prefab": "entities/parts/ak47_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/ak47_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_rif_ak47.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "aug", "AUG", "models/weapons/w_rif_aug.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 32, "Pellets": 1, "Range": 8192, "Radius": 1, "Force": 300, "SpreadBase": "0.25,0.25", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "DisplayName": "CS:S AUG", "HoldType": "rifle", "Npc": {"MinRange": 65, "MaxRange": 2048, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "ReloadTime": 2.5, "StartingAmmo": 90, "ViewModelPrefab": {"prefab": "entities/parts/aug_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/aug_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_rif_aug.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "awp", "AWP", "models/weapons/w_snip_awp.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 75, "Pellets": 1, "Range": 8192, "Radius": 1, "Force": 300, "SpreadBase": "0.05,0.05", "SpreadGrowth": "0,0", "SpreadRecovery": 0.2}, "ClipMaxSize": 10, "DisplayName": "CS:S AWP", "HoldType": "rifle", "Npc": {"MinRange": 0, "MaxRange": 8192, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PrimaryAutomatic": false, "PrimaryDelay": 1.25, "ReloadTime": 3, "StartingAmmo": 30, "ViewModelPrefab": {"prefab": "entities/parts/awp_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/awp_wm.prefab"}, "ZoomLevels": [40, 10]}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_snip_awp.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "deagle", "Desert Eagle", "models/weapons/w_pist_deagle.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"PreferredSlot": 1, "Ballistics": {"Damage": 25, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.2,0.2", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "ClipMaxSize": 13, "DisplayName": "CS:S Desert Eagle", "HoldType": "pistol", "Npc": {"MinRange": 24, "MaxRange": 1500, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PrimaryAutomatic": false, "PrimaryDelay": 0.15, "ReloadTime": 2.2, "StartingAmmo": 52, "ViewModelPrefab": {"prefab": "entities/parts/deagle_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/deagle_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_pist_deagle.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "elite", "Dual Elites", "models/weapons/w_pist_elite.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"PreferredSlot": 1, "Ballistics": {"Damage": 25, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.2,0.2", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "ClipMaxSize": 13, "DisplayName": "CS:S Dual Elites", "HoldType": "dual_pistols", "Npc": {"MinRange": 24, "MaxRange": 1500, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PrimaryAutomatic": false, "PrimaryDelay": 0.15, "ReloadTime": 2.2, "StartingAmmo": 52, "ViewModelPrefab": {"prefab": "entities/parts/elite_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/elite_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_pist_elite.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "famas", "FAMAS", "models/weapons/w_rif_famas.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 32, "Pellets": 1, "Range": 8192, "Radius": 1, "Force": 300, "SpreadBase": "0.25,0.25", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "DisplayName": "CS:S FAMAS", "HoldType": "rifle", "Npc": {"MinRange": 65, "MaxRange": 2048, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "ReloadTime": 2.5, "StartingAmmo": 90, "ViewModelPrefab": {"prefab": "entities/parts/famas_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/famas_wm.prefab"}, "Secondary": "Burst"}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_rif_famas.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "fiveseven", "Five-SeveN", "models/weapons/w_pist_fiveseven.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"PreferredSlot": 1, "Ballistics": {"Damage": 25, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.2,0.2", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "ClipMaxSize": 13, "DisplayName": "CS:S Five-SeveN", "HoldType": "pistol", "Npc": {"MinRange": 24, "MaxRange": 1500, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PrimaryAutomatic": false, "PrimaryDelay": 0.15, "ReloadTime": 2.2, "StartingAmmo": 52, "ViewModelPrefab": {"prefab": "entities/parts/fiveseven_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/fiveseven_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_pist_fiveseven.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "flashbang", "Flashbang", "models/weapons/w_eq_flashbang.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": null,
					"Source1.SourceProjectileWeapon": {"Ballistics": {"Damage": 8, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.200000003,0.200000003", "SpreadGrowth": "1.5,1.5", "SpreadRecovery": 0.2}, "CanCancelReload": false, "ClipMaxSize": 1, "DeployTime": 0.2, "DisplayName": "CS:S Flashbang", "DroppedGameObject": {"ref": "standalone_wm"}, "HoldType": "throwable", "PreferredSlot": 3, "PrimaryAutomatic": false, "PrimaryDelay": 0.8, "ReloadTime": 0.5, "SecondaryDelay": 1, "StartingAmmo": 3, "UsableByNpcs": false, "Value": 10, "ViewModelPrefab": {"prefab": "entities/parts/flashbang_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/flashbang_wm.prefab"}, "Kind": "Flashbang", "ProjectileModel": "models/weapons/w_eq_flashbang_thrown.mdl"}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_eq_flashbang.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "g3sg1", "G3SG1", "models/weapons/w_snip_g3sg1.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 75, "Pellets": 1, "Range": 8192, "Radius": 1, "Force": 300, "SpreadBase": "0.05,0.05", "SpreadGrowth": "0,0", "SpreadRecovery": 0.2}, "ClipMaxSize": 10, "DisplayName": "CS:S G3SG1", "HoldType": "rifle", "Npc": {"MinRange": 0, "MaxRange": 8192, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PrimaryAutomatic": false, "PrimaryDelay": 1.25, "ReloadTime": 3, "StartingAmmo": 30, "ViewModelPrefab": {"prefab": "entities/parts/g3sg1_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/g3sg1_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_snip_g3sg1.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "galil", "Galil", "models/weapons/w_rif_galil.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 32, "Pellets": 1, "Range": 8192, "Radius": 1, "Force": 300, "SpreadBase": "0.25,0.25", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "DisplayName": "CS:S Galil", "HoldType": "rifle", "Npc": {"MinRange": 65, "MaxRange": 2048, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "ReloadTime": 2.5, "StartingAmmo": 90, "ViewModelPrefab": {"prefab": "entities/parts/galil_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/galil_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_rif_galil.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "glock", "Glock 18", "models/weapons/w_pist_glock18.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"PreferredSlot": 1, "Ballistics": {"Damage": 25, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.2,0.2", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "ClipMaxSize": 13, "DisplayName": "CS:S Glock 18", "HoldType": "pistol", "Npc": {"MinRange": 24, "MaxRange": 1500, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PrimaryAutomatic": false, "PrimaryDelay": 0.15, "ReloadTime": 2.2, "StartingAmmo": 52, "ViewModelPrefab": {"prefab": "entities/parts/glock_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/glock_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_pist_glock18.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "hegrenade", "HE Grenade", "models/weapons/w_eq_fraggrenade.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": null,
					"Source1.SourceProjectileWeapon": {"Ballistics": {"Damage": 8, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.200000003,0.200000003", "SpreadGrowth": "1.5,1.5", "SpreadRecovery": 0.2}, "CanCancelReload": false, "ClipMaxSize": 1, "DeployTime": 0.2, "DisplayName": "CS:S HE Grenade", "DroppedGameObject": {"ref": "standalone_wm"}, "HoldType": "throwable", "PreferredSlot": 3, "PrimaryAutomatic": false, "PrimaryDelay": 0.8, "ReloadTime": 0.5, "SecondaryDelay": 1, "StartingAmmo": 3, "UsableByNpcs": false, "Value": 10, "ViewModelPrefab": {"prefab": "entities/parts/hegrenade_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/hegrenade_wm.prefab"}, "Kind": "HeGrenade", "ProjectileModel": "models/weapons/w_eq_fraggrenade_thrown.mdl"}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_eq_fraggrenade.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "knife", "Knife", "models/weapons/w_knife_t.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": null,
					"Source1.CssKnife": {"Ballistics": {"Damage": 8, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.200000003,0.200000003", "SpreadGrowth": "1.5,1.5", "SpreadRecovery": 0.2}, "CanCancelReload": false, "ClipMaxSize": 45, "DeployTime": 0.2, "DisplayName": "CS:S Knife", "DroppedGameObject": {"ref": "standalone_wm"}, "Handedness": "Right", "HoldType": "knife", "PreferredSlot": 2, "PrimaryDelay": 0.075, "ReloadTime": 1.5, "SecondaryDelay": 1, "StartingAmmo": 135, "UsableByNpcs": false, "Value": 10, "ViewModelPrefab": {"prefab": "entities/parts/knife_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/knife_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_knife_t.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "m249", "M249", "models/weapons/w_mach_m249para.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 32, "Pellets": 1, "Range": 8192, "Radius": 1, "Force": 300, "SpreadBase": "0.5,0.5", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "ClipMaxSize": 100, "DisplayName": "CS:S M249", "HoldType": "rifle", "Npc": {"MinRange": 65, "MaxRange": 2048, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PrimaryDelay": 0.08, "ReloadTime": 5, "StartingAmmo": 200, "ViewModelPrefab": {"prefab": "entities/parts/m249_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/m249_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_mach_m249para.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "m3", "M3 Super 90", "models/weapons/w_shot_m3super90.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 20, "Pellets": 8, "Range": 3000, "Radius": 1, "Force": 300, "SpreadBase": "4,4", "SpreadGrowth": "0,0", "SpreadRecovery": 0.2}, "ClipMaxSize": 8, "DisplayName": "CS:S M3 Super 90", "HoldType": "shotgun", "Npc": {"MinRange": 0, "MaxRange": 500, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PrimaryDelay": 0.9, "ReloadTime": 1.5, "StartingAmmo": 32, "ViewModelPrefab": {"prefab": "entities/parts/m3_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/m3_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_shot_m3super90.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "m4a1", "M4A1", "models/weapons/w_rif_m4a1.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 32, "Pellets": 1, "Range": 8192, "Radius": 1, "Force": 300, "SpreadBase": "0.25,0.25", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "DisplayName": "CS:S M4A1", "HoldType": "rifle", "Npc": {"MinRange": 65, "MaxRange": 2048, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "ReloadTime": 2.5, "StartingAmmo": 90, "ViewModelPrefab": {"prefab": "entities/parts/m4a1_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/m4a1_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_rif_m4a1.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "mac10", "MAC-10", "models/weapons/w_smg_mac10.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 26, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.4,0.4", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "DisplayName": "CS:S MAC-10", "HoldType": "smg", "PrimaryDelay": 0.08, "ReloadTime": 3, "StartingAmmo": 120, "ViewModelPrefab": {"prefab": "entities/parts/mac10_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/mac10_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_smg_mac10.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "mp5navy", "MP5 Navy", "models/weapons/w_smg_mp5.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 26, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.4,0.4", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "DisplayName": "CS:S MP5 Navy", "HoldType": "smg", "PrimaryDelay": 0.08, "ReloadTime": 3, "StartingAmmo": 120, "ViewModelPrefab": {"prefab": "entities/parts/mp5navy_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/mp5navy_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_smg_mp5.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "p228", "P228", "models/weapons/w_pist_p228.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"PreferredSlot": 1, "Ballistics": {"Damage": 25, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.2,0.2", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "ClipMaxSize": 13, "DisplayName": "CS:S P228", "HoldType": "pistol", "Npc": {"MinRange": 24, "MaxRange": 1500, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PrimaryAutomatic": false, "PrimaryDelay": 0.15, "ReloadTime": 2.2, "StartingAmmo": 52, "ViewModelPrefab": {"prefab": "entities/parts/p228_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/p228_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_pist_p228.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "p90", "P90", "models/weapons/w_smg_p90.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 26, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.4,0.4", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "DisplayName": "CS:S P90", "HoldType": "smg", "PrimaryDelay": 0.08, "ReloadTime": 3, "StartingAmmo": 120, "ViewModelPrefab": {"prefab": "entities/parts/p90_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/p90_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_smg_p90.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "scout", "Scout", "models/weapons/w_snip_scout.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 75, "Pellets": 1, "Range": 8192, "Radius": 1, "Force": 300, "SpreadBase": "0.05,0.05", "SpreadGrowth": "0,0", "SpreadRecovery": 0.2}, "ClipMaxSize": 10, "DisplayName": "CS:S Scout", "HoldType": "rifle", "Npc": {"MinRange": 0, "MaxRange": 8192, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PrimaryAutomatic": false, "PrimaryDelay": 1.25, "ReloadTime": 3, "StartingAmmo": 30, "ViewModelPrefab": {"prefab": "entities/parts/scout_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/scout_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_snip_scout.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "sg550", "SG 550", "models/weapons/w_snip_sg550.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 75, "Pellets": 1, "Range": 8192, "Radius": 1, "Force": 300, "SpreadBase": "0.05,0.05", "SpreadGrowth": "0,0", "SpreadRecovery": 0.2}, "ClipMaxSize": 10, "DisplayName": "CS:S SG 550", "HoldType": "rifle", "Npc": {"MinRange": 0, "MaxRange": 8192, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PrimaryAutomatic": false, "PrimaryDelay": 1.25, "ReloadTime": 3, "StartingAmmo": 30, "ViewModelPrefab": {"prefab": "entities/parts/sg550_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/sg550_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_snip_sg550.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "sg552", "SG 552", "models/weapons/w_rif_sg552.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 32, "Pellets": 1, "Range": 8192, "Radius": 1, "Force": 300, "SpreadBase": "0.25,0.25", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "DisplayName": "CS:S SG 552", "HoldType": "rifle", "Npc": {"MinRange": 65, "MaxRange": 2048, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "ReloadTime": 2.5, "StartingAmmo": 90, "ViewModelPrefab": {"prefab": "entities/parts/sg552_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/sg552_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_rif_sg552.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "smokegrenade", "Smoke Grenade", "models/weapons/w_eq_smokegrenade.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": null,
					"Source1.SourceProjectileWeapon": {"Ballistics": {"Damage": 8, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.200000003,0.200000003", "SpreadGrowth": "1.5,1.5", "SpreadRecovery": 0.2}, "CanCancelReload": false, "ClipMaxSize": 1, "DeployTime": 0.2, "DisplayName": "CS:S Smoke Grenade", "DroppedGameObject": {"ref": "standalone_wm"}, "HoldType": "throwable", "PreferredSlot": 3, "PrimaryAutomatic": false, "PrimaryDelay": 0.8, "ReloadTime": 0.5, "SecondaryDelay": 1, "StartingAmmo": 3, "UsableByNpcs": false, "Value": 10, "ViewModelPrefab": {"prefab": "entities/parts/smokegrenade_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/smokegrenade_wm.prefab"}, "Kind": "SmokeGrenade", "ProjectileModel": "models/weapons/w_eq_smokegrenade_thrown.mdl"}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_eq_smokegrenade.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "tmp", "TMP", "models/weapons/w_smg_tmp.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 26, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.4,0.4", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "DisplayName": "CS:S TMP", "HoldType": "smg", "PrimaryDelay": 0.08, "ReloadTime": 3, "StartingAmmo": 120, "ViewModelPrefab": {"prefab": "entities/parts/tmp_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/tmp_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_smg_tmp.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "ump45", "UMP-45", "models/weapons/w_smg_ump45.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 26, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.4,0.4", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "DisplayName": "CS:S UMP-45", "HoldType": "smg", "PrimaryDelay": 0.08, "ReloadTime": 3, "StartingAmmo": 120, "ViewModelPrefab": {"prefab": "entities/parts/ump45_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/ump45_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_smg_ump45.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "usp", "USP .45", "models/weapons/w_pist_usp.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"PreferredSlot": 1, "Ballistics": {"Damage": 25, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.2,0.2", "SpreadGrowth": "1,1", "SpreadRecovery": 0.2}, "ClipMaxSize": 13, "DisplayName": "CS:S USP .45", "HoldType": "pistol", "Npc": {"MinRange": 24, "MaxRange": 1500, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PrimaryAutomatic": false, "PrimaryDelay": 0.15, "ReloadTime": 2.2, "StartingAmmo": 52, "ViewModelPrefab": {"prefab": "entities/parts/usp_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/usp_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_pist_usp.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Weapon, "xm1014", "XM1014", "models/weapons/w_shot_xm1014.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"Ballistics": {"Damage": 20, "Pellets": 8, "Range": 3000, "Radius": 1, "Force": 300, "SpreadBase": "4,4", "SpreadGrowth": "0,0", "SpreadRecovery": 0.2}, "ClipMaxSize": 8, "DisplayName": "CS:S XM1014", "HoldType": "shotgun", "Npc": {"MinRange": 0, "MaxRange": 500, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PrimaryDelay": 0.9, "ReloadTime": 1.5, "StartingAmmo": 32, "ViewModelPrefab": {"prefab": "entities/parts/xm1014_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/xm1014_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_shot_xm1014.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Weapon, "357", ".357 Magnum", "models/weapons/w_357.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"PreferredSlot": 2, "Ballistics": {"Damage": 8, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.2,0.2", "SpreadGrowth": "1.5,1.5", "SpreadRecovery": 0.2}, "ClipMaxSize": 45, "DisplayName": "HL2 .357 Magnum", "HoldType": "pistol_two_handed", "Npc": {"MinRange": 65, "MaxRange": 1024, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PrimaryAutomatic": false, "PrimaryDelay": 0.7, "ReloadTime": 1.5, "StartingAmmo": 36, "ViewModelPrefab": {"prefab": "entities/parts/357_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/357_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_357.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Weapon, "ar2", "Pulse Rifle", "models/weapons/w_irifle.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"PreferredSlot": 2, "Ballistics": {"Damage": 8, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.2,0.2", "SpreadGrowth": "1.5,1.5", "SpreadRecovery": 0.2}, "ClipMaxSize": 45, "DisplayName": "HL2 Pulse Rifle", "HoldType": "rifle", "Npc": {"MinRange": 65, "MaxRange": 2048, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "ReloadTime": 1.5, "StartingAmmo": 90, "ViewModelPrefab": {"prefab": "entities/parts/ar2_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/ar2_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_irifle.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Weapon, "bugbait", "Bugbait", "models/weapons/w_bugbait.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": null,
					"Source1.SourceProjectileWeapon": {"Ballistics": {"Damage": 8, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.200000003,0.200000003", "SpreadGrowth": "1.5,1.5", "SpreadRecovery": 0.2}, "CanCancelReload": false, "ClipMaxSize": 1, "DeployTime": 0.2, "DisplayName": "HL2 Bugbait", "DroppedGameObject": {"ref": "standalone_wm"}, "HoldType": "throwable", "PreferredSlot": 4, "PrimaryAutomatic": false, "PrimaryDelay": 0.8, "ReloadTime": 1.7, "SecondaryDelay": 1, "StartingAmmo": 12, "UsableByNpcs": false, "UsesAmmo": false, "Value": 10, "ViewModelPrefab": {"prefab": "entities/parts/bugbait_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/bugbait_wm.prefab"}, "Kind": "Bugbait", "ProjectileModel": "models/weapons/w_bugbait.mdl", "ProjectileDamage": 0}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_bugbait.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Weapon, "crossbow", "Crossbow", "models/weapons/w_crossbow.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": null,
					"Source1.SourceProjectileWeapon": {"Ballistics": {"Damage": 8, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.200000003,0.200000003", "SpreadGrowth": "1.5,1.5", "SpreadRecovery": 0.2}, "CanCancelReload": false, "ClipMaxSize": 1, "DeployTime": 0.2, "DisplayName": "HL2 Crossbow", "DroppedGameObject": {"ref": "standalone_wm"}, "HoldType": "rifle", "Npc": {"MinRange": 65, "MaxRange": 1024, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PreferredSlot": 4, "PrimaryAutomatic": false, "PrimaryDelay": 0.8, "ReloadTime": 1.7, "SecondaryDelay": 1, "StartingAmmo": 12, "Value": 10, "ViewModelPrefab": {"prefab": "entities/parts/crossbow_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/crossbow_wm.prefab"}, "ProjectileModel": "models/crossbow_bolt.mdl", "Secondary": "Zoom"}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_crossbow.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Weapon, "crowbar", "Crowbar", "models/weapons/w_crowbar.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": null,
					"Source1.SourceMeleeWeapon": {"Ballistics": {"Damage": 8, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.200000003,0.200000003", "SpreadGrowth": "1.5,1.5", "SpreadRecovery": 0.2}, "CanCancelReload": false, "ClipMaxSize": 1, "DeployTime": 0.2, "DisplayName": "HL2 Crowbar", "DroppedGameObject": {"ref": "standalone_wm"}, "Handedness": "Right", "HoldType": "melee", "PreferredSlot": 0, "PrimaryAutomatic": false, "PrimaryDelay": 0.8, "ReloadTime": 1.7, "SecondaryDelay": 1, "StartingAmmo": 12, "UsableByNpcs": false, "Value": 10, "ViewModelPrefab": {"prefab": "entities/parts/crowbar_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/crowbar_wm.prefab"}, "Damage": 25, "Range": 72, "SwingDelay": 0.4, "MissSwingDelay": 0.5}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_crowbar.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Weapon, "gravity_gun", "Gravity Gun", "models/weapons/w_physics.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": null,
					"Source1.Hl2GravityWeapon": {"Ballistics": {"Damage": 8, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.200000003,0.200000003", "SpreadGrowth": "1.5,1.5", "SpreadRecovery": 0.2}, "CanCancelReload": false, "ClipMaxSize": 1, "DeployTime": 0.2, "DisplayName": "HL2 Gravity Gun", "DroppedGameObject": {"ref": "standalone_wm"}, "HoldType": "tool", "PreferredSlot": 4, "PrimaryAutomatic": false, "PrimaryDelay": 0.8, "ReloadTime": 1.7, "SecondaryDelay": 1, "StartingAmmo": 12, "UsableByNpcs": false, "UsesAmmo": false, "Value": 10, "ViewModelPrefab": {"prefab": "entities/parts/gravity_gun_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/gravity_gun_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_physics.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Weapon, "grenade", "Frag Grenade", "models/weapons/w_grenade.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": null,
					"Source1.SourceProjectileWeapon": {"Ballistics": {"Damage": 8, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.200000003,0.200000003", "SpreadGrowth": "1.5,1.5", "SpreadRecovery": 0.2}, "CanCancelReload": false, "ClipMaxSize": 1, "DeployTime": 0.2, "DisplayName": "HL2 Frag Grenade", "DroppedGameObject": {"ref": "standalone_wm"}, "HoldType": "throwable", "PreferredSlot": 4, "PrimaryAutomatic": false, "PrimaryDelay": 0.8, "ReloadTime": 0.5, "SecondaryDelay": 1, "StartingAmmo": 5, "UsableByNpcs": false, "Value": 10, "ViewModelPrefab": {"prefab": "entities/parts/grenade_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/grenade_wm.prefab"}, "Kind": "Grenade", "ProjectileModel": "models/weapons/w_grenade.mdl", "ProjectileDamage": 125}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_grenade.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Weapon, "pistol", "Pistol", "models/weapons/w_pistol.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"PreferredSlot": 2, "Ballistics": {"Damage": 8, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.2,0.2", "SpreadGrowth": "1.5,1.5", "SpreadRecovery": 0.2}, "ClipMaxSize": 45, "DisplayName": "HL2 Pistol", "HoldType": "pistol", "Npc": {"MinRange": 24, "MaxRange": 1500, "BurstMin": 1, "BurstMax": 3, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PrimaryAutomatic": false, "PrimaryDelay": 0.15, "ReloadTime": 1.5, "StartingAmmo": 150, "ViewModelPrefab": {"prefab": "entities/parts/pistol_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/pistol_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_pistol.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Weapon, "rpg", "RPG", "models/weapons/w_rocket_launcher.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": null,
					"Source1.SourceProjectileWeapon": {"Ballistics": {"Damage": 8, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.200000003,0.200000003", "SpreadGrowth": "1.5,1.5", "SpreadRecovery": 0.2}, "CanCancelReload": false, "ClipMaxSize": 1, "DeployTime": 0.2, "DisplayName": "HL2 RPG", "DroppedGameObject": {"ref": "standalone_wm"}, "HoldType": "shoulder", "Npc": {"MinRange": 480, "MaxRange": 6000, "BurstMin": 2, "BurstMax": 5, "RestMin": 0.3, "RestMax": 0.6, "SpreadScale": 3, "CanBePickedUp": true}, "PreferredSlot": 4, "PrimaryAutomatic": false, "PrimaryDelay": 0.8, "ReloadTime": 2.2, "SecondaryDelay": 1, "StartingAmmo": 3, "Value": 10, "ViewModelPrefab": {"prefab": "entities/parts/rpg_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/rpg_wm.prefab"}, "Kind": "Rocket", "ProjectileModel": "models/weapons/w_missile.mdl", "ProjectileDamage": 150, "Secondary": "Guide"}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_rocket_launcher.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Weapon, "shotgun", "Shotgun", "models/weapons/w_shotgun.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"PreferredSlot": 2, "Ballistics": {"Damage": 8, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "4,4", "SpreadGrowth": "0,0", "SpreadRecovery": 0.2}, "ClipMaxSize": 45, "DisplayName": "HL2 Shotgun", "HoldType": "shotgun", "Npc": {"MinRange": 0, "MaxRange": 500, "BurstMin": 1, "BurstMax": 1, "RestMin": 0.75, "RestMax": 1.2, "SpreadScale": 3, "CanBePickedUp": true}, "PrimaryDelay": 0.75, "ReloadTime": 1.5, "StartingAmmo": 30, "ViewModelPrefab": {"prefab": "entities/parts/shotgun_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/shotgun_wm.prefab"}, "Secondary": "DoubleShot"}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_shotgun.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Weapon, "smg", "SMG", "models/weapons/w_smg1.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": {"PreferredSlot": 2, "Ballistics": {"Damage": 8, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.200000003,0.200000003", "SpreadGrowth": "1.5,1.5", "SpreadRecovery": 0.2}, "ClipMaxSize": 45, "DisplayName": "HL2 SMG", "HoldType": "smg", "PrimaryDelay": 0.075, "ReloadTime": 1.5, "StartingAmmo": 135, "ViewModelPrefab": {"prefab": "entities/parts/smg_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/smg_wm.prefab"}}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_smg1.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Weapon, "stunstick", "Stunstick", "models/weapons/w_stunbaton.mdl", null, """
			{
				".": {
					"Source1.SourceBulletWeapon": null,
					"Source1.Hl2Stunstick": {"Ballistics": {"Damage": 8, "Pellets": 1, "Range": 4096, "Radius": 1, "Force": 300, "SpreadBase": "0.200000003,0.200000003", "SpreadGrowth": "1.5,1.5", "SpreadRecovery": 0.2}, "CanCancelReload": false, "ClipMaxSize": 1, "DeployTime": 0.2, "DisplayName": "HL2 Stunstick", "DroppedGameObject": {"ref": "standalone_wm"}, "Handedness": "Right", "HoldType": "melee", "PreferredSlot": 0, "PrimaryAutomatic": false, "PrimaryDelay": 0.8, "ReloadTime": 1.7, "SecondaryDelay": 1, "StartingAmmo": 12, "UsableByNpcs": false, "Value": 10, "ViewModelPrefab": {"prefab": "entities/parts/stunstick_vm.prefab"}, "WorldModelPrefab": {"prefab": "entities/parts/stunstick_wm.prefab"}, "Damage": 15, "Range": 72, "SwingDelay": 0.6, "MissSwingDelay": 0.6}
				},
				"standalone_wm": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_stunbaton.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "ak47_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_rif_ak47.mdl", "FallbackModelPath": "models/weapons/v_rif_ak47.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "aug_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_rif_aug.mdl", "FallbackModelPath": "models/weapons/v_rif_aug.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "awp_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_snip_awp.mdl", "FallbackModelPath": "models/weapons/v_snip_awp.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "deagle_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_pist_deagle.mdl", "FallbackModelPath": "models/weapons/v_pist_deagle.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "elite_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_pist_elite.mdl", "FallbackModelPath": "models/weapons/v_pist_elite.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "famas_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_rif_famas.mdl", "FallbackModelPath": "models/weapons/v_rif_famas.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "fiveseven_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_pist_fiveseven.mdl", "FallbackModelPath": "models/weapons/v_pist_fiveseven.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "flashbang_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_eq_flashbang.mdl", "FallbackModelPath": "models/weapons/v_eq_flashbang.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "g3sg1_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_snip_g3sg1.mdl", "FallbackModelPath": "models/weapons/v_snip_g3sg1.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "galil_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_rif_galil.mdl", "FallbackModelPath": "models/weapons/v_rif_galil.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "glock_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_pist_glock18.mdl", "FallbackModelPath": "models/weapons/v_pist_glock18.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "hegrenade_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_eq_fraggrenade.mdl", "FallbackModelPath": "models/weapons/v_eq_fraggrenade.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "knife_vm", null, null, null, """
			{
				".": {
					"Source1.SourceViewModel": {"Fire": "midslash1", "SecondaryFire": "stab"}
				},
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_knife_t.mdl", "FallbackModelPath": "models/weapons/v_knife_t.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "m249_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_mach_m249para.mdl", "FallbackModelPath": "models/weapons/v_mach_m249para.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "m3_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_shot_m3super90.mdl", "FallbackModelPath": "models/weapons/v_shot_m3super90.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "m4a1_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_rif_m4a1.mdl", "FallbackModelPath": "models/weapons/v_rif_m4a1.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "mac10_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_smg_mac10.mdl", "FallbackModelPath": "models/weapons/v_smg_mac10.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "mp5navy_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_smg_mp5.mdl", "FallbackModelPath": "models/weapons/v_smg_mp5.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "p228_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_pist_p228.mdl", "FallbackModelPath": "models/weapons/v_pist_p228.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "p90_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_smg_p90.mdl", "FallbackModelPath": "models/weapons/v_smg_p90.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "scout_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_snip_scout.mdl", "FallbackModelPath": "models/weapons/v_snip_scout.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "sg550_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_snip_sg550.mdl", "FallbackModelPath": "models/weapons/v_snip_sg550.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "sg552_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_rif_sg552.mdl", "FallbackModelPath": "models/weapons/v_rif_sg552.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "smokegrenade_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_eq_smokegrenade.mdl", "FallbackModelPath": "models/weapons/v_eq_smokegrenade.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "tmp_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_smg_tmp.mdl", "FallbackModelPath": "models/weapons/v_smg_tmp.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "ump45_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_smg_ump45.mdl", "FallbackModelPath": "models/weapons/v_smg_ump45.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "usp_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_pist_usp.mdl", "FallbackModelPath": "models/weapons/v_pist_usp.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.ViewModel, "xm1014_vm", null, null, null, """
			{
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/cstrike/c_shot_xm1014.mdl", "FallbackModelPath": "models/weapons/v_shot_xm1014.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.ViewModel, "357_vm", null, null, null, """
			{
				".": {
					"Source1.SourceViewModel": {"MuzzleEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_muzzleflash.prefab"}, "TracerEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_tracer.prefab"}, "Fire": "fire"}
				},
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/c_357.mdl", "FallbackModelPath": "models/weapons/v_357.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.ViewModel, "ar2_vm", null, null, null, """
			{
				".": {
					"Source1.SourceViewModel": {"MuzzleEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_muzzleflash.prefab"}, "TracerEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_tracer.prefab"}, "Idle": "IR_idle", "Draw": "IR_draw", "Fire": "IR_fire", "Reload": "IR_reload"}
				},
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/c_irifle.mdl", "FallbackModelPath": "models/weapons/v_irifle.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.ViewModel, "bugbait_vm", null, null, null, """
			{
				".": {
					"Source1.SourceViewModel": {"Fire": "throw", "Reload": "draw", "EjectBrass": null}
				},
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/c_bugbait.mdl", "FallbackModelPath": "models/weapons/v_bugbait.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.ViewModel, "crossbow_vm", null, null, null, """
			{
				".": {
					"Source1.SourceViewModel": {"Idle": "idle", "Fire": "fire", "EjectBrass": null}
				},
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/c_crossbow.mdl", "FallbackModelPath": "models/weapons/v_crossbow.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.ViewModel, "crowbar_vm", null, null, null, """
			{
				".": {
					"Source1.SourceViewModel": {"Fire": "misscenter1", "Reload": null, "EjectBrass": null}
				},
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/c_crowbar.mdl", "FallbackModelPath": "models/weapons/v_crowbar.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.ViewModel, "gravity_gun_vm", null, null, null, """
			{
				".": {
					"Source1.SourceViewModel": {"Idle": "idle", "Fire": "fire", "Reload": null, "EjectBrass": null}
				},
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/c_physcannon.mdl", "FallbackModelPath": "models/weapons/v_physcannon.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.ViewModel, "grenade_vm", null, null, null, """
			{
				".": {
					"Source1.SourceViewModel": {"Fire": "throw", "Reload": "draw", "EjectBrass": null}
				},
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/c_grenade.mdl", "FallbackModelPath": "models/weapons/v_grenade.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.ViewModel, "pistol_vm", null, null, null, """
			{
				".": {
					"Source1.SourceViewModel": {"MuzzleEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_muzzleflash.prefab"}, "TracerEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_tracer.prefab"}, "Fire": "fire", "DryFire": "fireempty"}
				},
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/c_pistol.mdl", "FallbackModelPath": "models/weapons/v_pistol.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.ViewModel, "rpg_vm", null, null, null, """
			{
				".": {
					"Source1.SourceViewModel": {"Idle": "idle1", "Fire": "fire", "EjectBrass": null}
				},
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/c_rpg.mdl", "FallbackModelPath": "models/weapons/v_rpg.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.ViewModel, "shotgun_vm", null, null, null, """
			{
				".": {
					"Source1.SourceViewModel": {"MuzzleEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_muzzleflash.prefab"}, "TracerEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_tracer.prefab"}, "Reload": "reload1", "ReloadInsert": "reload2", "ReloadEnd": "reload3", "Pump": "pump"}
				},
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/c_shotgun.mdl", "FallbackModelPath": "models/weapons/v_shotgun.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.ViewModel, "smg_vm", null, null, null, """
			{
				".": {
					"Source1.SourceViewModel": {"MuzzleEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_muzzleflash.prefab"}, "TracerEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_tracer.prefab"}}
				},
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/c_smg1.mdl", "FallbackModelPath": "models/weapons/v_smg1.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.ViewModel, "stunstick_vm", null, null, null, """
			{
				".": {
					"Source1.SourceViewModel": {"Fire": "misscenter1", "Reload": null, "EjectBrass": null}
				},
				"Viewmodel": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/c_stunstick.mdl", "FallbackModelPath": "models/weapons/v_stunstick.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "ak47_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_rif_ak47.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "aug_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_rif_aug.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "awp_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_snip_awp.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "deagle_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_pist_deagle.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "elite_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_pist_elite.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "famas_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_rif_famas.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "fiveseven_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_pist_fiveseven.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "flashbang_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_eq_flashbang.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "g3sg1_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_snip_g3sg1.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "galil_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_rif_galil.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "glock_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_pist_glock18.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "hegrenade_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_eq_fraggrenade.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "knife_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_knife_t.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "m249_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_mach_m249para.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "m3_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_shot_m3super90.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "m4a1_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_rif_m4a1.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "mac10_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_smg_mac10.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "mp5navy_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_smg_mp5.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "p228_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_pist_p228.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "p90_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_smg_p90.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "scout_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_snip_scout.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "sg550_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_snip_sg550.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "sg552_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_rif_sg552.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "smokegrenade_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_eq_smokegrenade.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "tmp_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_smg_tmp.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "ump45_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_smg_ump45.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "usp_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_pist_usp.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.WorldModel, "xm1014_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_shot_xm1014.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.WorldModel, "357_wm", null, null, null, """
			{
				".": {
					"Source1.SourceWeaponWorldModel": {"MuzzleEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_muzzleflash.prefab"}, "TracerEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_tracer.prefab"}}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_357.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.WorldModel, "ar2_wm", null, null, null, """
			{
				".": {
					"Source1.SourceWeaponWorldModel": {"MuzzleEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_muzzleflash.prefab"}, "TracerEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_tracer.prefab"}}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_irifle.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.WorldModel, "bugbait_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_bugbait.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.WorldModel, "crossbow_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_crossbow.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.WorldModel, "crowbar_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_crowbar.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.WorldModel, "gravity_gun_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_physics.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.WorldModel, "grenade_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_grenade.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.WorldModel, "pistol_wm", null, null, null, """
			{
				".": {
					"Source1.SourceWeaponWorldModel": {"MuzzleEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_muzzleflash.prefab"}, "TracerEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_tracer.prefab"}}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_pistol.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.WorldModel, "rpg_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_rocket_launcher.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.WorldModel, "shotgun_wm", null, null, null, """
			{
				".": {
					"Source1.SourceWeaponWorldModel": {"MuzzleEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_muzzleflash.prefab"}, "TracerEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_tracer.prefab"}}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_shotgun.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.WorldModel, "smg_wm", null, null, null, """
			{
				".": {
					"Source1.SourceWeaponWorldModel": {"MuzzleEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_muzzleflash.prefab"}, "TracerEffect": {"_type": "gameobject", "prefab": "prefabs/effects/default_tracer.prefab"}}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_smg1.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.WorldModel, "stunstick_wm", null, null, null, """
			{
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_stunbaton.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Npc, "counter_terrorist", "Counter-Terrorist", "models/player/ct_urban.mdl", "Idle_Upper_M4", """
			{
				".": {
					"Sandbox.NavMeshAgent": {"Radius": 16},
					"Sandbox.Npcs.Layers.NavigationLayer": {"RunSpeed": 180, "WalkSpeed": 85},
					"Sandbox.Npcs.Layers.SensesLayer": {"SightRange": 8192},
					"Source1.SourceNpcDefaults": null,
					"Source1.SourceCreatureNpc": null,
					"Source1.SourceNpcSpawnChoice": {"ModelChoices": ["models/player/ct_gign.mdl", "models/player/ct_gsg9.mdl", "models/player/ct_sas.mdl", "models/player/ct_urban.mdl"], "Team": "CT"},
					"Source1.SourceCombatNpc": {"DisplayName": "Counter-Terrorist", "Friendly": true, "PatrolRadius": 2048, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "SearchTimeout": 20, "Weapon": {"ref": "weapon:Source1.SourceBulletWeapon"}, "WeaponChoices": [{"prefab": "entities/weapons/glock.prefab"}, {"prefab": "entities/weapons/usp.prefab"}, {"prefab": "entities/weapons/p228.prefab"}, {"prefab": "entities/weapons/deagle.prefab"}, {"prefab": "entities/weapons/fiveseven.prefab"}, {"prefab": "entities/weapons/elite.prefab"}, {"prefab": "entities/weapons/mac10.prefab"}, {"prefab": "entities/weapons/tmp.prefab"}, {"prefab": "entities/weapons/mp5navy.prefab"}, {"prefab": "entities/weapons/ump45.prefab"}, {"prefab": "entities/weapons/p90.prefab"}, {"prefab": "entities/weapons/galil.prefab"}, {"prefab": "entities/weapons/famas.prefab"}, {"prefab": "entities/weapons/ak47.prefab"}, {"prefab": "entities/weapons/m4a1.prefab"}, {"prefab": "entities/weapons/sg552.prefab"}, {"prefab": "entities/weapons/aug.prefab"}, {"prefab": "entities/weapons/scout.prefab"}, {"prefab": "entities/weapons/awp.prefab"}, {"prefab": "entities/weapons/g3sg1.prefab"}, {"prefab": "entities/weapons/sg550.prefab"}, {"prefab": "entities/weapons/m3.prefab"}, {"prefab": "entities/weapons/xm1014.prefab"}, {"prefab": "entities/weapons/m249.prefab"}]},
					"Source1.SourceNpcAnimator": {},
					"Source1.CssPlayerSounds": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/player/ct_gign.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "32,32,72"}
				},
				"weapon": {
					"@": {"Prefab": "entities/weapons/m4a1.prefab"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Npc, "terrorist", "Terrorist", "models/player/t_leet.mdl", "Idle_Upper_AK", """
			{
				".": {
					"Sandbox.NavMeshAgent": {"Radius": 16},
					"Sandbox.Npcs.Layers.NavigationLayer": {"RunSpeed": 180, "WalkSpeed": 85},
					"Sandbox.Npcs.Layers.SensesLayer": {"SightRange": 8192},
					"Source1.SourceNpcDefaults": null,
					"Source1.SourceCreatureNpc": null,
					"Source1.SourceNpcSpawnChoice": {"ModelChoices": ["models/player/t_arctic.mdl", "models/player/t_guerilla.mdl", "models/player/t_leet.mdl", "models/player/t_phoenix.mdl"], "Team": "TERRORIST"},
					"Source1.SourceCombatNpc": {"DisplayName": "Terrorist", "PatrolRadius": 2048, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "SearchTimeout": 20, "Weapon": {"ref": "weapon:Source1.SourceBulletWeapon"}, "WeaponChoices": [{"prefab": "entities/weapons/glock.prefab"}, {"prefab": "entities/weapons/usp.prefab"}, {"prefab": "entities/weapons/p228.prefab"}, {"prefab": "entities/weapons/deagle.prefab"}, {"prefab": "entities/weapons/fiveseven.prefab"}, {"prefab": "entities/weapons/elite.prefab"}, {"prefab": "entities/weapons/mac10.prefab"}, {"prefab": "entities/weapons/tmp.prefab"}, {"prefab": "entities/weapons/mp5navy.prefab"}, {"prefab": "entities/weapons/ump45.prefab"}, {"prefab": "entities/weapons/p90.prefab"}, {"prefab": "entities/weapons/galil.prefab"}, {"prefab": "entities/weapons/famas.prefab"}, {"prefab": "entities/weapons/ak47.prefab"}, {"prefab": "entities/weapons/m4a1.prefab"}, {"prefab": "entities/weapons/sg552.prefab"}, {"prefab": "entities/weapons/aug.prefab"}, {"prefab": "entities/weapons/scout.prefab"}, {"prefab": "entities/weapons/awp.prefab"}, {"prefab": "entities/weapons/g3sg1.prefab"}, {"prefab": "entities/weapons/sg550.prefab"}, {"prefab": "entities/weapons/m3.prefab"}, {"prefab": "entities/weapons/xm1014.prefab"}, {"prefab": "entities/weapons/m249.prefab"}]},
					"Source1.SourceNpcAnimator": {},
					"Source1.CssPlayerSounds": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/player/t_arctic.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "32,32,72"}
				},
				"weapon": {
					"@": {"Prefab": "entities/weapons/ak47.prefab"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "alyx", "Alyx", "models/alyx.mdl", "Idle_subtle", """
			{
				".": {
					"Sandbox.Npcs.Layers.NavigationLayer": {"RunSpeed": 180, "WalkSpeed": 85},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_alyx_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.SourceCombatNpc": {"DisplayName": "Alyx", "Friendly": true, "PatrolRadius": 2048, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "SearchTimeout": 20, "Weapon": {"ref": "weapon:Source1.SourceBulletWeapon"}, "Voice": "NPC_Alyx", "EyeHeight": 57.6},
					"Source1.SourceNpcAnimator": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/alyx.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				},
				"weapon": {
					"@": {"Prefab": "entities/weapons/pistol.prefab"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "antlion", "Antlion", "models/antlion.mdl", "Idle", """
			{
				".": {
					"Sandbox.NavMeshAgent": {"Height": 55, "Radius": 25},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_antlion_health", "DamageSetting": "sk_antlion_swipe_damage"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Antlion", "Idle": "Idle", "Move": "run_all", "Attack": "attack1", "MoveSpeed": 180, "AttackDamage": 20, "RespondsToBugbait": true, "AttackHeight": 35, "Voice": "NPC_Antlion", "EyeHeight": 35},
					"Source1.Hl2Unburrow": {},
					"Source1.Hl2AntlionJump": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/antlion.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,27.5", "Scale": "50,50,55"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "antlion_guard", "Antlion Guard", "models/antlion_guard.mdl", "idle", """
			{
				".": {
					"Sandbox.NavMeshAgent": {"Height": 130, "Radius": 45},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_antlionguard_health", "PlayerDamageSetting": "sk_antlionguard_dmg_shove"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Antlion Guard", "Health": 500, "Move": "run1", "Attack": "shove", "MoveSpeed": 220, "AttackDamage": 250, "AttackRange": 140, "AttackHeight": 70, "Voice": "NPC_AntlionGuard", "EyeHeight": 70},
					"Source1.Hl2Unburrow": {"Activity": "ACT_ANTLIONGUARD_UNBURROW", "Sound": "NPC_AntlionGuard.Roar"},
					"Source1.Hl2AntlionGuard": {},
					"Source1.Hl2Charge": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/antlion_guard.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,65", "Scale": "90,80,130"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "antlion_worker", "Antlion Worker", "models/antlion_worker.mdl", "Idle", """
			{
				".": {
					"Sandbox.NavMeshAgent": {"Height": 45, "Radius": 25},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_antlion_worker_health", "AdditionalConfig": "skill_episodic.cfg"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Antlion Worker", "Health": 60, "Idle": "Idle", "Move": "run_all", "Attack": "spit", "MoveSpeed": 140, "AttackRange": 900, "AttackDamage": 5, "AttackInterval": 2, "Voice": "NPC_Antlion", "EyeHeight": 36},
					"Source1.Hl2NpcProjectileAttack": {"FireSound": "NPC_Antlion.PoisonShoot"},
					"Source1.Hl2Unburrow": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/antlion_worker.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,22.5", "Scale": "50,50,45"}
				}
			}
			""" ) { Description = "Half-Life 2: Episode Two" },
		new( "hl2", SourceEntityKind.Npc, "barnacle", "Barnacle", "models/barnacle.mdl", "idle01", """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_barnacle_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.Hl2BarnacleNpc": {"DisplayName": "Barnacle", "Health": 35, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "EyeHeight": 24}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/barnacle.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,15", "Scale": "40,40,30"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "barney", "Barney", "models/barney.mdl", "idle_subtle", """
			{
				".": {
					"Sandbox.Npcs.Layers.NavigationLayer": {"RunSpeed": 180, "WalkSpeed": 85},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_barney_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.SourceCombatNpc": {"DisplayName": "Barney", "Friendly": true, "PatrolRadius": 2048, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "SearchTimeout": 20, "Weapon": {"ref": "weapon:Source1.SourceBulletWeapon"}, "Voice": "NPC_Barney", "EyeHeight": 57.6},
					"Source1.SourceNpcAnimator": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/barney.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				},
				"weapon": {
					"@": {"Prefab": "entities/weapons/smg.prefab"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "breen", "Dr. Breen", "models/breen.mdl", "idle_subtle", """
			{
				".": {
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_breen_health"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Dr. Breen", "Idle": "idle_subtle", "Move": "walk_all", "Attack": "idle_subtle", "Passive": true, "MoveSpeed": 85, "EyeHeight": 57.6}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/breen.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "citizen", "Citizen", "models/humans/group01/male_07.mdl", "idle_subtle", """
			{
				".": {
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_citizen_health"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Citizen", "Idle": "idle_subtle", "Move": "walk_all", "Attack": "idle_subtle", "Passive": true, "MoveSpeed": 85, "EyeHeight": 57.6}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/humans/group01/male_07.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "combine_elite", "Combine Elite", "models/combine_super_soldier.mdl", "Idle1", """
			{
				".": {
					"Sandbox.Npcs.Layers.NavigationLayer": {"RunSpeed": 180, "WalkSpeed": 85},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_combine_guard_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.SourceCombatNpc": {"DisplayName": "Combine Elite", "PatrolRadius": 2048, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "SearchTimeout": 20, "Weapon": {"ref": "weapon:Source1.SourceBulletWeapon"}, "Elite": true, "Grenades": 5, "EyeHeight": 57.6},
					"Source1.SourceNpcAnimator": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/combine_super_soldier.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				},
				"weapon": {
					"@": {"Prefab": "entities/weapons/ar2.prefab"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "combine_soldier", "Combine Soldier", "models/combine_soldier.mdl", "Idle1", """
			{
				".": {
					"Sandbox.NavMeshAgent": {"Acceleration": 150, "Height": 64, "Radius": 16},
					"Sandbox.Npcs.Layers.NavigationLayer": {"RunSpeed": 180, "WalkSpeed": 85},
					"Sandbox.Npcs.Layers.SensesLayer": {"FieldOfView": 200, "SightRange": 8192},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_combine_s_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.SourceCombatNpc": {"DisplayName": "Combine Soldier", "Health": 50, "PatrolRadius": 2048, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "SearchTimeout": 20, "Weapon": {"ref": "weapon:Source1.SourceBulletWeapon"}, "Grenades": 5},
					"Source1.SourceNpcAnimator": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/combine_soldier.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,37.5", "Scale": "25,25,75"}
				},
				"weapon": {
					"@": {"Prefab": "entities/weapons/smg.prefab"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "crow", "Crow", "models/crow.mdl", "Idle01", """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_crow_health"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Crow", "Health": 5, "Idle": "Idle01", "Move": "Fly01", "Attack": "Idle01", "Passive": true, "Locomotion": "Flying", "Voice": "NPC_Crow", "EyeHeight": 14.4},
					"Source1.Hl2Bird": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/crow.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Scale": "15,15,18"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "dog", "Dog", "models/dog.mdl", "idle01", """
			{
				".": {
					"Sandbox.NavMeshAgent": {"Height": 105, "Radius": 30},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_dog_health"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Dog", "Friendly": true, "Health": 500, "Idle": "idle01", "Move": "run_all", "Attack": "pound", "AttackDamage": 80, "MoveSpeed": 220, "AttackRange": 130, "AttackHeight": 65, "Voice": "NPC_Dog", "EyeHeight": 65}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/dog.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,52.5", "Scale": "60,60,105"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "dropship", "Combine Dropship", "models/combine_dropship.mdl", "idle", """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Sandbox.Npcs.Layers.SensesLayer": {"SightRange": 2860},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_dropship_health", "DamageSetting": "sk_npc_dmg_dropship"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Combine Dropship", "Health": 1000, "Move": "hover", "Attack": "idle", "Locomotion": "Flying", "HatesCombine": false, "FlightHeight": 300, "MoveSpeed": 180, "AttackRange": 2200, "AttackDamage": 10, "AttackInterval": 0.3, "AttackDelay": 0, "Voice": "NPC_CombineDropship", "BlastHits": 5},
					"Source1.Hl2DeployPrefab": {"Prefab": "combine_soldier", "Range": 1600, "Count": 3}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/combine_dropship.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Scale": "400,250,180"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "eli", "Eli Vance", "models/eli.mdl", "idle_subtle", """
			{
				".": {
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_eli_health"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Eli Vance", "Idle": "idle_subtle", "Move": "walk_all", "Attack": "idle_subtle", "Passive": true, "MoveSpeed": 85, "EyeHeight": 57.6}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/eli.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "fast_headcrab", "Fast Headcrab", "models/headcrab.mdl", "Idle01", """
			{
				".": {
					"Sandbox.NavMeshAgent": {"Height": 20, "Radius": 12.5},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_headcrab_fast_health", "DamageSetting": "sk_headcrab_melee_dmg"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Fast Headcrab", "Idle": "Idle01", "Move": "Run1", "MoveSpeed": 210, "AttackDamage": 10, "AttackHeight": 15, "AttackDelay": 0.1, "AttackInterval": 0.7, "Voice": "NPC_FastHeadcrab", "EyeHeight": 15},
					"Source1.Hl2Leap": {"SoundPrefix": "NPC_FastHeadcrab"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/headcrab.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,10", "Scale": "25,25,20"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "fast_zombie", "Fast Zombie", "models/zombie/fast.mdl", "idle", """
			{
				".": {
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_zombie_health", "DamageSetting": "sk_zombie_dmg_one_slash"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Fast Zombie", "Move": "Run", "Attack": "Melee", "MoveSpeed": 220, "AttackInterval": 0.8, "AttackDelay": 0.2, "Voice": "NPC_FastZombie", "EyeHeight": 57.6},
					"Source1.Hl2Leap": {"SoundPrefix": "NPC_FastZombie", "LeapSound": "NPC_FastZombie.LeapAttack"},
					"Source1.Hl2Zombie": {"Headcrab": "fast_headcrab"}
				},
				"model": {
					"Sandbox.SkinnedModelRenderer": {"BodyGroups": 5},
					"Source1.SourceMountedModel": {"ModelPath": "models/zombie/fast.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "gman", "G-Man", "models/gman.mdl", "idle_subtle", """
			{
				".": {
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_gman_health"},
					"Source1.SourceCreatureNpc": {"DisplayName": "G-Man", "Idle": "idle_subtle", "Move": "walk_all", "Attack": "idle_subtle", "Passive": true, "MoveSpeed": 85, "EyeHeight": 57.6}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/gman.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "grigori", "Father Grigori", "models/monk.mdl", "Idle01", """
			{
				".": {
					"Sandbox.Npcs.Layers.NavigationLayer": {"RunSpeed": 180, "WalkSpeed": 85},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_grigori_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.SourceCombatNpc": {"DisplayName": "Father Grigori", "Friendly": true, "PatrolRadius": 2048, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "SearchTimeout": 20, "Weapon": {"ref": "weapon:Source1.SourceBulletWeapon"}, "EyeHeight": 57.6},
					"Source1.SourceNpcAnimator": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/monk.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				},
				"weapon": {
					"@": {"Prefab": "entities/weapons/shotgun.prefab"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "gunship", "Combine Gunship", "models/gunship.mdl", "idle", """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Sandbox.Npcs.Layers.SensesLayer": {"SightRange": 3250},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_gunship_health", "DamageSetting": "sk_npc_dmg_gunship", "PlayerDamageSetting": "sk_npc_dmg_gunship_to_plr"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Combine Gunship", "Health": 1000, "Move": "idle", "Attack": "idle", "Locomotion": "Flying", "HatesCombine": false, "FlightHeight": 350, "MoveSpeed": 220, "AttackRange": 2500, "AttackInterval": 0.2, "AttackDelay": 0, "Voice": "NPC_CombineGunship", "BlastHits": 5},
					"Source1.SourceNpcRangedEffects": {"FireSound": "NPC_CombineGunship.CannonSound", "ContinuousSound": true}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/gunship.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Scale": "400,200,100"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "headcrab", "Headcrab", "models/headcrabclassic.mdl", "Idle01", """
			{
				".": {
					"Sandbox.NavMeshAgent": {"Height": 16, "Radius": 12},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_headcrab_health", "DamageSetting": "sk_headcrab_melee_dmg"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Headcrab", "Idle": "Idle01", "Move": "Run1", "Attack": "jumpattack_broadcast", "MoveSpeed": 110, "AttackDamage": 10, "AttackHeight": 12, "AttackDelay": 0.2, "AttackRange": 60, "Voice": "NPC_Headcrab", "EyeHeight": 12},
					"Source1.Hl2Leap": {"SoundPrefix": "NPC_Headcrab"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/headcrabclassic.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,8", "Scale": "24,24,16"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "helicopter", "Hunter-Chopper", "models/combine_helicopter.mdl", "idle", """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Sandbox.Npcs.Layers.SensesLayer": {"SightRange": 3900},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_helicopter_health", "DamageSetting": "sk_npc_dmg_helicopter", "PlayerDamageSetting": "sk_npc_dmg_helicopter_to_plr"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Hunter-Chopper", "Health": 1200, "Move": "idle", "Attack": "idle", "Locomotion": "Flying", "HatesCombine": false, "FlightHeight": 400, "MoveSpeed": 250, "AttackRange": 3000, "AttackDamage": 12, "AttackInterval": 0.15, "AttackDelay": 0, "Voice": "NPC_AttackHelicopter", "BlastHits": 5},
					"Source1.SourceNpcRangedEffects": {"MuzzleAttachment": "Muzzle", "FireSound": "NPC_AttackHelicopter.FireGun", "ContinuousSound": true}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/combine_helicopter.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Scale": "400,180,180"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "hopper_mine", "Hopper Mine", "models/props_combine/combine_mine01.mdl", null, """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Sandbox.Rigidbody": {"Locking": {"X": false, "Y": false, "Z": false, "Pitch": false, "Yaw": false, "Roll": false}, "MotionEnabled": true},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_hopper_mine_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.Hl2HopperMine": {"DisplayName": "Hopper Mine", "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "EyeHeight": 22.400000000000002},
					"Sandbox.ModelCollider": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/props_combine/combine_mine01.mdl", "Collider": {"ref": ".:Sandbox.ModelCollider"}}
				},
				"collider": {
					"Sandbox.BoxCollider": null
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "hunter", "Hunter", "models/hunter.mdl", "idle1", """
			{
				".": {
					"Sandbox.NavMeshAgent": {"Height": 110, "Radius": 32.5},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_hunter_health", "AdditionalConfig": "skill_episodic.cfg"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Hunter", "Health": 210, "Idle": "idle1", "Move": "canter_all", "Attack": "shoot_unplanted", "HatesCombine": false, "MoveSpeed": 180, "AttackRange": 1500, "AttackDamage": 20, "AttackInterval": 2.5, "StandOff": true, "Voice": "NPC_Hunter"},
					"Source1.Hl2NpcProjectileAttack": {"Kind": "Flechette", "Attachment": "top_eye", "AlternateAttachment": "bottom_eye", "FireSound": "NPC_Hunter.FlechetteShoot"},
					"Source1.Hl2Charge": {"MaxRange": 1024, "Delay": 10, "RandomDelay": 5, "Chance": 0.25, "DamageSetting": "sk_hunter_dmg_charge", "HitSound": "NPC_Hunter.ChargeHitEnemy", "CrashSound": "NPC_Hunter.ChargeHitWorld"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/hunter.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,55", "Scale": "65,65,110"}
				}
			}
			""" ) { Description = "Half-Life 2: Episode Two" },
		new( "hl2", SourceEntityKind.Npc, "ichthyosaur", "Ichthyosaur", "models/ichthyosaur.mdl", "idle", """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_ichthyosaur_health", "DamageSetting": "sk_ichthyosaur_melee_dmg"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Ichthyosaur", "Health": 200, "Move": "swim", "Attack": "attackstart", "Locomotion": "Flying", "FlightHeight": 0, "MoveSpeed": 180, "AttackRange": 140, "AttackDamage": 40, "Aquatic": true, "Voice": "NPC_Ichthyosaur", "EyeHeight": 48}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/ichthyosaur.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Scale": "160,60,60"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "kleiner", "Dr. Kleiner", "models/kleiner.mdl", "idle_subtle", """
			{
				".": {
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_kleiner_health"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Dr. Kleiner", "Idle": "idle_subtle", "Move": "walk_all", "Attack": "idle_subtle", "Passive": true, "MoveSpeed": 85, "EyeHeight": 57.6}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/kleiner.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "leech", "Leech", "models/leech.mdl", "loop1", """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_leech_health"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Leech", "Health": 5, "Idle": "loop1", "Move": "loop2", "Attack": "attackloop1", "Locomotion": "Flying", "FlightHeight": 0, "AttackHeight": 0, "MoveSpeed": 70, "AttackRange": 35, "AttackDamage": 2, "Aquatic": true, "EyeHeight": 0}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/leech.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Scale": "12,12,8"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "manhack", "Manhack", "models/manhack.mdl", "idle", """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_manhack_health", "DamageSetting": "sk_manhack_melee_dmg"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Manhack", "Health": 25, "Move": "fly", "Attack": "fly2", "Locomotion": "Flying", "HatesCombine": false, "MoveSpeed": 180, "AttackHeight": 40, "AttackDamage": 20, "Voice": "NPC_Manhack", "EyeHeight": 40},
					"Source1.Hl2Manhack": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/manhack.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Scale": "24,24,24"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "metrocop", "Metrocop", "models/police.mdl", "pistolidle1", """
			{
				".": {
					"Sandbox.Npcs.Layers.NavigationLayer": {"RunSpeed": 180, "WalkSpeed": 85},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_metropolice_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.SourceCombatNpc": {"DisplayName": "Metrocop", "Health": 40, "PatrolRadius": 2048, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "SearchTimeout": 20, "Weapon": {"ref": "weapon:Source1.SourceBulletWeapon"}, "Manhacks": 1, "Voice": "NPC_MetroPolice", "EyeHeight": 57.6},
					"Source1.SourceNpcAnimator": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/police.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				},
				"weapon": {
					"@": {"Prefab": "entities/weapons/pistol.prefab"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "metrocop_stunstick", "Metrocop (Stunstick)", "models/police.mdl", "batonangryidle1", """
			{
				".": {
					"Sandbox.Npcs.Layers.NavigationLayer": {"RunSpeed": 180, "WalkSpeed": 85},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_metropolice_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.SourceCombatNpc": {"DisplayName": "Metrocop (Stunstick)", "Health": 40, "PatrolRadius": 2048, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "SearchTimeout": 20, "Weapon": {"ref": "weapon:Source1.Hl2Stunstick"}, "Manhacks": 1, "Voice": "NPC_MetroPolice", "EyeHeight": 57.6},
					"Source1.SourceNpcAnimator": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/police.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				},
				"weapon": {
					"@": {"Prefab": "entities/weapons/stunstick.prefab"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "mossman", "Dr. Mossman", "models/mossman.mdl", "idle_subtle", """
			{
				".": {
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_mossman_health"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Dr. Mossman", "Idle": "idle_subtle", "Move": "walk_all", "Attack": "idle_subtle", "Passive": true, "MoveSpeed": 85, "EyeHeight": 57.6}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/mossman.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "odessa", "Odessa Cubbage", "models/odessa.mdl", "idle_subtle", """
			{
				".": {
					"Sandbox.Npcs.Layers.NavigationLayer": {"RunSpeed": 180, "WalkSpeed": 85},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_odessa_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.SourceCombatNpc": {"DisplayName": "Odessa Cubbage", "Friendly": true, "PatrolRadius": 2048, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "SearchTimeout": 20, "Weapon": {"ref": "weapon:Source1.SourceBulletWeapon"}, "EyeHeight": 57.6},
					"Source1.SourceNpcAnimator": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/odessa.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				},
				"weapon": {
					"@": {"Prefab": "entities/weapons/smg.prefab"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "pigeon", "Pigeon", "models/pigeon.mdl", "Idle01", """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_pigeon_health"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Pigeon", "Health": 5, "Idle": "Idle01", "Move": "Fly01", "Attack": "Idle01", "Passive": true, "Locomotion": "Flying", "Voice": "NPC_Pigeon", "EyeHeight": 14.4},
					"Source1.Hl2Bird": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/pigeon.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Scale": "15,15,18"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "poison_headcrab", "Poison Headcrab", "models/headcrabblack.mdl", "Idle01", """
			{
				".": {
					"Sandbox.NavMeshAgent": {"Height": 22, "Radius": 14},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_headcrab_poison_health", "DamageSetting": "sk_headcrab_melee_dmg"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Poison Headcrab", "Idle": "Idle01", "Move": "Run1", "Attack": "Spitattack", "MoveSpeed": 100, "AttackDamage": 30, "AttackHeight": 16, "AttackInterval": 2, "Voice": "NPC_BlackHeadcrab", "EyeHeight": 16},
					"Source1.Hl2Leap": {"SoundPrefix": "NPC_BlackHeadcrab"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/headcrabblack.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,11", "Scale": "28,28,22"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "poison_zombie", "Poison Zombie", "models/zombie/poison.mdl", "Idle01", """
			{
				".": {
					"Sandbox.NavMeshAgent": {"Height": 70, "Radius": 21},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_zombie_poison_health", "DamageSetting": "sk_zombie_dmg_one_slash"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Poison Zombie", "Idle": "Idle01", "Move": "Walk", "Attack": "Throw", "MoveSpeed": 65, "AttackDamage": 25, "AttackRange": 70, "AttackInterval": 1.5, "AttackDelay": 0.8, "Voice": "NPC_PoisonZombie", "EyeHeight": 56},
					"Source1.Hl2Zombie": {"Headcrab": "poison_headcrab", "ThrowsCrabs": true}
				},
				"model": {
					"Sandbox.SkinnedModelRenderer": {"BodyGroups": 341},
					"Source1.SourceMountedModel": {"ModelPath": "models/zombie/poison.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,35", "Scale": "42,42,70"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "prison_guard", "Prison Guard", "models/combine_soldier_prisonguard.mdl", "Idle1_SMG1", """
			{
				".": {
					"Sandbox.Npcs.Layers.NavigationLayer": {"RunSpeed": 180, "WalkSpeed": 85},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_combine_s_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.SourceCombatNpc": {"DisplayName": "Prison Guard", "Health": 60, "PatrolRadius": 2048, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "SearchTimeout": 20, "Weapon": {"ref": "weapon:Source1.SourceBulletWeapon"}, "Grenades": 5, "EyeHeight": 57.6},
					"Source1.SourceNpcAnimator": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/combine_soldier_prisonguard.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				},
				"weapon": {
					"@": {"Prefab": "entities/weapons/shotgun.prefab"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "rollermine", "Rollermine", "models/roller.mdl", null, """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Sandbox.Npcs.Layers.SensesLayer": {"HearingRange": 800, "SightRange": 1500},
					"Sandbox.Rigidbody": {"AngularDamping": 1.5, "LinearDamping": 0.3, "Locking": {"Pitch": false, "Roll": false, "X": false, "Y": false, "Yaw": false, "Z": false}, "MotionEnabled": true},
					"Source1.SourceNpcDefaults": null,
					"Source1.SourceCreatureNpc": null,
					"Source1.Hl2Rollermine": {"DisplayName": "Rollermine", "Health": 35, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}},
					"Sandbox.SphereCollider": {"Elasticity": 0.3, "Friction": 0.8, "Radius": 10},
					"Source1.SourceIdleAnimation": {"Clip": "idle"}
				},
				"model": {
					"Sandbox.SkinnedModelRenderer": {"BodyGroups": 1},
					"Source1.SourceMountedModel": {"ModelPath": "models/roller.mdl"}
				},
				"collider": null
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "rebel", "Rebel", "models/humans/group03/male_07.mdl", "idle_subtle", """
			{
				".": {
					"Sandbox.Npcs.Layers.NavigationLayer": {"RunSpeed": 180, "WalkSpeed": 85},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_citizen_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.SourceCombatNpc": {"DisplayName": "Rebel", "Friendly": true, "PatrolRadius": 2048, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "SearchTimeout": 20, "Weapon": {"ref": "weapon:Source1.SourceBulletWeapon"}, "EyeHeight": 57.6},
					"Source1.SourceNpcAnimator": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/humans/group03/male_07.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				},
				"weapon": {
					"@": {"Prefab": "entities/weapons/smg.prefab"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "rebel_medic", "Rebel Medic", "models/humans/group03m/male_07.mdl", "idle_subtle", """
			{
				".": {
					"Sandbox.Npcs.Layers.NavigationLayer": {"RunSpeed": 180, "WalkSpeed": 85},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_citizen_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.SourceCombatNpc": {"DisplayName": "Rebel Medic", "Friendly": true, "PatrolRadius": 2048, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "SearchTimeout": 20, "Weapon": {"ref": "weapon:Source1.SourceBulletWeapon"}, "Medic": true, "EyeHeight": 57.6},
					"Source1.SourceNpcAnimator": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/humans/group03m/male_07.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				},
				"weapon": {
					"@": {"Prefab": "entities/weapons/smg.prefab"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "scanner", "City Scanner", "models/combine_scanner.mdl", "idle", """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_scanner_health"},
					"Source1.SourceCreatureNpc": {"DisplayName": "City Scanner", "Health": 40, "Move": "idle", "Attack": "alert", "Locomotion": "Flying", "HatesCombine": false, "MoveSpeed": 130, "AttackRange": 500, "AttackDamage": 0, "AttackInterval": 3, "Voice": "NPC_CScanner", "EyeHeight": 24},
					"Source1.Hl2ScannerPhoto": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/combine_scanner.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Scale": "35,25,30"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "seagull", "Seagull", "models/seagull.mdl", "Idle01", """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_seagull_health"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Seagull", "Health": 5, "Idle": "Idle01", "Move": "Fly", "Attack": "Idle01", "Passive": true, "Locomotion": "Flying", "Voice": "NPC_Seagull", "EyeHeight": 14.4},
					"Source1.Hl2Bird": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/seagull.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Scale": "15,15,18"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "shield_scanner", "Shield Scanner", "models/shield_scanner.mdl", "Hover1", """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_scanner_health"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Shield Scanner", "Health": 50, "Idle": "Hover1", "Move": "Hover4", "Attack": "Hover6", "Locomotion": "Flying", "HatesCombine": false, "MoveSpeed": 130, "AttackRange": 80, "AttackDamage": 0, "AttackInterval": 2, "Voice": "NPC_SScanner", "EyeHeight": 36},
					"Source1.Hl2DeployPrefab": {"Prefab": "hopper_mine"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/shield_scanner.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Scale": "30,25,45"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "stalker", "Stalker", "models/stalker.mdl", "idle1", """
			{
				".": {
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_stalker_health"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Stalker", "Idle": "idle1", "Attack": "working", "AttackRange": 700, "AttackDamage": 8, "AttackInterval": 0.3, "AttackDelay": 0.1, "HatesCombine": false, "AttackHeight": 50, "Voice": "NPC_Stalker", "EyeHeight": 50},
					"Source1.SourceNpcRangedEffects": {"MuzzleAttachment": "eyes", "FireSound": "NPC_Stalker.BurnWall", "ContinuousSound": true, "Beam": true, "TracerColor": "1,0.2,0.05,1", "BeamSprite": "sprites/laser", "BeamWidth": 3, "DamageTag": "burn"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/stalker.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "strider", "Strider", "models/combine_strider.mdl", "Idle01", """
			{
				".": {
					"Sandbox.NavMeshAgent": {"Height": 480, "Radius": 50},
					"Sandbox.Npcs.Layers.SensesLayer": {"SightRange": 3120},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_strider_health", "DamageSetting": "sk_npc_dmg_strider", "PlayerDamageSetting": "sk_npc_dmg_strider_to_plr"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Strider", "Health": 1000, "Idle": "Idle01", "Move": "walk_all", "Attack": "Idle01", "HatesCombine": false, "MoveSpeed": 140, "AttackRange": 2400, "AttackInterval": 0.3, "AttackDelay": 0, "AttackHeight": 430, "Voice": "NPC_Strider", "BlastHits": 7, "EyeHeight": 430},
					"Source1.Hl2StriderCannon": {},
					"Source1.Hl2StriderStomp": {},
					"Source1.SourceNpcRangedEffects": {"MuzzleAttachment": "MiniGun", "FireSound": "NPC_Strider.FireMinigun"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/combine_strider.mdl"},
					"@": {"Position": "0,0,480"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,240", "Scale": "100,100,480"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "turret_ceiling", "Ceiling Turret", "models/combine_turrets/ceiling_turret.mdl", null, """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Sandbox.Rigidbody": {"Locking": {"X": false, "Y": false, "Z": false, "Pitch": false, "Yaw": false, "Roll": false}, "Gravity": false},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_turret_ceiling_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.Hl2TurretNpc": {"DisplayName": "Ceiling Turret", "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "EyeHeight": 48},
					"Source1.SourceNpcRangedEffects": {"MuzzleAttachment": "eyes", "FireSound": "NPC_CeilingTurret.Shoot"},
					"Sandbox.ModelCollider": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/combine_turrets/ceiling_turret.mdl", "Collider": {"ref": ".:Sandbox.ModelCollider"}}
				},
				"collider": {
					"Sandbox.BoxCollider": null
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "turret_floor", "Floor Turret", "models/combine_turrets/floor_turret.mdl", null, """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Sandbox.Rigidbody": {"Locking": {"X": false, "Y": false, "Z": false, "Pitch": false, "Yaw": false, "Roll": false}, "MotionEnabled": true},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_turret_floor_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.Hl2TurretNpc": {"DisplayName": "Floor Turret", "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "FloorMounted": true, "EyeHeight": 48},
					"Source1.SourceNpcRangedEffects": {"MuzzleAttachment": "eyes", "FireSound": "NPC_FloorTurret.Shoot"},
					"Sandbox.ModelCollider": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/combine_turrets/floor_turret.mdl", "Collider": {"ref": ".:Sandbox.ModelCollider"}}
				},
				"collider": {
					"Sandbox.BoxCollider": null
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "turret_floor_friendly", "Friendly Floor Turret", "models/combine_turrets/floor_turret.mdl", null, """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Sandbox.Rigidbody": {"Locking": {"X": false, "Y": false, "Z": false, "Pitch": false, "Yaw": false, "Roll": false}, "MotionEnabled": true},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_turret_floor_friendly_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.Hl2TurretNpc": {"DisplayName": "Friendly Floor Turret", "Friendly": true, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "FloorMounted": true, "EyeHeight": 48},
					"Source1.SourceNpcRangedEffects": {"MuzzleAttachment": "eyes", "FireSound": "NPC_FloorTurret.Shoot"},
					"Sandbox.ModelCollider": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/combine_turrets/floor_turret.mdl", "Collider": {"ref": ".:Sandbox.ModelCollider"}}
				},
				"collider": {
					"Sandbox.BoxCollider": null
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "turret_ground", "Ground Turret", "models/combine_turrets/ground_turret.mdl", null, """
			{
				".": {
					"Sandbox.NavMeshAgent": null,
					"Sandbox.Npcs.Layers.NavigationLayer": null,
					"Sandbox.Rigidbody": {"Locking": {"X": false, "Y": false, "Z": false, "Pitch": false, "Yaw": false, "Roll": false}, "Gravity": false},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_turret_ground_health"},
					"Source1.SourceCreatureNpc": null,
					"Source1.Hl2TurretNpc": {"DisplayName": "Ground Turret", "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "TracksWithBody": true, "EyeHeight": 48},
					"Source1.SourceNpcRangedEffects": {"MuzzleAttachment": "eyes", "FireSound": "NPC_CeilingTurret.Shoot"},
					"Sandbox.ModelCollider": {}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/combine_turrets/ground_turret.mdl", "Collider": {"ref": ".:Sandbox.ModelCollider"}}
				},
				"collider": {
					"Sandbox.BoxCollider": null
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "vortigaunt", "Vortigaunt", "models/vortigaunt.mdl", "Idle01", """
			{
				".": {
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_vortigaunt_health", "DamageSetting": "sk_vortigaunt_dmg_zap"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Vortigaunt", "Friendly": true, "Idle": "Idle01", "Move": "Walk_all", "Attack": "zapattack1", "AttackRange": 900, "AttackDamage": 40, "AttackInterval": 2, "AttackDelay": 0.7, "AttackHeight": 50, "Voice": "NPC_Vortigaunt", "EyeHeight": 50},
					"Source1.SourceNpcRangedEffects": {"MuzzleAttachment": "leftclaw", "FireSound": "NPC_Vortigaunt.Shoot", "MuzzleParticle": "vortigaunt_hand_glow", "Beam": true, "TracerColor": "0.7,1,0.4,1", "BeamSprite": "sprites/lgtning", "BeamWidth": 6, "BeamNoise": 16, "DamageTag": "shock", "ChargeParticle": "vortigaunt_hand_glow", "ChargeAttachments": "leftclaw,rightclaw", "ChargeSound": "NPC_Vortigaunt.ZapPowerup"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/vortigaunt.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "zombie", "Zombie", "models/zombie/classic.mdl", "Idle01", """
			{
				".": {
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_zombie_health", "DamageSetting": "sk_zombie_dmg_both_slash"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Zombie", "Idle": "Idle01", "Attack": "attackA", "MoveSpeed": 55, "AttackInterval": 1.9, "AttackDelay": 0.55, "Voice": "Zombie", "EyeHeight": 57.6},
					"Source1.Hl2Zombie": {"Headcrab": "headcrab", "Torso": "zombie_torso", "LegsModel": "models/zombie/classic_legs.mdl"}
				},
				"model": {
					"Sandbox.SkinnedModelRenderer": {"BodyGroups": 5},
					"Source1.SourceMountedModel": {"ModelPath": "models/zombie/classic.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "zombie_torso", "Zombie Torso", "models/zombie/classic_torso.mdl", "Idle01", """
			{
				".": {
					"Sandbox.NavMeshAgent": {"Height": 24},
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_zombie_health", "DamageSetting": "sk_zombie_dmg_one_slash"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Zombie Torso", "Idle": "Idle01", "Move": "crawl", "MoveSpeed": 35, "AttackDamage": 10, "AttackHeight": 15, "Voice": "Zombie", "EyeHeight": 15},
					"Source1.Hl2Zombie": {"Headcrab": "headcrab"}
				},
				"model": {
					"Sandbox.SkinnedModelRenderer": {"BodyGroups": 5},
					"Source1.SourceMountedModel": {"ModelPath": "models/zombie/classic_torso.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,12", "Scale": "26,26,24"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Npc, "zombine", "Zombine", "models/zombie/zombie_soldier.mdl", "Idle01", """
			{
				".": {
					"Source1.SourceNpcDefaults": {"HealthSetting": "sk_zombie_soldier_health", "DamageSetting": "sk_zombie_dmg_both_slash", "AdditionalConfig": "skill_episodic.cfg"},
					"Source1.SourceCreatureNpc": {"DisplayName": "Zombine", "Idle": "Idle01", "Move": "walk_All", "Attack": "AttackA", "MoveSpeed": 80, "AttackRange": 75, "AttackDamage": 20, "Voice": "Zombie", "EyeHeight": 57.6},
					"Source1.Hl2ZombineGrenade": {},
					"Source1.Hl2Zombie": {"Headcrab": "headcrab"}
				},
				"model": {
					"Sandbox.SkinnedModelRenderer": {"BodyGroups": 5},
					"Source1.SourceMountedModel": {"ModelPath": "models/zombie/zombie_soldier.mdl"}
				},
				"collider": {
					"Sandbox.BoxCollider": {"Center": "0,0,36", "Scale": "26,26,72"}
				}
			}
			""" ) { Description = "Half-Life 2: Episode One" },
		new( "hl2", SourceEntityKind.Pickup, "ammo_357", ".357 Ammo", "models/items/357ammo.mdl", null, """
			{
				".": {
					"Source1.Hl2Pickup": {"Amount": 6, "AmmoName": "357"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/items/357ammo.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Pickup, "ammo_ar2", "Pulse Rifle Ammo", "models/items/combine_rifle_cartridge01.mdl", null, """
			{
				".": {
					"Source1.Hl2Pickup": {"Amount": 20, "AmmoName": "ar2"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/items/combine_rifle_cartridge01.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Pickup, "ammo_ar2_altfire", "Pulse Rifle Orb", "models/items/combine_rifle_ammo01.mdl", null, """
			{
				".": {
					"Source1.Hl2Pickup": {"Amount": 1, "AmmoName": "ar2altfire"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/items/combine_rifle_ammo01.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Pickup, "ammo_buckshot", "Shotgun Ammo", "models/items/boxbuckshot.mdl", null, """
			{
				".": {
					"Source1.Hl2Pickup": {"Amount": 20, "AmmoName": "buckshot"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/items/boxbuckshot.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Pickup, "ammo_crossbow", "Crossbow Bolts", "models/items/crossbowrounds.mdl", null, """
			{
				".": {
					"Source1.Hl2Pickup": {"Amount": 6, "AmmoName": "xbowbolt"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/items/crossbowrounds.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Pickup, "ammo_pistol", "Pistol Ammo", "models/items/boxsrounds.mdl", null, """
			{
				".": {
					"Source1.Hl2Pickup": {"Amount": 20, "AmmoName": "pistol"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/items/boxsrounds.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Pickup, "ammo_rpg", "RPG Rocket", "models/weapons/w_missile_closed.mdl", null, """
			{
				".": {
					"Source1.Hl2Pickup": {"Amount": 1, "AmmoName": "rpg_round"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/w_missile_closed.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Pickup, "ammo_smg1", "SMG Ammo", "models/items/boxmrounds.mdl", null, """
			{
				".": {
					"Source1.Hl2Pickup": {"Amount": 45, "AmmoName": "smg1"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/items/boxmrounds.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Pickup, "ammo_smg1_grenade", "SMG Grenade", "models/weapons/ar2_grenade.mdl", null, """
			{
				".": {
					"Source1.Hl2Pickup": {"Amount": 1, "AmmoName": "smg1_grenade"}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/weapons/ar2_grenade.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Pickup, "battery", "Suit Battery", "models/items/battery.mdl", null, """
			{
				".": {
					"Source1.Hl2Pickup": {"Kind": "Armour", "SoundName": "ItemBattery.Touch", "AmountSetting": "sk_battery", "Amount": 15}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/items/battery.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Pickup, "healthkit", "Health Kit", "models/items/healthkit.mdl", null, """
			{
				".": {
					"Source1.Hl2Pickup": {"Kind": "Health", "SoundName": "HealthKit.Touch", "AmountSetting": "sk_healthkit", "Amount": 25}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/items/healthkit.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Pickup, "healthvial", "Health Vial", "models/healthvial.mdl", null, """
			{
				".": {
					"Source1.Hl2Pickup": {"Kind": "Health", "SoundName": "HealthVial.Touch", "AmountSetting": "sk_healthvial", "Amount": 10}
				},
				"model": {
					"Source1.SourceMountedModel": {"ModelPath": "models/healthvial.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Charger, "health_charger", "Health Charger", "models/props_combine/health_charger001.mdl", null, """
			{
				".": {
					"Source1.Hl2Charger": {"CapacitySetting": "sk_healthcharger", "StartSound": "WallHealth.Start", "LoopSound": "WallHealth.LoopingContinueCharge", "DenySound": "WallHealth.Deny"},
					"Source1.SourceMountedModel": {"ModelPath": "models/props_combine/health_charger001.mdl"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Charger, "suit_charger", "Suit Charger", "models/props_combine/suit_charger001.mdl", null, """
			{
				".": {
					"Source1.Hl2Charger": {"Armour": true, "CapacitySetting": "sk_suitcharger", "StartSound": "SuitRecharge.Start", "LoopSound": "SuitRecharge.ChargingLoop", "DenySound": "SuitRecharge.Deny"},
					"Source1.SourceMountedModel": {"ModelPath": "models/props_combine/suit_charger001.mdl"}
				}
			}
			""" ),
		new( "css", SourceEntityKind.Effect, "explosion", null, null, null, """
			{
				".": {
					"TemporaryEffect": {"DestroyAfterSeconds": 3},
					"Source1.SourceMountedEffect": null,
					"Sandbox.RadiusDamage": {"Radius": 350, "PhysicsForceScale": 1.5},
					"Source1.CssExplosionEffect": {}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Effect, "acid_impact", null, null, null, """
			{
				".": {
					"Source1.SourceMountedEffect": {"Particle": "antlion_spit", "Sound": "GrenadeSpit.Hit"}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Effect, "explosion", null, null, null, """
			{
				".": {
					"Source1.SourceMountedEffect": {"Particle": "grenade_explosion_01", "Sound": "BaseGrenade.Explode"},
					"Sandbox.RadiusDamage": {"Radius": 250, "PhysicsForceScale": 1.5}
				}
			}
			""" ),
		new( "hl2", SourceEntityKind.Effect, "flechette_explosion", null, null, null, """
			{
				".": {
					"Source1.SourceMountedEffect": {"Particle": "hunter_projectile_explosion_1", "Sound": "NPC_Hunter.FlechetteExplode"}
				}
			}
			""" ),
	];
}
