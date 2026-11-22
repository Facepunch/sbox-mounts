using System;

namespace Doom;

public enum ThingCategory
{
	PlayerStart,
	DeathmatchStart,
	TeleportDestination,
	Monster,
	Weapon,
	Ammo,
	Health,
	Armor,
	Powerup,
	Key,
	Decoration,
	Light,
	Gore,
}

[Flags]
public enum ThingTypeFlags
{
	None = 0,

	/// <summary>MF_SOLID: blocks movement with a box <see cref="ThingType.Radius"/> wide and <see cref="ThingType.Height"/> tall.</summary>
	Solid = 1,

	/// <summary>MF_SPAWNCEILING: hangs from the ceiling.</summary>
	Hanging = 2,

	/// <summary>The spawn frames are full bright (FF_FULLBRIGHT), ignoring sector light.</summary>
	Bright = 4,

	/// <summary>Can be shot and blows up when killed (A_Explode), like the barrel.</summary>
	Explosive = 8,

	/// <summary>
	/// Pickupable and lobable
	/// </summary>
	Rigidbody = 9,
}

/// <summary>
/// A run of sprite frames. A frame shown for longer than <see cref="Tics"/> is repeated, so
/// vanilla's per-state durations play back at one frame rate.
/// </summary>
/// <param name="Sprite">4 letter sprite name.</param>
/// <param name="Frames">Frame letters in play order.</param>
/// <param name="Tics">Tics per letter (35 per second).</param>
public sealed record ThingAnimation( string Sprite, string Frames, int Tics );

/// <summary>
/// A thing's editor number and what vanilla's mobjinfo and spawn state make of it.
/// Only what a static scene needs is kept: the idle animation, size and a few flags.
/// </summary>
/// <param name="Id">Editor number (doomednum).</param>
/// <param name="Name">Short unique name, used for the generated sprite resource.</param>
/// <param name="Category">What kind of thing it is.</param>
/// <param name="Sprite">4 letter sprite name, or null for things that aren't drawn.</param>
/// <param name="Frames">Frame letters of the spawn animation in play order.</param>
/// <param name="Tics">Tics per frame (35 per second).</param>
/// <param name="Radius">Collision radius in map units.</param>
/// <param name="Height">Collision height in map units.</param>
/// <param name="Flags">Behaviour flags.</param>
/// <param name="Health">Spawn health, for things that can be killed.</param>
/// <param name="Death">The death animation, for things that can be killed. Always full bright here.</param>
public sealed record ThingType( int Id, string Name, ThingCategory Category, string Sprite, string Frames = "A",
	int Tics = 0, int Radius = 20, int Height = 16, ThingTypeFlags Flags = ThingTypeFlags.None,
	int Health = 0, ThingAnimation Death = null );

public static class ThingTypes
{
	const ThingTypeFlags Solid = ThingTypeFlags.Solid, 
		Hanging = ThingTypeFlags.Hanging, 
		Bright = ThingTypeFlags.Bright,
		Explosive = ThingTypeFlags.Explosive,
		Rigidbody = ThingTypeFlags.Rigidbody;

