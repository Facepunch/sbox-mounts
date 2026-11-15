using System;

namespace Source1;

/// <summary>
/// CCSPlayerAnimState's weapon layers on a Counter-Strike player model: the legs are its activities, the weapon layers are found by name,
/// &lt;stance&gt;_&lt;layer&gt;_&lt;extension&gt;, such as Walk_Upper_AK.
/// </summary>
public static class CssPlayerAnimState
{
	static readonly string[] Stances = ["Idle", "Walk", "Run"];

	/// <summary>A shotgun reloads by looping a shell insert.</summary>
	const string ReloadLoopSuffix = "_loop";

	/// <summary>The dual Elites shoot from the right and left gun in turn.</summary>
	static readonly string[] AlternatingShotSuffixes = ["_R", "_L"];

	const string UpperPrefix = "Idle_Upper_";

	/// <summary>The acttable's legs with the player model's weapon layers over them, or null for a model without weapon sequences.</summary>
	public static SourceNpcWeaponLayers Resolve( Model model, SourceModelSequences sequences, BaseCombatWeapon weapon, Model weaponModel, SourceNpcWeaponLayers legs )
	{
		if ( PlayerAnimationExtension( sequences, weapon, weaponModel ) is not { } extension )
			return null;

		string SequenceName( string name ) => sequences.Get( name ) is { } sequence && model.AnimationClips().ContainsKey( sequence.Name ) ? sequence.Name : null;

		var upper = new string[SourceNpcWeaponLayers.Stances];
		var reloads = new string[SourceNpcWeaponLayers.Stances];
		var shots = new string[SourceNpcWeaponLayers.Stances][];
		var loops = false;
		for ( var i = 0; i < Stances.Length; i++ )
		{
			var stance = Stances[i];
			upper[i] = SequenceName( $"{stance}_Upper_{extension}" );
			reloads[i] = SequenceName( $"{stance}_Reload_{extension}" );
			if ( reloads[i] is null && SequenceName( $"{stance}_Reload_{extension}{ReloadLoopSuffix}" ) is { } loop )
			{
				reloads[i] = loop;
				loops = true;
			}

			shots[i] = SequenceName( $"{stance}_Shoot_{extension}" ) is { } single ? [single]
				: AlternatingShotSuffixes.Select( x => SequenceName( $"{stance}_Shoot_{extension}{x}" ) ).Where( x => x is not null ).ToArray();
		}

		return legs with { Upper = upper, Reloads = reloads, Shots = shots, ReloadLoops = loops };
	}

	/// <summary>The extension each well-known hold's weapons share; the rest borrow a neighbour's (<see cref="SourceWeaponHolds"/>).</summary>
	static readonly Dictionary<string, string> HoldExtensions = new( StringComparer.OrdinalIgnoreCase )
	{
		[WeaponHolds.Pistol] = "PISTOL",
		[WeaponHolds.DualPistols] = "ELITES",
		[WeaponHolds.Smg] = "MP5",
		[WeaponHolds.Rifle] = "AK",
		[WeaponHolds.Shotgun] = "M3S90",
		[WeaponHolds.Melee] = "KNIFE",
		[WeaponHolds.Knife] = "KNIFE",
		[WeaponHolds.Throwable] = "GREN",
		[WeaponHolds.Item] = "C4",
	};

	/// <summary>
	/// The suffix of a player model's weapon sequences, such as AK in Idle_Upper_AK: the weapon script's PlayerAnimationExtension (for a
	/// Counter-Strike weapon without its script, the suffix its world model's name matches), else the weapon's HoldType when the model
	/// has it, else the suffix for its well-known hold or the closest one the model has, else a suffix the world model's name matches.
	/// Null for a model without weapon sequences, or when nothing fits and the weapon is held unarmed.
	/// </summary>
	static string PlayerAnimationExtension( SourceModelSequences sequences, BaseCombatWeapon weapon, Model weaponModel )
	{
		var extensions = sequences.All.Select( x => x.Name ).Where( x => x.StartsWith( UpperPrefix, StringComparison.OrdinalIgnoreCase ) ).Select( x => x[UpperPrefix.Length..] ).ToArray();
		if ( extensions.Length == 0 )
			return null;

		string Find( string name ) => extensions.FirstOrDefault( x => x.Equals( name, StringComparison.OrdinalIgnoreCase ) );

		var scripted = SourceWeaponSetup.WeaponData( weapon )?.PlayerAnimationExtension ?? weaponModel?.GetData<SourceModelWeaponData[]>()?.FirstOrDefault()?.PlayerAnimationExtension;
		if ( Find( scripted ) is { } match )
			return match;

		if ( CssRules.Owns( weapon ) && NamedExtension( extensions, weaponModel ) is { } named )
			return named;

		if ( Find( weapon?.HoldType ) is { } holdType )
			return holdType;

		if ( SourceWeaponHolds.For( weapon ).Select( x => Find( HoldExtensions.GetValueOrDefault( x ) ) ).FirstOrDefault( x => x is not null ) is { } held )
			return held;

		return NamedExtension( extensions, weaponModel );
	}

	/// <summary>The model's suffix sharing the longest start with a part of the world model's name (w_rif_ak47 holds an AK, w_pist_usp a PISTOL).</summary>
	static string NamedExtension( string[] extensions, Model weaponModel )
	{
		var parts = System.IO.Path.GetFileName( weaponModel?.Name ?? "" ).Split( '.' )[0].Split( '_' );
		static int Shared( string a, string b )
		{
			var length = 0;
			while ( length < a.Length && length < b.Length && char.ToLowerInvariant( a[length] ) == char.ToLowerInvariant( b[length] ) )
				length++;
			return length;
		}

		var best = extensions.MaxBy( x => parts.Max( part => Shared( part, x ) ) );
		return parts.Max( part => Shared( part, best ) ) >= 2 ? best : null;
	}
}
