using System;

namespace Source1;

public abstract partial class SourceGameMount
{
	private void ReadWeaponScripts( MountContext context, FileIndex files )
	{
		files.Weapons.Clear();
		var skill = ReadSkillConfigs( context, files );
		var ammoResources = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var waveEvents = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var hudFonts = new Lazy<HudFonts>( () => ReadHudFonts( files ) );
		foreach ( var entry in WeaponScriptFiles( files ) )
		{
			try
			{
				using var stream = entry.Value.Open();
				var bytes = new byte[checked((int)stream.Length)];
				stream.ReadExactly( bytes );
				var root = WeaponScript.Parse( WeaponScript.Decode( bytes, entry.Key, WeaponScriptKey ), entry.Key );
				var data = WeaponScript.Read( root, entry.Key, skill, this );
				if ( data is null ) continue;
				data.Icon = MountWeaponIcon( context, files, entry.Key, SourceHudTexture.Read( root, "weapon" ) ?? SourceHudTexture.Read( root, "weapon_s" ), hudFonts );
				data.PrimaryAmmoResource = AmmoResource( context, data.PrimaryAmmo );
				data.SecondaryAmmoResource = AmmoResource( context, data.SecondaryAmmo );
				RegisterWeaponSounds( context, data, entry.Key, waveEvents );
				context.Add( ResourceType.Text, entry.Key + ".json", new WeaponScriptLoader( data ) );
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

		string AmmoResource( MountContext context, string name )
		{
			if ( string.IsNullOrEmpty( name ) ) return null;
			var (title, limit) = AmmoDefaults( name );
			if ( (skill.AmmoCapacity( name ) ?? limit ) is not { } capacity ) return null;
			var path = $"scripts/ammo/{Uri.EscapeDataString( name.ToLowerInvariant() )}.ammo";
			if ( ammoResources.Add( path ) ) context.Add( ResourceType.GameResource, path, new AmmoLoader( title, capacity ) );
			return $"mount://{Ident}/{path}";
		}
	}

	private static SkillConfig ReadSkillConfigs( MountContext context, FileIndex files )
	{
		var skill = new SkillConfig();
		// Expose supplemental game settings independently. Importing an Episode's
		// resources must not replace the base game's weapon balance globally.
		foreach ( var entry in files.Files.Where( x => x.Key.StartsWith( "cfg/skill", StringComparison.OrdinalIgnoreCase )
			&& x.Key.EndsWith( ".cfg", StringComparison.OrdinalIgnoreCase ) ) )
		{
			var values = SkillConfig.Parse( entry.Value.ReadText() );
			if ( entry.Key.Equals( "cfg/skill.cfg", StringComparison.OrdinalIgnoreCase ) ) skill = values;
			if ( values.Values.Count > 0 ) context.Add( ResourceType.Text, entry.Key + ".json", new SkillConfigLoader( values ) );
		}
		return skill;
	}

	private void RegisterWeaponSounds( MountContext context, SourceModelWeaponData data, string script, HashSet<string> registered )
	{
		foreach ( var (role, sound) in data.Sounds.ToArray() )
		{
			var path = $"sounds/events/{Uri.EscapeDataString( sound.ToLowerInvariant() )}.sound";
			if ( sound.EndsWith( ".wav", StringComparison.OrdinalIgnoreCase ) || sound.EndsWith( ".mp3", StringComparison.OrdinalIgnoreCase ) )
			{
				var definition = ResolveSurfaceSound( SourceSoundDefinition.FromWave( sound, script ) );
				if ( definition is null )
				{
					data.Sounds.Remove( role );
					continue;
				}
				if ( registered.Add( path ) ) context.Add( ResourceType.GameResource, path, new SurfaceSoundLoader( definition ) );
			}
			data.Sounds[role] = $"mount://{Ident}/{path}";
		}
	}

	protected internal virtual bool KeepAttackCooldown( BaseCombatWeapon weapon ) => false;
	protected internal virtual bool ResetFireOnHolsteredReload => false;
	protected internal virtual bool UseDrawSequenceDuration => false;

	protected internal virtual BaseAmmoResource CreateAmmoResource() => new();

	internal virtual ShellEjection ReadBrassEvent( SourceModelSequenceData.Event e ) => null;

	internal virtual bool DrawMuzzleFlash( string type, GameObject attachment, bool firstPerson, float scale ) => false;

	internal virtual SourceModelWeaponData CreateWeaponData( VmtParameters script ) => new();

	internal virtual void ApplyWeaponDefaults( BaseCombatWeapon weapon, WeaponSetup setup, string worldModel ) { }

	/// <summary>The ICE key of this game's encrypted <c>.ctx</c> weapon scripts, if it ships any.</summary>
	protected virtual string WeaponScriptKey => null;

	/// <summary>The pickup title and fallback reserve limit for a weapon script's ammo type.</summary>
	private protected virtual (string Title, int? Capacity) AmmoDefaults( string ammo ) => (ammo.ToTitleCase(), null);

	protected virtual string WeaponScriptPrefix => "scripts/weapon_";

	// Source reads a plain .txt script in preference to its encrypted .ctx counterpart.
	private IEnumerable<KeyValuePair<string, SourceFile>> WeaponScriptFiles( FileIndex files )
	{
		var prefix = WeaponScriptPrefix;
		return files.Files.Where( x => x.Key.StartsWith( prefix, StringComparison.OrdinalIgnoreCase )
			&& x.Key.IndexOf( '/', prefix.Length ) < 0
			&& (x.Key.EndsWith( ".txt", StringComparison.OrdinalIgnoreCase )
				|| (WeaponScriptKey is not null && x.Key.EndsWith( ".ctx", StringComparison.OrdinalIgnoreCase ) && !files.Files.ContainsKey( Path.ChangeExtension( x.Key, ".txt" ) ))) )
			.OrderBy( x => x.Key );
	}

	internal SourceModelWeaponData FindWeaponScript( string view, string world, string fallback, string weaponClass = null )
	{
		using var files = AcquireIndex();
		return Find( view ) ?? Find( world ) ?? Find( fallback );

		SourceModelWeaponData Find( string model )
		{
			if ( model is null || !files.Weapons.TryGetValue( model, out var scripts ) ) return null;
			// NPC weapons can share a viewmodel with a player weapon.
			return scripts.Find( x => x.ClassName == weaponClass )
				?? scripts.Find( x => x.WorldModel == world )
				?? scripts.Find( x => x.ClassName.StartsWith( "weapon_", StringComparison.OrdinalIgnoreCase )
					&& x.ViewModel?.EndsWith( "/v_" + x.ClassName["weapon_".Length..] + ".mdl", StringComparison.OrdinalIgnoreCase ) == true )
				?? scripts.FirstOrDefault();
		}
	}

	internal SourceModelWeaponData[] GetWeaponData( string model )
	{
		using var files = AcquireIndex();
		return files.Weapons.GetValueOrDefault( model )?.ToArray();
	}
}
