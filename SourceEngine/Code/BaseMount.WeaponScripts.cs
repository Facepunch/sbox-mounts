using System;

namespace Source1;

public abstract partial class BaseMount
{
	private void ReadWeaponScripts( MountContext context, FileIndex files )
	{
		files.Weapons.Clear();
		var skill = SourceSkillConfig.Parse( "" );
		// Expose supplemental game settings independently. Importing an Episode's
		// resources must not replace the base game's weapon balance globally.
		foreach ( var entry in files.Files.Where( x => x.Key.StartsWith( "cfg/skill", StringComparison.OrdinalIgnoreCase )
			&& x.Key.EndsWith( ".cfg", StringComparison.OrdinalIgnoreCase ) ) )
		{
			using var stream = entry.Value.Open();
			using var reader = new StreamReader( stream );
			var values = SourceSkillConfig.Parse( reader.ReadToEnd() );
			if ( entry.Key.Equals( "cfg/skill.cfg", StringComparison.OrdinalIgnoreCase ) ) skill = values;
			if ( values.Values.Count > 0 ) context.Add( ResourceType.Text, entry.Key + ".json", new SourceSkillConfigLoader( values ) );
		}
		var ammoResources = new Dictionary<string, SourceAmmoLoader>( StringComparer.OrdinalIgnoreCase );
		var waveEvents = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var hudFonts = new Lazy<HudFonts>( () => ReadHudFonts( files ) );
		string AmmoResource( string name )
		{
			if ( skill.AmmoCapacity( name ) is not { } capacity ) return null;
			var path = $"scripts/ammo/{Uri.EscapeDataString( name.ToLowerInvariant() )}.ammo";
			ammoResources.TryAdd( path, new SourceAmmoLoader( name, capacity ) );
			return $"mount://{Ident}/{path}";
		}
		foreach ( var entry in WeaponScriptFiles( files ) )
		{
			try
			{
				using var stream = entry.Value.Open();
				var bytes = new byte[checked((int)stream.Length)];
				stream.ReadExactly( bytes );
				var root = SourceWeaponScript.Parse( SourceWeaponScript.Decode( bytes, entry.Key, WeaponScriptKey ), entry.Key );
				var data = SourceWeaponScript.Read( root, entry.Key, skill );
				if ( data is null ) continue;
				data.Icon = MountWeaponIcon( context, files, entry.Key, SourceHudTexture.Read( root, "weapon" ), hudFonts );
				data.PrimaryAmmoResource = AmmoResource( data.PrimaryAmmo );
				data.SecondaryAmmoResource = AmmoResource( data.SecondaryAmmo );
				foreach ( var (role, sound) in data.Sounds.ToArray() )
				{
					var path = $"sounds/events/{Uri.EscapeDataString( sound.ToLowerInvariant() )}.sound";
					if ( sound.EndsWith( ".wav", StringComparison.OrdinalIgnoreCase ) || sound.EndsWith( ".mp3", StringComparison.OrdinalIgnoreCase ) )
					{
						var definition = ResolveSurfaceSound( SourceSoundDefinition.FromWave( sound, entry.Key ) );
						if ( definition is null )
						{
							data.Sounds.Remove( role );
							continue;
						}
						if ( waveEvents.Add( path ) ) context.Add( ResourceType.GameResource, path, new SurfaceSoundLoader( definition ) );
					}
					data.Sounds[role] = $"mount://{Ident}/{path}";
				}
				context.Add( ResourceType.Text, entry.Key + ".json", new SourceWeaponScriptLoader( data ) );
				foreach ( var model in new[] { data.ViewModel, data.WorldModel }.Where( x => x is not null ).Distinct() )
				{
					if ( !files.Weapons.TryGetValue( model, out var definitions ) ) files.Weapons.Add( model, definitions = [] );
					definitions.Add( data );
				}
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
			{
				Log.Trace( $"{Title}: weapon script '{entry.Key}' was not imported: {e.Message}" );
			}
		}
		foreach ( var ammo in ammoResources ) context.Add( ResourceType.GameResource, ammo.Key, ammo.Value );
	}

	/// <summary>The ICE key of this game's encrypted <c>.ctx</c> weapon scripts, if it ships any.</summary>
	protected virtual string WeaponScriptKey => null;

	// Source reads a plain .txt script in preference to its encrypted .ctx counterpart.
	private IEnumerable<KeyValuePair<string, SourceFile>> WeaponScriptFiles( FileIndex files ) => files.Files
		.Where( x => x.Key.StartsWith( "scripts/weapon_", StringComparison.OrdinalIgnoreCase )
			&& (x.Key.EndsWith( ".txt", StringComparison.OrdinalIgnoreCase )
				|| (WeaponScriptKey is not null && x.Key.EndsWith( ".ctx", StringComparison.OrdinalIgnoreCase ) && !files.Files.ContainsKey( Path.ChangeExtension( x.Key, ".txt" ) ))) )
		.OrderBy( x => x.Key );

	internal ModelWeaponData[] GetWeaponData( string model )
	{
		using var files = AcquireIndex();
		return files.Weapons.GetValueOrDefault( model )?.ToArray();
	}
}
