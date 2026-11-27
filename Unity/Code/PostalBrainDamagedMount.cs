namespace UnityMount;

/// <summary>
/// Game mount for POSTAL: Brain Damaged (Steam AppID 1359980, Unity 2021.3.14f1 URP).
/// Includes maps, props, materials, FMOD audio, interactive doors, breakables/glass, jump pads,
/// explosive barrels, sliding doors, teleporters, pickups, 9 animated playable weapons with authentic
/// primary + secondary attacks, and 24 animated functional NPCs.
/// </summary>
public sealed class PostalBrainDamagedMount : UnityGameMount
{
	public override string Ident => "pbd";
	public override string Title => "POSTAL: Brain Damaged";

	protected override long AppId => 1359980;
	protected override string DataFolder => "POSTAL Brain Damaged_Data";

	static readonly HashSet<string> SkippedScenes = new( StringComparer.OrdinalIgnoreCase )
	{
		"PlatformIntro",
		"Intro",
		"Title",
	};

	static readonly PostalWeaponDef[] BaseWeaponDefs =
	[
		new(
			Id: "pbd_shovel",
			Title: "Shovel",
			Archetype: PostalWeaponArchetype.Shovel,
			ModelPath: "mount://pbd/models/prefabs/weapons/shovel/playerweaponpickup_shovel.vmdl",
			ViewModelRigName: "PlayerWeapon_Shovel",
			IconPath: "mount://pbd/textures/sharedassets0/codex_weapon_shovel.vtex",
			FireSoundPath: "mount://pbd/sounds/player/pbd_sfx_shovel_attack1.vsnd",
			AltFireSoundPath: "mount://pbd/sounds/player/pbd_sfx_shovel_attack_loop.vsnd",
			ReloadSoundPath: "mount://pbd/sounds/player/pbd_sfx_shovel_draw.vsnd",
			ImpactSoundPath: "mount://pbd/sounds/player/sfx_impact_actor_07.vsnd",
			PreferredSlot: 0,
			PrimaryDelay: 0.36f,
			Automatic: true,
			ClipSize: 0,
			Damage: 60f,
			Pellets: 1,
			Spread: 0f,
			TargetLength: 30f,
			ViewOffset: new Vector3( 0f, 0f, 0f ),
			TracerColor: new Color( 1f, 0.9f, 0.65f, 1f )
		),
		new(
			Id: "pbd_pistol",
			Title: "Pistol",
			Archetype: PostalWeaponArchetype.Pistol,
			ModelPath: "mount://pbd/models/prefabs/weapons/pistol/playerweaponpickup_pistol.vmdl",
			ViewModelRigName: "PlayerWeapon_Pistol",
			IconPath: "mount://pbd/textures/sharedassets0/codex_weapon_pistol.vtex",
			FireSoundPath: "mount://pbd/sounds/player/shot_1-gsg5_4x_singleshots_crispy.vsnd",
			AltFireSoundPath: "mount://pbd/sounds/player/pbd_sfx_pistol_alt_shot1.vsnd",
			ReloadSoundPath: "mount://pbd/sounds/player/m9_reload_01.vsnd",
			ImpactSoundPath: "mount://pbd/sounds/master/pbd_sfx_metal_impact_7.vsnd",
			PreferredSlot: 1,
			PrimaryDelay: 0.16f,
			Automatic: false,
			ClipSize: 15,
			Damage: 28f,
			Pellets: 1,
			Spread: 0.5f,
			TargetLength: 14f,
			ViewOffset: new Vector3( 0f, 0f, 0f ),
			TracerColor: new Color( 1f, 0.88f, 0.38f, 1f )
		),
		new(
			Id: "pbd_super_shotgun",
			Title: "Super Shotgun",
			Archetype: PostalWeaponArchetype.SuperShotgun,
			ModelPath: "mount://pbd/models/prefabs/weapons/super_shotgun/playerweaponpickup_shotgun.vmdl",
			ViewModelRigName: "PlayerWeapon_Shotgun",
			IconPath: "mount://pbd/textures/sharedassets0/codex_weapon_shotgun.vtex",
			FireSoundPath: "mount://pbd/sounds/player/shotgun_rework.vsnd",
			AltFireSoundPath: "mount://pbd/sounds/player/nail_gun_kick_and_chain3.vsnd",
			ReloadSoundPath: "mount://pbd/sounds/player/reload_shotgun.vsnd",
			ImpactSoundPath: "mount://pbd/sounds/player/hook_return.vsnd",
			PreferredSlot: 2,
			PrimaryDelay: 0.85f,
			Automatic: false,
			ClipSize: 2,
			Damage: 15f,
			Pellets: 12,
			Spread: 6.2f,
			TargetLength: 28f,
			ViewOffset: new Vector3( 0f, 0f, 0f ),
			TracerColor: new Color( 1f, 0.72f, 0.25f, 1f )
		),
		new(
			Id: "pbd_machinegun",
			Title: "Machine Gun",
			Archetype: PostalWeaponArchetype.MachineGun,
			ModelPath: "mount://pbd/models/prefabs/weapons/machinegun/playerweaponpickup_machinegun.vmdl",
			ViewModelRigName: "PlayerWeapon_Machinegun",
			IconPath: "mount://pbd/textures/sharedassets0/codex_weapon_machinegun.vtex",
			FireSoundPath: "mount://pbd/sounds/player/final_fire_1.vsnd",
			AltFireSoundPath: "mount://pbd/sounds/player/grenade_clock_launch1.vsnd",
			ReloadSoundPath: "mount://pbd/sounds/player/reload_chamber_03_stereo.vsnd",
			ImpactSoundPath: "mount://pbd/sounds/player/explosion_med_long_tail_01.vsnd",
			PreferredSlot: 2,
			PrimaryDelay: 0.085f,
			Automatic: true,
			ClipSize: 40,
			Damage: 18f,
			Pellets: 1,
			Spread: 1.7f,
			TargetLength: 28f,
			ViewOffset: new Vector3( 0f, 0f, 0f ),
			TracerColor: new Color( 1f, 0.85f, 0.35f, 1f )
		),
		new(
			Id: "pbd_gatlinggun",
			Title: "Gatling Gun",
			Archetype: PostalWeaponArchetype.GatlingGun,
			ModelPath: "mount://pbd/models/prefabs/weapons/gatlinggun/playerweaponpickup_gatlinggun.vmdl",
			ViewModelRigName: "PlayerWeapon_GatlingGun",
			IconPath: "mount://pbd/textures/sharedassets0/codex_weapon_gatlinggun.vtex",
			FireSoundPath: "mount://pbd/sounds/player/golden_fire_1.vsnd",
			AltFireSoundPath: "mount://pbd/sounds/player/tracker_shot.vsnd",
			ReloadSoundPath: "mount://pbd/sounds/player/gatling_draw.vsnd",
			ImpactSoundPath: "mount://pbd/sounds/player/tracker_release.vsnd",
			PreferredSlot: 3,
			PrimaryDelay: 0.055f,
			Automatic: true,
			ClipSize: 120,
			Damage: 16f,
			Pellets: 1,
			Spread: 2.6f,
			TargetLength: 32f,
			ViewOffset: new Vector3( 0f, 0f, 0f ),
			TracerColor: new Color( 1f, 0.78f, 0.2f, 1f )
		),
		new(
			Id: "pbd_dildobow",
			Title: "Dildo Bow",
			Archetype: PostalWeaponArchetype.DildoBow,
			ModelPath: "mount://pbd/models/prefabs/weapons/dildobow/playerweaponpickup_dildobow.vmdl",
			ViewModelRigName: "PlayerWeapon_DildoBow",
			IconPath: "mount://pbd/textures/sharedassets0/codex_weapon_dildobow.vtex",
			FireSoundPath: "mount://pbd/sounds/player/bow_fire_1.vsnd",
			AltFireSoundPath: "mount://pbd/sounds/player/bow_full_release.vsnd",
			ReloadSoundPath: "mount://pbd/sounds/player/dildo_bow_draw.vsnd",
			ImpactSoundPath: "mount://pbd/sounds/player/dildo_impact.vsnd",
			PreferredSlot: 3,
			PrimaryDelay: 0.45f,
			Automatic: false,
			ClipSize: 12,
			Damage: 90f,
			Pellets: 1,
			Spread: 0.2f,
			TargetLength: 24f,
			ViewOffset: new Vector3( 0f, 0f, 0f ),
			TracerColor: new Color( 1f, 0.3f, 0.85f, 1f ),
			ExplosionRadius: 0f,
			ProjectileSpeed: 2150f
		),
		new(
			Id: "pbd_rocketlauncher",
			Title: "Rocket Launcher",
			Archetype: PostalWeaponArchetype.RocketLauncher,
			ModelPath: "mount://pbd/models/prefabs/weapons/rocketlauncher/playerweaponpickup_rocketlauncher.vmdl",
			ViewModelRigName: "PlayerWeapon_RocketLauncher",
			IconPath: "mount://pbd/textures/sharedassets0/codex_weapon_rocketlauncher.vtex",
			FireSoundPath: "mount://pbd/sounds/player/rl_fire.vsnd",
			AltFireSoundPath: "mount://pbd/sounds/player/rl_grenade.vsnd",
			ReloadSoundPath: "mount://pbd/sounds/player/rocket_launcher_draw.vsnd",
			ImpactSoundPath: "mount://pbd/sounds/player/rl_explosion_rocket.vsnd",
			PreferredSlot: 4,
			PrimaryDelay: 0.92f,
			Automatic: false,
			ClipSize: 4,
			Damage: 145f,
			Pellets: 1,
			Spread: 0.2f,
			TargetLength: 34f,
			ViewOffset: new Vector3( 0f, 0f, 0f ),
			TracerColor: new Color( 1f, 0.48f, 0.12f, 1f ),
			ExplosionRadius: 260f,
			ProjectileSpeed: 1550f
		),
		new(
			Id: "pbd_catcanon",
			Title: "Cat Cannon",
			Archetype: PostalWeaponArchetype.CatCannon,
			ModelPath: "mount://pbd/models/prefabs/weapons/catcanon/playerweaponpickup_catcanon.vmdl",
			ViewModelRigName: "PlayerWeapon_CatCanon",
			IconPath: "mount://pbd/textures/sharedassets0/codex_weapon_catcannon.vtex",
			FireSoundPath: "mount://pbd/sounds/player/vacuum_shot1.vsnd",
			AltFireSoundPath: "mount://pbd/sounds/player/vacuum_loop.vsnd",
			ReloadSoundPath: "mount://pbd/sounds/player/vacuum_suck_in01.vsnd",
			ImpactSoundPath: "mount://pbd/sounds/player/cat_splat1.vsnd",
			PreferredSlot: 4,
			PrimaryDelay: 0.65f,
			Automatic: false,
			ClipSize: 6,
			Damage: 115f,
			Pellets: 1,
			Spread: 0.4f,
			TargetLength: 30f,
			ViewOffset: new Vector3( 0f, 0f, 0f ),
			TracerColor: new Color( 1f, 0.72f, 0.28f, 1f ),
			ExplosionRadius: 210f,
			ProjectileSpeed: 1450f,
			ProjectileModelPath: "mount://pbd/models/prefabs/characters/other/cat.vmdl"
		),
		new(
			Id: "pbd_lightninggun",
			Title: "Lightning Gun",
			Archetype: PostalWeaponArchetype.LightningGun,
			ModelPath: "mount://pbd/models/prefabs/weapons/lightninggun/playerweaponpickup_lightninggun.vmdl",
			ViewModelRigName: "PlayerWeapon_LightningGun",
			IconPath: "mount://pbd/textures/sharedassets0/codex_weapon_lightninggun.vtex",
			FireSoundPath: "mount://pbd/sounds/player/shock_rifle_zap_dropping_tail_01_mono.vsnd",
			AltFireSoundPath: "mount://pbd/sounds/player/brain_alt_gun_fire.vsnd",
			ReloadSoundPath: "mount://pbd/sounds/player/sci-fi_weapon_reload_3_snd7248.vsnd",
			ImpactSoundPath: "mount://pbd/sounds/player/weather_thunder_thunder_clap_heavy_impact_tone_bright_01_snd12924_5.vsnd",
			PreferredSlot: 4,
			PrimaryDelay: 0.075f,
			Automatic: true,
			ClipSize: 100,
			Damage: 16f,
			Pellets: 1,
			Spread: 0f,
			TargetLength: 28f,
			ViewOffset: new Vector3( 0f, 0f, 0f ),
			TracerColor: new Color( 0.3f, 0.88f, 1f, 1f )
		),
	];