	/// <summary>Every vanilla Doom and Doom II thing (info.c).</summary>
	public static readonly IReadOnlyDictionary<int, ThingType> Vanilla = new ThingType[]
	{
		new( 1, "player1_start", ThingCategory.PlayerStart, null ),
		new( 2, "player2_start", ThingCategory.PlayerStart, null ),
		new( 3, "player3_start", ThingCategory.PlayerStart, null ),
		new( 4, "player4_start", ThingCategory.PlayerStart, null ),
		new( 11, "deathmatch_start", ThingCategory.DeathmatchStart, null ),
		new( 14, "teleport_destination", ThingCategory.TeleportDestination, null ),

		// Monsters: idle frames, front rotation.
		new( 3004, "zombieman", ThingCategory.Monster, "POSS", "AB", 10, 20, 56, Solid ),
		new( 9, "shotgun_guy", ThingCategory.Monster, "SPOS", "AB", 10, 20, 56, Solid ),
		new( 65, "heavy_weapon_dude", ThingCategory.Monster, "CPOS", "AB", 10, 20, 56, Solid ),
		new( 3001, "imp", ThingCategory.Monster, "TROO", "AB", 10, 20, 56, Solid ),
		new( 3002, "demon", ThingCategory.Monster, "SARG", "AB", 10, 30, 56, Solid ),
		new( 58, "spectre", ThingCategory.Monster, "SARG", "AB", 10, 30, 56, Solid ),
		new( 3006, "lost_soul", ThingCategory.Monster, "SKUL", "AB", 10, 16, 56, Solid | Bright ),
		new( 3005, "cacodemon", ThingCategory.Monster, "HEAD", "A", 10, 31, 56, Solid ),
		new( 69, "hell_knight", ThingCategory.Monster, "BOS2", "AB", 10, 24, 64, Solid ),
		new( 3003, "baron_of_hell", ThingCategory.Monster, "BOSS", "AB", 10, 24, 64, Solid ),
		new( 68, "arachnotron", ThingCategory.Monster, "BSPI", "AB", 10, 64, 64, Solid ),
		new( 71, "pain_elemental", ThingCategory.Monster, "PAIN", "A", 10, 31, 56, Solid ),
		new( 66, "revenant", ThingCategory.Monster, "SKEL", "AB", 10, 20, 56, Solid ),
		new( 67, "mancubus", ThingCategory.Monster, "FATT", "AB", 15, 48, 64, Solid ),
		new( 64, "arch_vile", ThingCategory.Monster, "VILE", "AB", 10, 20, 56, Solid ),
		new( 7, "spider_mastermind", ThingCategory.Monster, "SPID", "AB", 10, 128, 100, Solid ),
		new( 16, "cyberdemon", ThingCategory.Monster, "CYBR", "AB", 10, 40, 110, Solid ),
		new( 84, "wolfenstein_ss", ThingCategory.Monster, "SSWV", "AB", 10, 20, 56, Solid ),
		new( 72, "commander_keen", ThingCategory.Monster, "KEEN", "A", 0, 16, 72, Solid | Hanging ),
		new( 88, "boss_brain", ThingCategory.Monster, "BBRN", "A", 0, 16, 16, Solid ),

		// Weapons
		new( 2005, "chainsaw", ThingCategory.Weapon, "CSAW" ),
		new( 2001, "shotgun", ThingCategory.Weapon, "SHOT" ),
		new( 82, "super_shotgun", ThingCategory.Weapon, "SGN2" ),
		new( 2002, "chaingun", ThingCategory.Weapon, "MGUN" ),
		new( 2003, "rocket_launcher", ThingCategory.Weapon, "LAUN" ),
		new( 2004, "plasma_rifle", ThingCategory.Weapon, "PLAS" ),
		new( 2006, "bfg9000", ThingCategory.Weapon, "BFUG" ),

		// Ammo
		new( 2007, "clip", ThingCategory.Ammo, "CLIP" ),
		new( 2048, "box_of_bullets", ThingCategory.Ammo, "AMMO" ),
		new( 2008, "shells", ThingCategory.Ammo, "SHEL" ),
		new( 2049, "box_of_shells", ThingCategory.Ammo, "SBOX" ),
		new( 2010, "rocket", ThingCategory.Ammo, "ROCK" ),
		new( 2046, "box_of_rockets", ThingCategory.Ammo, "BROK" ),
		new( 2047, "cell_charge", ThingCategory.Ammo, "CELL" ),
		new( 17, "cell_pack", ThingCategory.Ammo, "CELP" ),
		new( 8, "backpack", ThingCategory.Ammo, "BPAK" ),

		// Health and armour
		new( 2011, "stimpack", ThingCategory.Health, "STIM" ),
		new( 2012, "medikit", ThingCategory.Health, "MEDI" ),
		new( 2014, "health_bonus", ThingCategory.Health, "BON1", "ABCDCB", 6 ),
		new( 2013, "soulsphere", ThingCategory.Health, "SOUL", "ABCDCB", 6, Flags: Bright ),
		new( 83, "megasphere", ThingCategory.Health, "MEGA", "ABCD", 6, Flags: Bright ),
		new( 2015, "armor_bonus", ThingCategory.Armor, "BON2", "ABCDCB", 6 ),
		new( 2018, "green_armor", ThingCategory.Armor, "ARM1", "AB", 6, Flags: Bright ),
		new( 2019, "blue_armor", ThingCategory.Armor, "ARM2", "AB", 6, Flags: Bright ),

		// Powerups
		new( 2022, "invulnerability", ThingCategory.Powerup, "PINV", "ABCD", 6, Flags: Bright ),
		new( 2023, "berserk", ThingCategory.Powerup, "PSTR", "A", Flags: Bright ),
		new( 2024, "partial_invisibility", ThingCategory.Powerup, "PINS", "ABCD", 6, Flags: Bright ),
		new( 2025, "radiation_suit", ThingCategory.Powerup, "SUIT", "A", Flags: Bright ),
		new( 2026, "computer_map", ThingCategory.Powerup, "PMAP", "ABCDCB", 6, Flags: Bright ),
		new( 2045, "light_amplification", ThingCategory.Powerup, "PVIS", "AB", 6, Flags: Bright ),

		// Keys
		new( 5, "blue_keycard", ThingCategory.Key, "BKEY", "AB", 10, Flags: Bright ),
		new( 6, "yellow_keycard", ThingCategory.Key, "YKEY", "AB", 10, Flags: Bright ),
		new( 13, "red_keycard", ThingCategory.Key, "RKEY", "AB", 10, Flags: Bright ),
		new( 40, "blue_skull_key", ThingCategory.Key, "BSKU", "AB", 10, Flags: Bright ),
		new( 39, "yellow_skull_key", ThingCategory.Key, "YSKU", "AB", 10, Flags: Bright ),
		new( 38, "red_skull_key", ThingCategory.Key, "RSKU", "AB", 10, Flags: Bright ),

		// Obstacles and decorations
		// S_BEXP to S_BEXP5: A, B, C for 5 tics each, then D and E for 10.
		new( 2035, "barrel", ThingCategory.Decoration, "BAR1", "AB", 6, 10, 42, Solid | Explosive | Rigidbody,
			Health: 20, Death: new ThingAnimation( "BEXP", "ABCDDEE", 5 ) ),
		new( 30, "tall_green_pillar", ThingCategory.Decoration, "COL1", "A", 0, 16, 16, Solid ),
		new( 31, "short_green_pillar", ThingCategory.Decoration, "COL2", "A", 0, 16, 16, Solid ),
		new( 32, "tall_red_pillar", ThingCategory.Decoration, "COL3", "A", 0, 16, 16, Solid ),
		new( 33, "short_red_pillar", ThingCategory.Decoration, "COL4", "A", 0, 16, 16, Solid ),
		new( 36, "pillar_with_heart", ThingCategory.Decoration, "COL5", "AB", 14, 16, 16, Solid ),
		new( 37, "pillar_with_skull", ThingCategory.Decoration, "COL6", "A", 0, 16, 16, Solid ),
		new( 41, "evil_eye", ThingCategory.Decoration, "CEYE", "ABCB", 6, 16, 16, Solid | Bright ),
		new( 42, "floating_skull", ThingCategory.Decoration, "FSKU", "ABC", 6, 16, 16, Solid | Bright ),
		new( 43, "burnt_tree", ThingCategory.Decoration, "TRE1", "A", 0, 16, 16, Solid ),
		new( 54, "brown_tree", ThingCategory.Decoration, "TRE2", "A", 0, 32, 16, Solid ),
		new( 47, "stalagmite", ThingCategory.Decoration, "SMIT", "A", 0, 16, 16, Solid ),
		new( 48, "tall_techno_column", ThingCategory.Decoration, "ELEC", "A", 0, 16, 16, Solid ),
		new( 27, "skull_on_pole", ThingCategory.Decoration, "POL4", "A", 0, 16, 16, Solid ),
		new( 28, "skulls_on_pole", ThingCategory.Decoration, "POL2", "A", 0, 16, 16, Solid ),
		new( 29, "pile_of_skulls", ThingCategory.Decoration, "POL3", "AB", 6, 16, 16, Solid | Bright ),
		new( 25, "impaled_human", ThingCategory.Decoration, "POL1", "A", 0, 16, 16, Solid ),
		new( 26, "twitching_impaled_human", ThingCategory.Decoration, "POL6", "AB", 8, 16, 16, Solid ),

		// Light sources
		new( 2028, "floor_lamp", ThingCategory.Light, "COLU", "A", 0, 16, 16, Solid | Bright ),
		new( 85, "tall_techno_lamp", ThingCategory.Light, "TLMP", "ABCD", 4, 16, 16, Solid | Bright ),
		new( 86, "short_techno_lamp", ThingCategory.Light, "TLP2", "ABCD", 4, 16, 16, Solid | Bright ),
		new( 34, "candle", ThingCategory.Light, "CAND", "A", 0, 20, 16, Bright ),
		new( 35, "candelabra", ThingCategory.Light, "CBRA", "A", 0, 16, 16, Solid | Bright ),
		new( 44, "tall_blue_torch", ThingCategory.Light, "TBLU", "ABCD", 4, 16, 16, Solid | Bright ),
		new( 45, "tall_green_torch", ThingCategory.Light, "TGRN", "ABCD", 4, 16, 16, Solid | Bright ),
		new( 46, "tall_red_torch", ThingCategory.Light, "TRED", "ABCD", 4, 16, 16, Solid | Bright ),
		new( 55, "short_blue_torch", ThingCategory.Light, "SMBT", "ABCD", 4, 16, 16, Solid | Bright ),
		new( 56, "short_green_torch", ThingCategory.Light, "SMGT", "ABCD", 4, 16, 16, Solid | Bright ),
		new( 57, "short_red_torch", ThingCategory.Light, "SMRT", "ABCD", 4, 16, 16, Solid | Bright ),
		new( 70, "burning_barrel", ThingCategory.Light, "FCAN", "ABC", 4, 16, 16, Solid | Bright ),

		// Hanging
		new( 49, "hanging_victim_twitching", ThingCategory.Gore, "GOR1", "ABCB", 10, 16, 68, Solid | Hanging ),
		new( 50, "hanging_victim_arms_out", ThingCategory.Gore, "GOR2", "A", 0, 16, 84, Solid | Hanging ),
		new( 51, "hanging_victim_one_legged", ThingCategory.Gore, "GOR3", "A", 0, 16, 84, Solid | Hanging ),
		new( 52, "hanging_pair_of_legs", ThingCategory.Gore, "GOR4", "A", 0, 16, 68, Solid | Hanging ),
		new( 53, "hanging_leg", ThingCategory.Gore, "GOR5", "A", 0, 16, 52, Solid | Hanging ),
		new( 59, "hanging_victim_arms_out_nb", ThingCategory.Gore, "GOR2", "A", 0, 20, 84, Hanging ),
		new( 60, "hanging_pair_of_legs_nb", ThingCategory.Gore, "GOR4", "A", 0, 20, 68, Hanging ),
		new( 61, "hanging_victim_one_legged_nb", ThingCategory.Gore, "GOR3", "A", 0, 20, 52, Hanging ),
		new( 62, "hanging_leg_nb", ThingCategory.Gore, "GOR5", "A", 0, 20, 52, Hanging ),
		new( 63, "hanging_victim_twitching_nb", ThingCategory.Gore, "GOR1", "ABCB", 10, 20, 68, Hanging ),
		new( 73, "hanging_victim_guts_removed", ThingCategory.Gore, "HDB1", "A", 0, 16, 88, Solid | Hanging ),
		new( 74, "hanging_victim_guts_and_brain_removed", ThingCategory.Gore, "HDB2", "A", 0, 16, 88, Solid | Hanging ),
		new( 75, "hanging_torso_looking_down", ThingCategory.Gore, "HDB3", "A", 0, 16, 64, Solid | Hanging ),
		new( 76, "hanging_torso_open_skull", ThingCategory.Gore, "HDB4", "A", 0, 16, 64, Solid | Hanging ),
		new( 77, "hanging_torso_looking_up", ThingCategory.Gore, "HDB5", "A", 0, 16, 64, Solid | Hanging ),
		new( 78, "hanging_torso_brain_removed", ThingCategory.Gore, "HDB6", "A", 0, 16, 64, Solid | Hanging ),

		// Floor gore
		new( 10, "bloody_mess", ThingCategory.Gore, "PLAY", "W" ),
		new( 12, "bloody_mess_2", ThingCategory.Gore, "PLAY", "W" ),
		new( 15, "dead_player", ThingCategory.Gore, "PLAY", "N" ),
		new( 18, "dead_zombieman", ThingCategory.Gore, "POSS", "L" ),
		new( 19, "dead_shotgun_guy", ThingCategory.Gore, "SPOS", "L" ),
		new( 20, "dead_imp", ThingCategory.Gore, "TROO", "M" ),
		new( 21, "dead_demon", ThingCategory.Gore, "SARG", "N" ),
		new( 22, "dead_cacodemon", ThingCategory.Gore, "HEAD", "L" ),
		new( 23, "dead_lost_soul", ThingCategory.Gore, "SKUL", "K" ),
		new( 24, "pool_of_blood_and_flesh", ThingCategory.Gore, "POL5" ),
		new( 79, "pool_of_blood", ThingCategory.Gore, "POB1" ),
		new( 80, "pool_of_blood_2", ThingCategory.Gore, "POB2" ),
		new( 81, "pool_of_brains", ThingCategory.Gore, "BRS1" ),
	}.ToDictionary( t => t.Id );
}
