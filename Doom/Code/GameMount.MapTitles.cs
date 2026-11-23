using System;

namespace Doom;

partial class DoomMount
{
	/// <summary>The titles the intermission screens and automap give each map.</summary>
	public override string GetMapTitle( string wad, string mapName ) => wad.ToLowerInvariant() switch
	{
		"doom" => mapName.ToUpperInvariant() switch
		{
			// Knee-Deep in the Dead
			"E1M1" => "Hangar",
			"E1M2" => "Nuclear Plant",
			"E1M3" => "Toxin Refinery",
			"E1M4" => "Command Control",
			"E1M5" => "Phobos Lab",
			"E1M6" => "Central Processing",
			"E1M7" => "Computer Station",
			"E1M8" => "Phobos Anomaly",
			"E1M9" => "Military Base",

			// The Shores of Hell
			"E2M1" => "Deimos Anomaly",
			"E2M2" => "Containment Area",
			"E2M3" => "Refinery",
			"E2M4" => "Deimos Lab",
			"E2M5" => "Command Center",
			"E2M6" => "Halls of the Damned",
			"E2M7" => "Spawning Vats",
			"E2M8" => "Tower of Babel",
			"E2M9" => "Fortress of Mystery",

			// Inferno
			"E3M1" => "Hell Keep",
			"E3M2" => "Slough of Despair",
			"E3M3" => "Pandemonium",
			"E3M4" => "House of Pain",
			"E3M5" => "Unholy Cathedral",
			"E3M6" => "Mt. Erebus",
			"E3M7" => "Limbo",
			"E3M8" => "Dis",
			"E3M9" => "Warrens",

			// Thy Flesh Consumed
			"E4M1" => "Hell Beneath",
			"E4M2" => "Perfect Hatred",
			"E4M3" => "Sever the Wicked",
			"E4M4" => "Unruly Evil",
			"E4M5" => "They Will Repent",
			"E4M6" => "Against Thee Wickedly",
			"E4M7" => "And Hell Followed",
			"E4M8" => "Unto the Cruel",
			"E4M9" => "Fear",

			_ => null,
		},

		"sigil" => mapName.ToUpperInvariant() switch
		{
			"E5M1" => "Baphomet's Demesne",
			"E5M2" => "Sheol",
			"E5M3" => "Cages of the Damned",
			"E5M4" => "Paths of Wretchedness",
			"E5M5" => "Abaddon's Void",
			"E5M6" => "Unspeakable Persecution",
			"E5M7" => "Nightmare Underworld",
			"E5M8" => "Halls of Perdition",
			"E5M9" => "Realm of Iblis",
			_ => null,
		},

		"sigil2" => mapName.ToUpperInvariant() switch
		{
			"E6M1" => "Cursed Darkness",
			"E6M2" => "Violent Hatred",
			"E6M3" => "Twilight Desolation",
			"E6M4" => "Fragments of Sanity",
			"E6M5" => "Wrath of Reason",
			"E6M6" => "Vengeance Unleashed",
			"E6M7" => "Descent Into Terror",
			"E6M8" => "Abyss of Despair",
			"E6M9" => "Shattered Homecoming",
			_ => null,
		},

		"doom2" => MapNumber( mapName ) switch
		{
			1 => "Entryway",
			2 => "Underhalls",
			3 => "The Gantlet",
			4 => "The Focus",
			5 => "The Waste Tunnels",
			6 => "The Crusher",
			7 => "Dead Simple",
			8 => "Tricks and Traps",
			9 => "The Pit",
			10 => "Refueling Base",
			11 => "'O' of Destruction!",
			12 => "The Factory",
			13 => "Downtown",
			14 => "The Inmost Dens",
			15 => "Industrial Zone",
			16 => "Suburbs",
			17 => "Tenements",
			18 => "The Courtyard",
			19 => "The Citadel",
			20 => "Gotcha!",
			21 => "Nirvana",
			22 => "The Catacombs",
			23 => "Barrels o' Fun",
			24 => "The Chasm",
			25 => "Bloodfalls",
			26 => "The Abandoned Mines",
			27 => "Monster Condo",
			28 => "The Spirit World",
			29 => "The Living End",
			30 => "Icon of Sin",
			31 => "Wolfenstein",
			32 => "Grosse",
			_ => null,
		},

		"tnt" => MapNumber( mapName ) switch
		{
			1 => "System Control",
			2 => "Human BBQ",
			3 => "Power Control",
			4 => "Wormhole",
			5 => "Hanger",
			6 => "Open Season",
			7 => "Prison",
			8 => "Metal",
			9 => "Stronghold",
			10 => "Redemption",
			11 => "Storage Facility",
			12 => "Crater",
			13 => "Nukage Processing",
			14 => "Steel Works",
			15 => "Dead Zone",
			16 => "Deepest Reaches",
			17 => "Processing Area",
			18 => "Mill",
			19 => "Shipping/Respawning",
			20 => "Central Processing",
			21 => "Administration Center",
			22 => "Habitat",
			23 => "Lunar Mining Project",
			24 => "Quarry",
			25 => "Baron's Den",
			26 => "Ballistyx",
			27 => "Mount Pain",
			28 => "Heck",
			29 => "River Styx",
			30 => "Last Call",
			31 => "Pharaoh",
			32 => "Caribbean",
			_ => null,
		},

		"plutonia" => MapNumber( mapName ) switch
		{
			1 => "Congo",
			2 => "Well of Souls",
			3 => "Aztec",
			4 => "Caged",
			5 => "Ghost Town",
			6 => "Baron's Lair",
			7 => "Caughtyard",
			8 => "Realm",
			9 => "Abattoire",
			10 => "Onslaught",
			11 => "Hunted",
			12 => "Speed",
			13 => "The Crypt",
			14 => "Genesis",
			15 => "The Twilight",
			16 => "The Omen",
			17 => "Compound",
			18 => "Neurosphere",
			19 => "NME",
			20 => "The Death Domain",
			21 => "Slayer",
			22 => "Impossible Mission",
			23 => "Tombstone",
			24 => "The Final Frontier",
			25 => "The Temple of Darkness",
			26 => "Bunker",
			27 => "Anti-Christ",
			28 => "The Sewers",
			29 => "Odyssey of Noises",
			30 => "The Gateway of Hell",
			31 => "Cyberden",
			32 => "Go 2 It",
			_ => null,
		},

		// No Rest for the Living
		"nerve" => MapNumber( mapName ) switch
		{
			1 => "The Earth Base",
			2 => "The Pain Labs",
			3 => "Canyon of the Dead",
			4 => "Hell Mountain",
			5 => "Vivisection",
			6 => "Inferno of Blood",
			7 => "Baron's Banquet",
			8 => "Tomb of Malevolence",
			9 => "March of the Demons",
			_ => null,
		},

		_ => null,
	};

	/// <summary>The number of a MAPxx map, or 0 for any other name.</summary>
	private int MapNumber( string mapName )
		=> mapName.StartsWith( "MAP", StringComparison.OrdinalIgnoreCase ) && int.TryParse( mapName[3..], out int number ) ? number : 0;
}