	static readonly PostalNpcDef[] BaseNpcDefs =
	[
		// Civilians (realistic walking & panicking run speeds, authentic single-take voices!)
		new( "pbd_citizen_01", "Citizen (Fat Female)", PostalNpcArchetype.Civilian, PostalNpcAttackStyle.None,
			"mount://pbd/models/prefabs/characters/american_dream/citizen_01.vmdl", "NPC_Citizen_01",
			60f, 64f, 132f, 0f, 0f, 1f, 1f,
			"mount://pbd/sounds/master/female_citizen_notice.vsnd", "", "mount://pbd/sounds/player/pbd_dlc_npc_thot_death_vo_03.vsnd", "mount://pbd/sounds/master/screamfemale_sfxb_2096.vsnd" ),
		new( "pbd_citizen_02", "Citizen (Male)", PostalNpcArchetype.Civilian, PostalNpcAttackStyle.None,
			"mount://pbd/models/prefabs/characters/american_dream/citizen_02.vmdl", "NPC_Citizen_02",
			60f, 72f, 148f, 0f, 0f, 1f, 1f,
			"mount://pbd/sounds/master/male_citizen_notice.vsnd", "", "mount://pbd/sounds/master/male_citizen_stagger.vsnd", "mount://pbd/sounds/master/male_citizen_stagger.vsnd" ),
		new( "pbd_citizen_03", "Citizen (Female)", PostalNpcArchetype.Civilian, PostalNpcAttackStyle.None,
			"mount://pbd/models/prefabs/characters/american_dream/citizen_03.vmdl", "NPC_Citizen_03",
			60f, 72f, 148f, 0f, 0f, 1f, 1f,
			"mount://pbd/sounds/master/female_citizen_notice.vsnd", "", "mount://pbd/sounds/player/pbd_dlc_npc_thot_death_vo_03.vsnd", "mount://pbd/sounds/master/screamfemale_sfxb_2096.vsnd" ),
		new( "pbd_asylum_victim", "Asylum Victim", PostalNpcArchetype.Civilian, PostalNpcAttackStyle.None,
			"mount://pbd/models/prefabs/characters/asylum/asylum_victim.vmdl", "NPC_AsylumCitizen",
			60f, 68f, 140f, 0f, 0f, 1f, 1f,
			"mount://pbd/sounds/master/male_citizen_notice.vsnd", "", "mount://pbd/sounds/master/male_citizen_stagger.vsnd", "mount://pbd/sounds/master/male_citizen_stagger.vsnd" ),
		new( "pbd_cat", "Cat", PostalNpcArchetype.Civilian, PostalNpcAttackStyle.None,
			"mount://pbd/models/prefabs/characters/other/cat.vmdl", "NPC_Cat",
			35f, 78f, 170f, 0f, 0f, 1f, 1f,
			"mount://pbd/sounds/player/vacuum_idle_meow-007.vsnd", "", "mount://pbd/sounds/player/vacuum_idle_meow-006.vsnd", "mount://pbd/sounds/player/cat_death1.vsnd" ),

		// American Dream Enemies
		new( "pbd_karen", "Karen", PostalNpcArchetype.MeleeEnemy, PostalNpcAttackStyle.KarenBrawler,
			"mount://pbd/models/prefabs/characters/american_dream/karen.vmdl", "NPC_Karen",
			150f, 78f, 155f, 18f, 95f, 0.95f, 0.40f,
			"mount://pbd/sounds/master/karen_onblockstart-005.vsnd", "mount://pbd/sounds/master/karen_onmeleestart-002.vsnd", "mount://pbd/sounds/master/karen_onstagger-006.vsnd", "mount://pbd/sounds/master/karen_onstagger-006.vsnd" ),
		new( "pbd_burger_boy", "Burger Boy", PostalNpcArchetype.RangedEnemy, PostalNpcAttackStyle.RangedBurst,
			"mount://pbd/models/prefabs/characters/american_dream/burger_boy.vmdl", "NPC_BurgerKid",
			130f, 68f, 130f, 11f, 820f, 1.35f, 0.66f,
			"mount://pbd/sounds/master/burgerkid_shit3.vsnd", "mount://pbd/sounds/player/shot_1-gsg5_4x_singleshots_crispy.vsnd", "mount://pbd/sounds/master/burgerkid_death_burger_kid_vo_take_2-006.vsnd", "mount://pbd/sounds/master/burgerkid_death_burger_kid_vo_take_2-010.vsnd" ),
		new( "pbd_prison_guard", "Prison Guard", PostalNpcArchetype.RangedEnemy, PostalNpcAttackStyle.RangedSingle,
			"mount://pbd/models/prefabs/characters/american_dream/prison_guard.vmdl", "NPC_PrisonGuard",
			120f, 74f, 145f, 13f, 900f, 0.9f, 1f,
			"mount://pbd/sounds/master/male_citizen_notice.vsnd", "mount://pbd/sounds/player/shot_2-gsg5_4x_singleshots_crispy.vsnd", "mount://pbd/sounds/master/male_citizen_stagger.vsnd", "mount://pbd/sounds/master/male_citizen_stagger.vsnd" ),
		new( "pbd_prisoner", "Prisoner", PostalNpcArchetype.MeleeEnemy, PostalNpcAttackStyle.MeleeRush,
			"mount://pbd/models/prefabs/characters/american_dream/prisoner.vmdl", "NPC_Prisoner",
			100f, 80f, 162f, 15f, 85f, 0.9f, 1f,
			"mount://pbd/sounds/master/male_citizen_notice.vsnd", "mount://pbd/sounds/player/pbd_sfx_shovel_attack2.vsnd", "mount://pbd/sounds/master/prisoner_onstagger_postal_brain_damaged_-_prisoner_lines-005.vsnd", "mount://pbd/sounds/master/prisoner_ondeath_postal_brain_damaged_-_prisoner_lines-002.vsnd" ),
		new( "pbd_cusslinger", "Cusslinger", PostalNpcArchetype.RangedEnemy, PostalNpcAttackStyle.RangedSingle,
			"mount://pbd/models/prefabs/characters/american_dream/cusslinger.vmdl", "NPC_SheriffRevolver",
			110f, 76f, 146f, 16f, 950f, 1.0f, 1f,
			"mount://pbd/sounds/master/male_citizen_notice.vsnd", "mount://pbd/sounds/player/pbd_sfx_pistol_alt_shot2.vsnd", "mount://pbd/sounds/master/male_citizen_stagger.vsnd", "mount://pbd/sounds/master/male_citizen_stagger.vsnd" ),
		new( "pbd_veteran", "Veteran", PostalNpcArchetype.RangedEnemy, PostalNpcAttackStyle.ShotgunSpread,
			"mount://pbd/models/prefabs/characters/american_dream/veteran.vmdl", "NPC_Veteran",
			130f, 72f, 140f, 14f, 850f, 1.15f, 1f,
			"mount://pbd/sounds/master/male_citizen_notice.vsnd", "mount://pbd/sounds/player/shotgun_rework.vsnd", "mount://pbd/sounds/master/veteran_on_stagger_vox-002.vsnd", "mount://pbd/sounds/master/veteran_on_stagger_vox-001.vsnd" ),
		new( "pbd_basement_dweller", "Basement Dweller", PostalNpcArchetype.MeleeEnemy, PostalNpcAttackStyle.MeleeRush,
			"mount://pbd/models/prefabs/characters/american_dream/basement_dweller.vmdl", "NPC_Fatty",
			145f, 65f, 128f, 19f, 95f, 1.1f, 0.9f,
			"mount://pbd/sounds/master/burgerkid_shit3.vsnd", "mount://pbd/sounds/player/sfx_swing_heavy_00.vsnd", "mount://pbd/sounds/master/fatty_stagger_vox-001.vsnd", "mount://pbd/sounds/master/fatty_death_vox-001.vsnd" ),
		new( "pbd_golden_retriever", "Golden Retriever", PostalNpcArchetype.MeleeEnemy, PostalNpcAttackStyle.MeleeRush,
			"mount://pbd/models/prefabs/characters/american_dream/golden_retriver.vmdl", "NPC_GoldenRetriever",
			90f, 88f, 185f, 14f, 82f, 0.8f, 1f,
			"", "mount://pbd/sounds/master/karen_onmeleestart-002.vsnd", "mount://pbd/sounds/master/karen_onstagger-006.vsnd", "mount://pbd/sounds/master/karen_onstagger-006.vsnd" ),
		new( "pbd_goliath", "Goliath", PostalNpcArchetype.MeleeEnemy, PostalNpcAttackStyle.GoliathSlam,
			"mount://pbd/models/prefabs/characters/american_dream/goliath.vmdl", "NPC_Goliath_Body",
			340f, 62f, 118f, 32f, 125f, 1.45f, 1.65f,
			"mount://pbd/sounds/master/goliath_onhit1.vsnd", "mount://pbd/sounds/master/pbd_sfx_destructible_door.vsnd", "mount://pbd/sounds/master/goliath_onhit2.vsnd", "mount://pbd/sounds/master/goliath_death.vsnd" ),

		// Asylum Enemies
		new( "pbd_lizard_dude", "Lizard-Dude", PostalNpcArchetype.RangedEnemy, PostalNpcAttackStyle.PlasmaSpit,
			"mount://pbd/models/prefabs/characters/asylum/lizard-dude.vmdl", "NPC_LizardMan",
			125f, 76f, 152f, 14f, 880f, 1.0f, 1f,
			"mount://pbd/sounds/master/lizardman_target_found_voice-001.vsnd", "mount://pbd/sounds/master/lizardman_shot_laser_init.vsnd", "mount://pbd/sounds/master/lizardman_stagger_voice-001.vsnd", "mount://pbd/sounds/master/lizardman_death_voice-003.vsnd" ),
		new( "pbd_the_vaccinated", "The Vaccinated", PostalNpcArchetype.RangedEnemy, PostalNpcAttackStyle.PlasmaSpit,
			"mount://pbd/models/prefabs/characters/asylum/the_vaccinated.vmdl", "NPC_Infected",
			130f, 74f, 146f, 14f, 850f, 1.05f, 1f,
			"mount://pbd/sounds/master/male_citizen_notice.vsnd", "mount://pbd/sounds/master/infected_shoot.vsnd", "mount://pbd/sounds/master/male_citizen_stagger.vsnd", "mount://pbd/sounds/master/male_citizen_stagger.vsnd" ),
		new( "pbd_lobotomized_creep", "Lobotomized Creep", PostalNpcArchetype.MeleeEnemy, PostalNpcAttackStyle.MeleeRush,
			"mount://pbd/models/prefabs/characters/asylum/lobotomized_creep.vmdl", "NPC_Crawler",
			110f, 84f, 178f, 16f, 86f, 0.85f, 1f,
			"mount://pbd/sounds/master/male_citizen_notice.vsnd", "mount://pbd/sounds/master/karen_onmeleestart-002.vsnd", "mount://pbd/sounds/player/crawler_death_01.vsnd", "mount://pbd/sounds/player/crawler_death_02.vsnd" ),
		new( "pbd_gray_trash", "Zeta Reticuli Gray Trash", PostalNpcArchetype.RangedEnemy, PostalNpcAttackStyle.RangedBurst,
			"mount://pbd/models/prefabs/characters/asylum/zeta_reticuli_gray_trash.vmdl", "NPC_AlienGray",
			115f, 76f, 152f, 11f, 920f, 1.25f, 1f,
			"mount://pbd/sounds/master/alien_non_humanoid_on_target_found_shrieks-001.vsnd", "mount://pbd/sounds/player/laser.vsnd", "mount://pbd/sounds/master/alien_non_humanoid_death_shrieks-002.vsnd", "mount://pbd/sounds/master/alien_non_humanoid_death_shrieks-002.vsnd" ),
		new( "pbd_tin_foiled", "Tin-Foiled", PostalNpcArchetype.RangedEnemy, PostalNpcAttackStyle.RangedSingle,
			"mount://pbd/models/prefabs/characters/asylum/tin-foiled.vmdl", "NPC_FoilGuy",
			115f, 74f, 146f, 13f, 860f, 1.0f, 0.84f,
			"mount://pbd/sounds/master/male_citizen_notice.vsnd", "mount://pbd/sounds/player/pbd_sfx_pistol_alt_shot1.vsnd", "mount://pbd/sounds/master/male_citizen_stagger.vsnd", "mount://pbd/sounds/master/male_citizen_stagger.vsnd" ),
		new( "pbd_nurse_joy", "Nurse Joy", PostalNpcArchetype.RangedEnemy, PostalNpcAttackStyle.RangedSingle,
			"mount://pbd/models/prefabs/characters/asylum/nurse_joy.vmdl", "NPC_Nurse",
			120f, 76f, 150f, 14f, 850f, 1.05f, 0.72f,
			"mount://pbd/sounds/master/female_citizen_notice.vsnd", "mount://pbd/sounds/master/nurse_attack_stomp-001.vsnd", "mount://pbd/sounds/player/pbd_dlc_npc_thot_death_vo_03.vsnd", "mount://pbd/sounds/master/nurse_ondeath-006.vsnd" ),

		// No Lives Matter & Orchestra Enemies
		new( "pbd_arseface", "Arseface", PostalNpcArchetype.MeleeEnemy, PostalNpcAttackStyle.MeleeRush,
			"mount://pbd/models/prefabs/characters/orchestra/arseface.vmdl", "NPC_OrchestraAssface",
			135f, 76f, 154f, 18f, 90f, 0.95f, 1f,
			"mount://pbd/sounds/master/karen_onblockstart-005.vsnd", "mount://pbd/sounds/master/karen_onmeleestart-002.vsnd", "mount://pbd/sounds/master/karen_onstagger-006.vsnd", "mount://pbd/sounds/master/karen_onstagger-006.vsnd" ),
		new( "pbd_cheer_killer", "Cheer Killer", PostalNpcArchetype.MeleeEnemy, PostalNpcAttackStyle.MeleeRush,
			"mount://pbd/models/prefabs/characters/orchestra/cheer_killer.vmdl", "",
			120f, 82f, 168f, 16f, 88f, 0.85f, 1f,
			"mount://pbd/sounds/master/female_citizen_notice.vsnd", "mount://pbd/sounds/master/karen_onmeleestart-002.vsnd", "mount://pbd/sounds/player/pbd_dlc_npc_thot_death_vo_03.vsnd", "mount://pbd/sounds/master/screamfemale_sfxb_2096.vsnd" ),
		new( "pbd_dominatrix", "Dominatrix", PostalNpcArchetype.RangedEnemy, PostalNpcAttackStyle.CrossbowBolt,
			"mount://pbd/models/prefabs/characters/no_live_matters/dominatrix.vmdl", "NPC_Domina",
			145f, 78f, 155f, 18f, 920f, 1.0f, 1f,
			"mount://pbd/sounds/master/female_citizen_notice.vsnd", "mount://pbd/sounds/master/domina_shot_crossbow-001.vsnd", "mount://pbd/sounds/master/domina_onstagger-006.vsnd", "mount://pbd/sounds/master/domina_ondeath-009.vsnd" ),
		new( "pbd_the_other_dude", "The Other Dude", PostalNpcArchetype.RangedEnemy, PostalNpcAttackStyle.ShotgunSpread,
			"mount://pbd/models/prefabs/characters/no_live_matters/the_other_dude.vmdl", "NPC_AlterDude",
			260f, 84f, 168f, 18f, 1050f, 0.9f, 1f,
			"mount://pbd/sounds/master/altdude_onshotgun_0-001.vsnd", "mount://pbd/sounds/player/shotgun_rework.vsnd", "mount://pbd/sounds/master/altdude_onbigpain_0.vsnd", "mount://pbd/sounds/master/altdude_onbigpain_3-001.vsnd" ),
	];

