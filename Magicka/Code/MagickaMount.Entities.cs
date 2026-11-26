using System;
using System.Globalization;

namespace Magicka;

public sealed partial class MagickaMount
{
	public const string NpcTag = "npc";

	/// <summary>
	/// The curated roster: one or two archetypes per family. Everything else in the game's 338
	/// templates shares a model or an attack pattern with one of these.
	/// </summary>
	internal static readonly HashSet<string> Roster = new( StringComparer.OrdinalIgnoreCase )
	{
		// Friendly and ambient
		"citizen_male00",
		"villager_male00_pitchfork",
		"scholar_reading",
		"wizard",
		"wizard_support",

		// Goblin & Orc
		"goblin_warrior",
		"goblin_shaman",
		"orc_warrior",
		"orc_captain",

		// Trolls
		"troll_stone",
		"troll_snow",
		"troll_spirit",

		// Undead & Spirits
		"skeleton_swordsman",
		"skeleton_darksoul_arcane",
		"ghoul",
		"ghost",
		"necromancer",

		// Soldiers, Dwarves & Elves
		"soldier_swordsman",
		"soldier_archer",
		"dwarf_warrior",
		"dwarf_mage",
		"elf_pathfinder",
		"elf_mage",

		// Beasts, Spiders & Beholder
		"beastman_brute",
		"beastman_chieftain",
		"spider_forest",
		"beholder_malign",

		// Daemons
		"daemon_lesser",
		"daemon_greater",
		"daemon_lord",

		// Elementals (colored variants)
		"elemental_fire",
		"elemental_cold",
		"elemental_arcane",
		"elemental_lightning",
		"elemental_life",
		"elemental_water",

		// Dungeon & Grimnir
		"dungeons_gargoyle",
		"dungeons_acolyte_b",
		"grimnirs_starspawn",

		// Iconic Bosses & Characters
		"boss_warlord",
		"boss_cult_leader",
		"vlad_diplomat",
		"death",
	};

	/// <summary>Registers spawnable character prefabs tagged <c>npc</c>, built lazily when loaded.</summary>
	private int MountEntities( MountContext context, List<IndexedTemplate> templates, HashSet<string> registeredModels )
	{
		var registered = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var rosterCount = 0;
		var extraCount = 0;

		foreach ( var (file, template) in templates )
		{
			var (modelPath, variant) = FindPrimaryModel( file, template, registeredModels );
			if ( modelPath is null )
				continue;

			var id = SanitizeId( string.IsNullOrWhiteSpace( template.Id ) ? Path.GetFileNameWithoutExtension( file ) : template.Id );
			if ( string.IsNullOrEmpty( id ) || !registered.Add( id ) )
				continue;

			var title = FormatTitle( id );
			var loader = new NpcPrefabLoader( template, file, modelPath, variant, title );
			if ( Roster.Contains( id ) )
			{
				loader.Tags.Add( NpcTag );
				rosterCount++;
			}
			else
			{
				extraCount++;
			}

			context.Add( ResourceType.PrefabFile, $"entities/npcs/{id}.prefab", loader );
		}

		Log.Info( $"{Title}: {rosterCount} spawnmenu NPCs + {extraCount} level-only NPCs registered" );
		return rosterCount + extraCount;
	}

	private static (string Path, CharacterModelVariant Variant) FindPrimaryModel(
		string templateFile,
		CharacterTemplate template,
		HashSet<string> registeredModels )
	{
		foreach ( var variant in template.Models )
		{
			if ( variant.Model is null )
				continue;

			var path = ResolveExternalPath( templateFile, variant.Model.Path );
			if ( path is not null && registeredModels.Contains( path ) )
				return (path, variant);
		}

		return (null, null);
	}

	internal static string SanitizeId( string raw )
	{
		var chars = new char[raw.Length];
		var length = 0;
		foreach ( var c in raw )
		{
			if ( char.IsLetterOrDigit( c ) || c == '_' || c == '-' )
				chars[length++] = char.ToLowerInvariant( c );
		}

		return new string( chars, 0, length );
	}

	private static string FormatTitle( string id )
	{
		var words = id.Split( ['_', '-'], StringSplitOptions.RemoveEmptyEntries );
		var text = CultureInfo.InvariantCulture.TextInfo;
		for ( var i = 0; i < words.Length; i++ )
			words[i] = text.ToTitleCase( words[i] );

		return string.Join( ' ', words );
	}
}
