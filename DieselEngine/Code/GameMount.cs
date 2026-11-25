using System.Text;

namespace Diesel;

public sealed class PaydayMount : BaseGameMount
{
	public override string Ident => "payday";
	public override string Title => "PAYDAY: The Heist";
	public override long? SteamAppId => 24240;
	BaseFileSystem fileSystem;
	internal BundleDatabase Database { get; private set; }
	MapMaterials browserMaterials;
	readonly Dictionary<string, PaydayViewAssets> viewAssets = [];
	internal PaydayViewAssets ViewAssets => GetViewAssets( "amcar4" );
	internal PaydayViewAssets GetViewAssets( string id )
	{
		if ( !viewAssets.TryGetValue( id, out var assets ) )
			viewAssets[id] = assets = new PaydayViewAssets( Database, BrowserMaterials, WeaponDefinition.Get( id ) );
		return assets;
	}
	internal Dictionary<string, WeaponAudio> WeaponAudio { get; } = [];
	internal Dictionary<string, PaydayCharacter> Characters { get; } = [];
	/// <summary>Unit path for each unit IdString in the installed lookup.</summary>
	internal Dictionary<ulong, string> UnitNames { get; } = [];

	readonly Dictionary<string, UnitEndurance> endurance = new( StringComparer.Ordinal );
	MapMaterials runtimeMaterials;

	/// <summary>Materials for geometry rebuilt at runtime (breakables, mission sequences), shared across maps.</summary>
	internal MapMaterials RuntimeMaterials => runtimeMaterials ??= new MapMaterials( Database, "runtime" );

