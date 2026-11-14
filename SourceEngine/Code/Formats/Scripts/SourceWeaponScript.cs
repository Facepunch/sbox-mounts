using System;
using System.Text;

namespace Source1;

/// <summary>Reads a Source <c>WeaponData</c> script. Sound roles map to script sound names, not mounted resources.</summary>
internal static class SourceWeaponScript
{
	internal static string Decode( byte[] data, string path, string iceKey )
	{
		if ( path.EndsWith( ".ctx", StringComparison.OrdinalIgnoreCase ) )
		{
			if ( iceKey is null )
				throw new NotSupportedException( $"{path}: encrypted weapon script has no known key." );
			new IceKey( Encoding.ASCII.GetBytes( iceKey ) ).Decrypt( data );
		}
		return Encoding.UTF8.GetString( data ).TrimEnd( '\0' );
	}

	internal static ModelWeaponData Read( string text, string path, SourceSkillConfig skill ) => Read( Parse( text, path ), path, skill );

	/// <summary>The script's <c>WeaponData</c> block, or null.</summary>
	internal static VmtNode Parse( string text, string path ) =>
		// HUD font glyphs such as "\" are literal: Source reads weapon scripts without escape sequences.
		VmtFile.ParseKeyValues( text, path, diagnostic => Log.Trace( $"{diagnostic.Path}:{diagnostic.Line}: {diagnostic.Message}" ), escapeSequences: false )
			.FirstOrDefault( x => x.IsBlock && x.Name.Equals( "WeaponData", StringComparison.OrdinalIgnoreCase ) );

	internal static ModelWeaponData Read( VmtNode root, string path, SourceSkillConfig skill )
	{
		if ( root is null )
			return null;

		var p = new VmtParameters( root.Values, path );
		var weaponClass = Path.GetFileNameWithoutExtension( path );
		var primaryAmmo = p.String( "primary_ammo" );
		var secondaryAmmo = p.String( "secondary_ammo" );
		var damage = p.Contains( "Damage" ) ? p.Float( "Damage" ) : (float?)null;
		var data = new ModelWeaponData
		{
			ClassName = weaponClass,
			ViewModel = ModelPath( p, "viewmodel" ),
			WorldModel = ModelPath( p, "playermodel" ),
			SilencerModel = ModelPath( p, "SilencerModel" ),
			PrimaryAmmo = primaryAmmo,
			SecondaryAmmo = secondaryAmmo,
			PrintName = p.String( "printname" ),
			PlayerDamage = damage ?? skill.WeaponDamage( primaryAmmo, weaponClass ),
			NpcDamage = damage ?? skill.WeaponDamage( primaryAmmo, weaponClass, npc: true ),
			SecondaryPlayerDamage = skill.WeaponDamage( secondaryAmmo, null ),
			SecondaryNpcDamage = skill.WeaponDamage( secondaryAmmo, null, npc: true ),
			BlastRadius = skill.WeaponRadius( primaryAmmo, weaponClass ),
			SecondaryBlastRadius = skill.WeaponRadius( secondaryAmmo, null ),
			ClipSize = p.Integer( "clip_size", -1 ),
			SecondaryClipSize = p.Integer( "clip2_size", -1 ),
			Bucket = p.Integer( "bucket", 0 ),
			BucketPosition = p.Integer( "bucket_position", -1 ) is var position && position >= 0 ? position : null,
			Weight = p.Integer( "weight", -1 ) is var weight && weight >= 0 ? weight : null,
			CycleTime = p.Contains( "CycleTime" ) ? p.Float( "CycleTime" ) : null,
			Bullets = p.Contains( "Bullets" ) ? p.Integer( "Bullets" ) : null,
			FullAuto = p.Contains( "FullAuto" ) ? p.Boolean( "FullAuto" ) : null,
			Range = p.Contains( "Range" ) ? p.Float( "Range" ) : null,
			RangeModifier = p.Contains( "RangeModifier" ) ? p.Float( "RangeModifier" ) : null,
			WeaponType = p.String( "WeaponType" ),
			Team = p.String( "Team" ),
			PlayerAnimationExtension = p.String( "PlayerAnimationExtension" ),
			BuiltRightHanded = p.Contains( "BuiltRightHanded" ) ? p.Boolean( "BuiltRightHanded" ) : null,
			MuzzleFlashStyle = p.String( "MuzzleFlashStyle" ),
			MuzzleFlashScale = p.Contains( "MuzzleFlashScale" ) ? p.Float( "MuzzleFlashScale" ) : null
		};
		foreach ( var sound in root.Children.Where( x => x.IsBlock && x.Name.Equals( "SoundData", StringComparison.OrdinalIgnoreCase ) ).SelectMany( x => x.Children ) )
		{
			if ( !sound.IsBlock && !string.IsNullOrWhiteSpace( sound.Value ) )
				data.Sounds.TryAdd( sound.Name.ToLowerInvariant(), sound.Value );
		}
		return data;
	}

	private static string ModelPath( VmtParameters p, string key ) =>
		p.String( key ) is { } value && !string.IsNullOrWhiteSpace( value ) ? ScriptManifest.ContentPath( value ) : null;
}