	PostalNpcDef[] _resolvedNpcDefs = BaseNpcDefs;

	protected override void RegisterGameplayPrefabs( MountContext context )
	{
		// Discover animated weapon ViewModels, NPC skeletal rigs, and 3D projectile prefabs in sharedassets0.assets
		Dictionary<string, long> animRoots = null;
		Dictionary<string, long> projRoots = null;
		SerializedFile shared0 = null;
		if ( Workspace.TryGetFile( "sharedassets0.assets", out shared0 ) )
		{
			animRoots = IndexAnimatedRigRoots( shared0 );
			projRoots = IndexGameObjectsByName( shared0, new HashSet<string>( StringComparer.OrdinalIgnoreCase )
			{
				"Projectile_DildoArrow",
				"Projectile_Rocket",
				"Projectile_Grenade",
				"Projectile_Timebomb",
				"LightningGun_Projectile",
			} );
		}

		// Register original 3D PBD projectile models for Dildo Bow, Rocket Launcher, Holy Hand Grenade, SMG Timebomb, and Tesla Orb
		string dildoProjModel = "";
		string rocketProjModel = "";
		string lightningProjModel = "";
		if ( shared0 is not null && projRoots is not null )
		{
			if ( projRoots.TryGetValue( "Projectile_DildoArrow", out long dPid ) )
				dildoProjModel = RegisterPrefabModel( context, shared0, dPid, "models/projectiles/pbd_dildo_arrow" );
			if ( projRoots.TryGetValue( "Projectile_Rocket", out long rPid ) )
				rocketProjModel = RegisterPrefabModel( context, shared0, rPid, "models/projectiles/pbd_rocket" );
			if ( projRoots.TryGetValue( "Projectile_Grenade", out long gPid ) )
				RegisterPrefabModel( context, shared0, gPid, "models/projectiles/pbd_grenade" );
			if ( projRoots.TryGetValue( "Projectile_Timebomb", out long tPid ) )
				RegisterPrefabModel( context, shared0, tPid, "models/projectiles/pbd_timebomb" );
			if ( projRoots.TryGetValue( "LightningGun_Projectile", out long lPid ) )
				lightningProjModel = RegisterPrefabModel( context, shared0, lPid, "models/projectiles/pbd_lightning_orb" );
		}

		// 1. Register animated First-Person ViewModel rigs + Playable Weapon Prefabs
		foreach ( var baseDef in BaseWeaponDefs )
		{
			string vmPath = "";
			if ( shared0 is not null && animRoots is not null
				&& !string.IsNullOrEmpty( baseDef.ViewModelRigName )
				&& animRoots.TryGetValue( baseDef.ViewModelRigName, out long vmGoPid ) )
			{
				vmPath = RegisterAnimatedRigModel(
					context, shared0, vmGoPid,
					$"models/viewmodels/{baseDef.Id}_vm",
					isViewModel: true,
					maxClips: 16,
					developerOnly: true );
			}

			string projModel = baseDef.ProjectileModelPath;
			if ( string.IsNullOrEmpty( projModel ) )
			{
				projModel = baseDef.Archetype switch
				{
					PostalWeaponArchetype.DildoBow => dildoProjModel,
					PostalWeaponArchetype.RocketLauncher => rocketProjModel,
					PostalWeaponArchetype.LightningGun => lightningProjModel,
					_ => ""
				};
			}

			var resolvedDef = baseDef with { ResolvedViewModelPath = vmPath, ProjectileModelPath = projModel };
			RegisterWeaponPrefab( context, resolvedDef );
		}

		// 2. Register animated NPC skeletal rigs + Playable NPC Prefabs
		var resolvedList = new PostalNpcDef[BaseNpcDefs.Length];
		for ( int i = 0; i < BaseNpcDefs.Length; i++ )
		{
			var baseDef = BaseNpcDefs[i];
			string animModelPath = "";
			if ( shared0 is not null && animRoots is not null
				&& !string.IsNullOrEmpty( baseDef.AnimatedRigName )
				&& animRoots.TryGetValue( baseDef.AnimatedRigName, out long npcGoPid ) )
			{
				animModelPath = RegisterAnimatedRigModel(
					context, shared0, npcGoPid,
					$"models/characters_anim/{baseDef.Id}",
					isViewModel: false,
					maxClips: 10,
					developerOnly: true );
			}

			resolvedList[i] = baseDef with { ResolvedModelPath = animModelPath };
			RegisterNpcPrefab( context, resolvedList[i] );
		}
		_resolvedNpcDefs = resolvedList;
	}