	/// <summary>A unit's damage rules (sequence-manager body endurance), or null if it has none. Cached per unit.</summary>
	internal UnitEndurance GetEndurance( string unit )
	{
		if ( string.IsNullOrEmpty( unit ) || Database is null ) return null;
		if ( endurance.TryGetValue( unit, out var cached ) ) return cached;
		UnitEndurance result = null;
		try
		{
			if ( Database.Find( "unit", unit ) is { } unitBytes
				&& XmlNode.Parse( Encoding.UTF8.GetString( unitBytes ) ).Element( "object" )?.Attribute( "file" ) is { } objectPath
				&& Database.Find( "object", objectPath ) is { } objectBytes )
			{
				var obj = XmlNode.Parse( Encoding.UTF8.GetString( objectBytes ) );
				if ( obj.Element( "sequence_manager" )?.Attribute( "file" ) is { } managerPath && Database.Find( "sequence_manager", managerPath ) is { } managerBytes )
					result = UnitEndurance.Read( ScriptData.Read( managerBytes ), obj );
			}
		}
		catch ( Exception e ) { Log.Warning( $"PAYDAY endurance {unit}: {e.Message}" ); }
		return endurance[unit] = result;
	}
	internal MapMaterials BrowserMaterials => browserMaterials ??= new MapMaterials( Database, "browser" );

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( 24240 ) ) return;
		fileSystem = context.GetFileSystem( 24240 );
		IsInstalled = fileSystem?.FileExists( "assets/all.blb" ) == true;
	}

	protected override Task Mount( MountContext context )
	{
		if ( !IsInstalled ) return Task.CompletedTask;
		Database = new BundleDatabase( path => fileSystem.OpenRead( path ),
			fileSystem.FindFile( "assets", "*_h.bundle", false ).Select( name => $"assets/{name}" ) );
		ulong worldType = IdString.Hash( "world" );
		var worlds = Database.Entries.Values.Where( e => e.Type == worldType ).ToDictionary( e => e.Path );
		ulong textureType = IdString.Hash( "texture" );
		ulong unitType = IdString.Hash( "unit" );
		ulong materialType = IdString.Hash( "material_config" );
		// The installed game supplies its own path dictionary. No external hashlist or extracted assets required.
		var lookup = Database.Find( "idstring_lookup", "idstring_lookup" );
		if ( lookup is null ) throw new InvalidDataException( "PAYDAY idstring lookup is missing." );
		int maps = 0, textures = 0, models = 0, materials = 0;
		foreach ( var name in Encoding.Latin1.GetString( lookup ).Split( '\0' ).Distinct() )
		{
			ulong hash = IdString.Hash( name );
			if ( Database.Entries.ContainsKey( (materialType, hash) ) )
			{
				try
				{
					// A config contains multiple independently named material slots. Hashes
					// preserve identity even when names contain path separators or punctuation.
					foreach ( var (slot, definition) in MaterialConfig.Read( Database.Find( "material_config", name ) ) )
					{
						if ( definition.Hidden ) continue;
						context.Add( ResourceType.Material, $"{name}.material_config/{slot:x16}",
							new DieselMaterialLoader( name, slot, definition.Name ) );
						materials++;
					}
				}
				catch ( Exception e ) { Log.Warning( $"PAYDAY material index {name}: {e.Message}" ); }
			}
			if ( Database.Entries.ContainsKey( (textureType, hash) ) )
			{
				context.Add( ResourceType.Texture, $"{name}.texture", new DieselTextureLoader( name ) );
				textures++;
			}
			if ( Database.Entries.ContainsKey( (unitType, hash) ) )
			{
				// Binary formats (e.g. massunit brushes) reference units by hash only.
				UnitNames.TryAdd( hash, name );
				try
				{
					if ( UnitModel.DescribeForBrowser( Database, name ) is { } info )
					{
						context.Add( ResourceType.Model, $"{name}.unit", new DieselModelLoader( name, info ) );
						models++;
					}
				}
				catch ( Exception e ) { Log.Warning( $"PAYDAY model index {name}: {e.Message}" ); }
			}
			if ( name.StartsWith( "levels/", StringComparison.Ordinal ) && worlds.ContainsKey( hash ) )
			{
				context.Add( ResourceType.Scene, $"{name}.world", new MapLoader( name ) );
				maps++;
			}
		}
		if ( maps == 0 ) throw new InvalidDataException( "No PAYDAY worlds were found in the installed bundles." );
		var hospitalAudio = new WwiseBank( Database.Find( "bnk", "soundbanks/lvl_hos_sfx" ),
			id => Database.Find( "stream", $"soundbanks/streamed/lvl_hos_sfx/{id}" ) );
		context.Add( ResourceType.GameResource, "sounds/doors/hos_icu_doors_open.sound",
			new PaydayWeaponSoundLoader( "hos_icu_doors_open", hospitalAudio ) );
		var banks = WeaponDefinition.All.ToDictionary( w => w.Bank, w => new WwiseBank( Database.Find( "bnk", $"soundbanks/weapon_{w.Bank}" ),
			id => Database.Find( "stream", $"soundbanks/streamed/weapon_{w.Bank}/{id}" ) ) );
		var registeredSounds = new HashSet<string>();
		foreach ( var definition in WeaponDefinition.All )
		{
			var audio = WeaponAudio[definition.Id] = new WeaponAudio( Database, definition, banks );
			foreach ( var (sound, bank) in audio.Events )
				if ( registeredSounds.Add( sound ) )
					context.Add( ResourceType.GameResource, PaydayWeaponSoundLoader.EventPath( sound ), new PaydayWeaponSoundLoader( sound, bank ) );
			context.Add( ResourceType.GameResource, definition.Ammo, new PaydayAmmoLoader( definition ) );
			context.Add( ResourceType.Texture, definition.Icon, new PaydayWeaponIconLoader( definition ) );
			context.Add( ResourceType.PrefabFile, definition.View, new PaydayWeaponPrefabLoader( definition, view: true ) );
			context.Add( ResourceType.PrefabFile, definition.WorldPrefab, new PaydayWeaponPrefabLoader( definition, world: true ) );
			var weapon = new PaydayWeaponPrefabLoader( definition );
			weapon.Tags.Add( "weapon" );
			context.Add( ResourceType.PrefabFile, definition.Root, weapon );
		}
		Log.Info( $"PAYDAY: indexed {Database.Entries.Count} resources, {maps} worlds, {textures} textures, {models} models and {materials} materials." );
		foreach ( var definition in NpcDefinition.All )
		{
			context.Add( ResourceType.Model, definition.Model, new PaydayCharacterLoader( definition ) );
			var npc = new PaydayNpcPrefabLoader( definition ); npc.Tags.Add( "npc" );
			context.Add( ResourceType.PrefabFile, definition.Prefab, npc );
		}
		var alarm = new PaydayAlarmPrefabLoader(); alarm.Tags.Add( "entity" );
		context.Add( ResourceType.PrefabFile, "entities/interactive/alarm_phone.prefab", alarm );
		IsMounted = true;
		return Task.CompletedTask;
	}

	protected override void Shutdown()
	{
		viewAssets.Clear();
		WeaponAudio.Clear();
		browserMaterials = null;
		Characters.Clear();
		UnitNames.Clear();
		endurance.Clear();
		runtimeMaterials = null;
		Database = null;
	}
}
