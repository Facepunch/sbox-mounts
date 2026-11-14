using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Source1;

/// <summary>Reads numeric skill defaults as data. Config commands are never executed.</summary>
internal sealed class SourceSkillConfig
{
	private readonly Dictionary<string, float> values = new( StringComparer.OrdinalIgnoreCase );
	internal IReadOnlyDictionary<string, float> Values => values;

	internal static SourceSkillConfig Parse( string text )
	{
		var result = new SourceSkillConfig();
		foreach ( var commandLine in text.Split( ["\r\n", "\n", "\r"], StringSplitOptions.None ) )
		{
			var line = commandLine;
			var comment = line.IndexOf( "//", StringComparison.Ordinal );
			if ( comment >= 0 ) line = line[..comment];
			foreach ( var command in line.Split( ';' ) )
			{
				var match = Regex.Match( command, "^\\s*\"?(sk_[a-zA-Z0-9_]+)\"?\\s+\"?([-+0-9.eE]+)\"?\\s*$", RegexOptions.CultureInvariant );
				if ( match.Success && float.TryParse( match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) && float.IsFinite( value ) )
					result.values[match.Groups[1].Value] = value;
			}
		}
		return result;
	}

	internal int? AmmoCapacity( string ammo )
	{
		if ( string.IsNullOrWhiteSpace( ammo ) ) return null;
		// Source's crossbow ammo identifier differs from its skill convar suffix.
		var key = Normalize( ammo.Equals( "XBowBolt", StringComparison.OrdinalIgnoreCase ) ? "crossbow" : ammo );
		foreach ( var entry in values )
		{
			if ( !entry.Key.StartsWith( "sk_max_", StringComparison.OrdinalIgnoreCase ) || Normalize( entry.Key[7..] ) != key ) continue;
			if ( entry.Value > 0 && entry.Value < int.MaxValue && entry.Value == MathF.Truncate( entry.Value ) ) return (int)entry.Value;
		}
		return null;
	}

	internal float? WeaponDamage( string ammo, string weaponClass, bool npc = false )
		=> WeaponValue( ammo, weaponClass, npc ? "sk_npc_dmg_" : "sk_plr_dmg_", "" );

	internal float? WeaponRadius( string ammo, string weaponClass ) => WeaponValue( ammo, weaponClass, "sk_", "_radius" );

	private float? WeaponValue( string ammo, string weaponClass, string prefix, string suffix )
	{
		var weapon = weaponClass?.StartsWith( "weapon_", StringComparison.OrdinalIgnoreCase ) == true ? weaponClass[7..] : weaponClass;
		// These Source identifiers are aliases of the config's authored names.
		if ( weapon?.Equals( "frag", StringComparison.OrdinalIgnoreCase ) == true ) weapon = "fraggrenade";
		if ( ammo?.Equals( "XBowBolt", StringComparison.OrdinalIgnoreCase ) == true ) ammo = "crossbow";
		foreach ( var name in new[] { weapon, ammo }.Where( x => !string.IsNullOrWhiteSpace( x ) ) )
		{
			var key = Normalize( prefix + name + suffix );
			foreach ( var entry in values )
				if ( Normalize( entry.Key ) == key && entry.Value >= 0 ) return entry.Value;
		}
		return null;
	}

	private static string Normalize( string value ) => value.Replace( "_", "" ).ToLowerInvariant();
}
