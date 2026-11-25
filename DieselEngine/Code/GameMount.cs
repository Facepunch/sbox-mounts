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
			if ( GetSequenceManager( unit ) is { } manager ) result = UnitEndurance.Read( manager.Root, manager.Object );
		}
		catch ( Exception e ) { Log.Warning( $"PAYDAY endurance {unit}: {e.Message}" ); }
		return endurance[unit] = result;
	}

	readonly Dictionary<string, (ScriptTable Root, XmlNode Object)?> sequenceManagers = new( StringComparer.Ordinal );
	readonly Dictionary<(string Unit, string Sequence), string[]> sequenceSounds = [];

	/// <summary>A unit's parsed sequence manager and object XML, or null. Cached per unit.</summary>
	internal (ScriptTable Root, XmlNode Object)? GetSequenceManager( string unit )
	{
		if ( string.IsNullOrEmpty( unit ) || Database is null ) return null;
		if ( sequenceManagers.TryGetValue( unit, out var cached ) ) return cached;
		(ScriptTable, XmlNode)? result = null;
		try
		{
			if ( Database.Find( "unit", unit ) is { } unitBytes
				&& XmlNode.Parse( Encoding.UTF8.GetString( unitBytes ) ).Element( "object" )?.Attribute( "file" ) is { } objectPath
				&& Database.Find( "object", objectPath ) is { } objectBytes )
			{
				var obj = XmlNode.Parse( Encoding.UTF8.GetString( objectBytes ) );
				if ( obj.Element( "sequence_manager" )?.Attribute( "file" ) is { } managerPath && Database.Find( "sequence_manager", managerPath ) is { } managerBytes )
					result = (ScriptData.Read( managerBytes ), obj);
			}
		}
		catch ( Exception e ) { Log.Warning( $"PAYDAY sequence manager {unit}: {e.Message}" ); }
		return sequenceManagers[unit] = result;
	}

	/// <summary>Mounted sound paths a unit's sequence plays (only events some bank has). Cached.</summary>
	internal string[] SequenceSoundPaths( string unit, string sequence )
	{
		if ( string.IsNullOrEmpty( sequence ) ) return [];
		if ( sequenceSounds.TryGetValue( (unit, sequence), out var cached ) ) return cached;
		string[] paths = GetSequenceManager( unit ) is { } manager
			? SequenceSounds.Read( manager.Root, sequence ).Select( SoundPath ).Where( p => p is not null ).ToArray() : [];
		return sequenceSounds[(unit, sequence)] = paths;
	}

	/// <summary>Mounted open/close sound paths for a door unit, from its open/close sequences. Either may be null.</summary>
	internal (string Open, string Close) DoorSoundPaths( string unit )
	{
		if ( GetSequenceManager( unit ) is not { } manager ) return (null, null);
		var (open, close) = SequenceSounds.DoorSounds( manager.Root );
		return (SoundPath( open ), SoundPath( close ));
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
		ulong bankType = IdString.Hash( "bnk" );
		var bankPaths = new List<string>();
		var levelDirectories = new List<string>();
		foreach ( var name in Encoding.Latin1.GetString( lookup ).Split( '\0' ).Distinct() )
		{
			ulong hash = IdString.Hash( name );
			if ( Database.Entries.ContainsKey( (bankType, hash) ) ) bankPaths.Add( name );
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
				levelDirectories.Add( name[..name.LastIndexOf( '/' )] );
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
		RegisterSounds( context, bankPaths, levelDirectories );
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

	/// <summary>Event name → short bank name, for every registered named event (map ambience looks sounds up here).</summary>
	internal Dictionary<string, string> SoundEventBanks { get; } = new( StringComparer.OrdinalIgnoreCase );

	readonly Dictionary<string, WwiseBank> soundBanks = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, List<SoundEvent>> loopLayers = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// A named event as natively looping sounds, one per simultaneous layer (Wwise loops each sound on its own, so a
	/// mixed take restarted on a timer gaps and drops shorter layers). Main thread only. Null if the event can't render.
	/// </summary>
	internal List<SoundEvent> GetLoopLayers( string eventName, float distance )
	{
		if ( string.IsNullOrEmpty( eventName ) ) return null;
		string key = $"{eventName}@{distance}";
		if ( loopLayers.TryGetValue( key, out var cached ) ) return cached;
		List<SoundEvent> result = null;
		if ( SoundEventBanks.TryGetValue( eventName, out var bankName ) && soundBanks.TryGetValue( bankName, out var bank ) )
		{
			try
			{
				string name = eventName.ToLowerInvariant();
				var takes = bank.RenderLoopLayers( WwiseBank.Hash( name ), name );
				result = takes.Select( ( wav, i ) => new SoundEvent
				{
					Sounds = [SoundFile.FromWav( $"{PaydaySoundEventLoader.EventPath( bankName, name )}/loop-{i}.vsnd", wav, new SoundFile.LoadOptions { Loop = true } )],
					Volume = 1, Pitch = 1,
					DistanceAttenuation = true, Distance = distance,
					ReverbEnabled = true, AirAbsorption = false
				} ).ToList();
				if ( result.Count == 0 ) result = null;
			}
			catch ( Exception e ) { Log.Warning( $"PAYDAY loop {eventName}: {e.Message}" ); }
		}
		return loopLayers[key] = result;
	}

	/// <summary>Mounted path of a named soundbank event, or null if no bank has it.</summary>
	internal string SoundPath( string eventName ) =>
		!string.IsNullOrEmpty( eventName ) && SoundEventBanks.TryGetValue( eventName, out var bank )
			? $"mount://payday/{PaydaySoundEventLoader.EventPath( bank, eventName.ToLowerInvariant() )}" : null;

	/// <summary>
	/// Every playable event of every soundbank as <c>sounds/&lt;bank&gt;/&lt;event&gt;.sound</c> (harvested name, else
	/// <c>event_&lt;hash&gt;</c>). Streaming-only banks read media from bundle <c>stream</c> entries.
	/// </summary>
	void RegisterSounds( MountContext context, List<string> bankPaths, List<string> levelDirectories )
	{
		var started = DateTime.UtcNow;
		var names = SoundNames.Collect( Database, levelDirectories );
		int events = 0, named = 0, unreadable = 0;
		foreach ( var bankPath in bankPaths.OrderBy( p => p, StringComparer.Ordinal ) )
		{
			string bankName = SoundNames.BankShortName( bankPath );
			WwiseBank bank;
			try { bank = new WwiseBank( Database.Find( "bnk", bankPath ), id => Database.Find( "stream", $"soundbanks/streamed/{bankName}/{id}" ) ); }
			catch ( Exception ) { unreadable++; continue; }
			soundBanks[bankName] = bank;
			foreach ( uint id in bank.EventIds.ToList() )
			{
				if ( !bank.IsPlayable( id ) ) continue;
				string label = names.GetValueOrDefault( id );
				if ( label is not null ) { named++; SoundEventBanks.TryAdd( label, bankName ); }
				label ??= $"event_{id:x8}";
				context.Add( ResourceType.GameResource, PaydaySoundEventLoader.EventPath( bankName, label ), new PaydaySoundEventLoader( bank, id, label ) );
				events++;
			}
		}
		Log.Info( $"PAYDAY: registered {events} sound events ({named} named) from {bankPaths.Count - unreadable} of {bankPaths.Count} banks "
			+ $"in {(DateTime.UtcNow - started).TotalSeconds:0.0}s ({names.Count} candidate names)." );
	}

	protected override void Shutdown()
	{
		SoundEventBanks.Clear();
		soundBanks.Clear();
		loopLayers.Clear();
		viewAssets.Clear();
		WeaponAudio.Clear();
		browserMaterials = null;
		Characters.Clear();
		UnitNames.Clear();
		endurance.Clear();
		sequenceManagers.Clear();
		sequenceSounds.Clear();
		runtimeMaterials = null;
		Database = null;
	}
}