	public override bool TryGetMapNpcDef( string spawnObjectName, out PostalNpcDef def )
	{
		def = null;
		if ( string.IsNullOrWhiteSpace( spawnObjectName ) )
			return false;

		var defs = _resolvedNpcDefs;
		var n = spawnObjectName.ToLowerInvariant();
		if ( n.Contains( "pastel" ) || n.Contains( "citizen" ) ) def = defs[1];
		else if ( n.Contains( "asylum victim" ) ) def = defs[3];
		else if ( n.Contains( "cat" ) ) def = defs[4];
		else if ( n.Contains( "karen" ) ) def = defs[5];
		else if ( n.Contains( "burger" ) ) def = defs[6];
		else if ( n.Contains( "prison guard" ) ) def = defs[7];
		else if ( n.Contains( "prisoner" ) ) def = defs[8];
		else if ( n.Contains( "cusslinger" ) ) def = defs[9];
		else if ( n.Contains( "veteran" ) ) def = defs[10];
		else if ( n.Contains( "basement" ) ) def = defs[11];
		else if ( n.Contains( "retriever" ) || n.Contains( "retriver" ) ) def = defs[12];
		else if ( n.Contains( "goliath" ) ) def = defs[13];
		else if ( n.Contains( "lizard" ) ) def = defs[14];
		else if ( n.Contains( "vaccinated" ) ) def = defs[15];
		else if ( n.Contains( "lobotomized" ) ) def = defs[16];
		else if ( n.Contains( "gray trash" ) ) def = defs[17];
		else if ( n.Contains( "tin-foiled" ) || n.Contains( "tinfoil" ) ) def = defs[18];
		else if ( n.Contains( "nurse" ) ) def = defs[19];
		else if ( n.Contains( "arseface" ) || n.Contains( "assface" ) ) def = defs[20];
		else if ( n.Contains( "cheer" ) ) def = defs[21];
		else if ( n.Contains( "dominatrix" ) ) def = defs[22];
		else if ( n.Contains( "other dude" ) ) def = defs[23];

		return def is not null;
	}

	protected override string GetMapMountName( int levelIndex, string unityScenePath )
	{
		var rawName = Path.GetFileNameWithoutExtension( unityScenePath );
		if ( string.IsNullOrWhiteSpace( rawName ) || SkippedScenes.Contains( rawName ) )
			return null;

		return SanitizeSegment( rawName );
	}

	public override bool IsHiddenVisualLayer( uint layer )
		=> layer is 8   // Player_Only (invisible clip borders like Neighbrohood_Border_Collider)
			or 13       // TimeBubble
			or 18;      // ACSCulling

	public override bool IsPlayerSpawnObjectName( string gameObjectName )
	{
		if ( string.IsNullOrEmpty( gameObjectName ) )
			return false;

		var n = gameObjectName.ToLowerInvariant();
		if ( n.StartsWith( "npc" ) || n.Contains( "fake" ) || n.StartsWith( "spawn:" ) || n.Contains( "enemy" ) )
			return false;

		return base.IsPlayerSpawnObjectName( gameObjectName )
			|| n.Contains( "fpsplayer" )
			|| n.Contains( "fps_player" )
			|| n.Contains( "playercontroller" )
			|| n.Contains( "spawn_player" )
			|| n.Contains( "checkpoint_start" );
	}
}
