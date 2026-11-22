using System;
using GoldSrc.Bsp;

namespace GoldSrc.Maps;

partial class MapBuilder
{
	private bool SpawnSpecialTriggerVolume( Entity entity, BspModel model )
	{
		if ( entity.ClassName is not ("trigger_teleport" or "trigger_gravity") ) return false;
		var center = (model.Mins + model.Maxs) * 0.5f;
		var go = new GameObject( true, EntityName( entity ) ) { IsStatic = true };
		go.WorldPosition = entity.Origin + center;
		AddCollider( go, GetTriggerPhysics( entity.BrushModelIndex, model.Maxs - model.Mins ), true, trigger: true );
		var trigger = go.AddComponent<MovementTrigger>();
		trigger.TargetName = entity.TargetName;
		trigger.Kind = entity.ClassName == "trigger_teleport" ? MovementTrigger.TriggerKind.Teleport : MovementTrigger.TriggerKind.Gravity;
		trigger.NoClients = (entity.SpawnFlags & 2) != 0;
		trigger.AllowMonsters = (entity.SpawnFlags & 1) != 0;
		trigger.Gravity = entity.GetFloat( "gravity" );
		RegisterTarget( entity, trigger, null, null, null, nameof( MapEntity.Kill ) );
		return true;
	}

	private static float DefaultSpeed( Entity entity, float fallback )
	{
		var speed = entity.GetFloat( "speed" );
		return speed == 0f ? fallback : speed;
	}

	private static Angles BrushAxis( int flags, int rollFlag = 64, int pitchFlag = 128 ) =>
		(flags & rollFlag) != 0 ? new( 0f, 0f, 1f )
		: (flags & pitchFlag) != 0 ? new( 1f, 0f, 0f ) : new( 0f, 1f, 0f );

	private void AddRotatingBrush( GameObject go, Entity entity )
	{
		var flags = entity.SpawnFlags;
		var rotating = go.AddComponent<RotatingBrush>();
		rotating.TargetName = entity.TargetName;
		rotating.StartAngles = entity.Angles;
		rotating.Axis = BrushAxis( flags, 4, 8 ) * ((flags & 2) != 0 ? -1f : 1f);
		rotating.Speed = MathF.Max( entity.GetFloat( "speed" ), 0f );
		rotating.Friction = entity.GetFloat( "fanfriction" ) / 100f;
		if ( rotating.Friction == 0f ) rotating.Friction = 1f;
		rotating.Accelerate = (flags & 16) != 0;
		rotating.StartOn = (flags & 1) != 0;
		rotating.HurtTouch = (flags & 32) != 0;
		rotating.Damage = entity.GetFloat( "dmg" );
		rotating.Volume = Math.Clamp( entity.GetFloat( "Volume" ) / 10f, 0f, 1f );
		if ( rotating.Volume == 0f ) rotating.Volume = 1f;
		var sound = entity.GetString( "message" );
		var index = entity.GetInt( "sounds" );
		if ( string.IsNullOrEmpty( sound ) && index is >= 1 and <= 5 ) sound = $"fans/fan{index}.wav";
		var attenuation = (flags & 128) != 0 ? AttnIdle : (flags & 256) != 0 ? AttnStatic : AttnNorm;
		rotating.MoveSound = BuildSoundEvent( sound, attenuation: attenuation );
		var origin = entity.GetVector3( "spawnorigin" );
		if ( origin != Vector3.Zero ) go.WorldPosition = origin;
		RegisterTarget( entity, rotating );
	}

	private void AddPendulum( GameObject go, Entity entity )
	{
		var pendulum = go.AddComponent<PendulumBrush>();
		pendulum.TargetName = entity.TargetName;
		pendulum.StartAngles = entity.Angles;
		pendulum.Axis = BrushAxis( entity.SpawnFlags );
		pendulum.Distance = entity.GetFloat( "distance" );
		pendulum.MaxSpeed = DefaultSpeed( entity, 100f );
		pendulum.Damping = entity.GetFloat( "damp" ) * 0.001f;
		pendulum.Damage = entity.GetFloat( "dmg" );
		pendulum.StartOn = (entity.SpawnFlags & 1) != 0;
		pendulum.AutoReturn = (entity.SpawnFlags & 16) != 0;
		pendulum.Rope = (entity.SpawnFlags & 2) != 0;
		RegisterTarget( entity, pendulum );
		if ( pendulum.Rope )
			Log.Warning( $"{FilePath}: {EntityName( entity )} requires player movement ownership for its rope attachment." );
	}

	private void AddMomentaryButton( GameObject go, Entity entity )
	{
		var button = go.AddComponent<MomentaryRotButton>();
		button.TargetName = entity.TargetName;
		button.Target = entity.Target;
		button.StartAngles = entity.Angles;
		button.Axis = BrushAxis( entity.SpawnFlags );
		button.Distance = entity.GetFloat( "distance" );
		button.Speed = DefaultSpeed( entity, 100f );
		button.ReturnSpeed = entity.GetFloat( "returnspeed" );
		button.AutoReturn = (entity.SpawnFlags & 16) != 0;
		button.UseEnabled = (entity.SpawnFlags & 1) == 0;
		button.MoveSound = GetIndexedSound( entity, "sounds", ButtonSounds );
		RegisterTarget( entity, button );
	}

	private static readonly Dictionary<int, string> PlatformMoveSounds = new()
	{
		[1] = "plats/bigmove1.wav",
		[2] = "plats/bigmove2.wav",
		[3] = "plats/elevmove1.wav",
		[4] = "plats/elevmove2.wav",
		[5] = "plats/elevmove3.wav",
		[6] = "plats/freightmove1.wav",
		[7] = "plats/freightmove2.wav",
		[8] = "plats/heavymove1.wav",
		[9] = "plats/rackmove1.wav",
		[10] = "plats/railmove1.wav",
		[11] = "plats/squeekmove1.wav",
		[12] = "plats/talkmove1.wav",
		[13] = "plats/talkmove2.wav"
	};

	private static readonly Dictionary<int, string> PlatformStopSounds = new()
	{
		[1] = "plats/bigstop1.wav",
		[2] = "plats/bigstop2.wav",
		[3] = "plats/freightstop1.wav",
		[4] = "plats/heavystop2.wav",
		[5] = "plats/rackstop1.wav",
		[6] = "plats/railstop1.wav",
		[7] = "plats/squeekstop1.wav",
		[8] = "plats/talkstop1.wav"
	};

	private void AddPlatform( GameObject go, Entity entity, BspModel model )
	{
		var changing = entity.ClassName is "func_trackchange" or "func_trackautochange";
		PlatformBrush platform = changing ? go.AddComponent<TrackChange>() : go.AddComponent<PlatformBrush>();
		platform.TargetName = entity.TargetName;
		platform.Height = entity.GetFloat( "height" );
		if ( platform.Height == 0f ) platform.Height = model.Maxs.z - model.Mins.z - 8f;
		platform.Speed = DefaultSpeed( entity, 150f );
		platform.AngleOffset = entity.ClassName == "func_plat" ? default
			: BrushAxis( entity.SpawnFlags ) * entity.GetFloat( "rotation" );
		platform.Toggle = changing || (entity.SpawnFlags & 1) != 0;
		platform.StartAtTop = changing ? (entity.SpawnFlags & 8) == 0 : !string.IsNullOrEmpty( entity.TargetName );
		platform.MoveSound = GetIndexedSound( entity, "movesnd", PlatformMoveSounds );
		platform.StopSound = GetIndexedSound( entity, "stopsnd", PlatformStopSounds );
		platform.Volume = entity.GetFloat( "volume" );
		if ( platform.Volume == 0f ) platform.Volume = 0.85f;
		RegisterTarget( entity, platform );
		if ( platform is TrackChange track )
		{
			track.Automatic = entity.ClassName == "func_trackautochange";
			track.DontMove = (entity.SpawnFlags & 16) != 0;
			track.AlarmSound = BuildSoundEvent( "buttons/button11.wav" );
			if ( track.DontMove ) platform.Height = 0f;
			_wiring.Add( () =>
			{
				track.TopTrack = FindPathNode( entity.GetString( "toptrack" ) );
				track.BottomTrack = FindPathNode( entity.GetString( "bottomtrack" ) );
				track.Train = _mapState.Find( entity.GetString( "train" ) ) as TrackTrain;
			} );
		}
		if ( platform.Toggle ) return;
		var min = model.Mins + new Vector3( 25f, 25f, 0f );
		var max = model.Maxs + new Vector3( 25f, 25f, 8f );
		min.z = max.z - (platform.Height + 8f);
		if ( model.Maxs.x - model.Mins.x <= 50f )
		{
			min.x = (model.Mins.x + model.Maxs.x) * 0.5f;
			max.x = min.x + 1f;
		}
		if ( model.Maxs.y - model.Mins.y <= 50f )
		{
			min.y = (model.Mins.y + model.Maxs.y) * 0.5f;
			max.y = min.y + 1f;
		}
		var triggerObject = new GameObject( true, $"{go.Name} trigger" ) { IsStatic = true };
		triggerObject.WorldPosition = entity.Origin + (min + max) * 0.5f;
		var collider = triggerObject.AddComponent<BoxCollider>();
		collider.Scale = max - min;
		collider.IsTrigger = true;
		collider.Static = true;
		triggerObject.AddComponent<PlatformTrigger>().Platform = platform;
	}

	private void AddBreakable( GameObject go, Entity entity, BspModel model )
	{
		var breakable = go.AddComponent<BreakableBrush>();
		breakable.TargetName = entity.TargetName;
		breakable.Health = entity.GetFloat( "health" );
		breakable.MaterialType = entity.GetInt( "material" );
		if ( breakable.MaterialType is < 0 or > 9 ) breakable.MaterialType = 1;
		breakable.TriggerOnly = (entity.SpawnFlags & 1) != 0;
		breakable.BreakOnTouch = (entity.SpawnFlags & 2) != 0;
		breakable.BreakOnPressure = (entity.SpawnFlags & 4) != 0;
		breakable.CrowbarSensitive = (entity.SpawnFlags & 256) != 0;
		breakable.Pushable = entity.ClassName == "func_pushable";
		breakable.PushableBreakable = (entity.SpawnFlags & 128) != 0;
		breakable.PressureDelay = entity.GetFloat( "delay" );
		breakable.PressureTop = model.Maxs.z;
		string[] damageNames = breakable.MaterialType switch
		{
			0 or 6 or 7 => new[] { "glass1", "glass2", "glass3" },
			1 => ["wood1", "wood2", "wood3"],
			2 => ["metal1", "metal3"],
			3 => ["flesh1", "flesh2", "flesh3", "flesh5", "flesh6", "flesh7"],
			4 or 8 => ["concrete1", "concrete2", "concrete3"],
			_ => []
		};
		string[] breakNames = breakable.MaterialType switch
		{
			0 => new[] { "bustglass1", "bustglass2" },
			1 => ["bustcrate1", "bustcrate2"],
			2 or 6 => ["bustmetal1", "bustmetal2"],
			3 => ["bustflesh1", "bustflesh2"],
			4 or 8 => ["bustconcrete1", "bustconcrete2"],
			5 => ["bustceiling"],
			_ => []
		};
		breakable.DamageSounds = damageNames.Select( x => BuildSoundEvent( $"debris/{x}.wav" ) ).ToArray();
		breakable.BreakSounds = breakNames.Select( x => BuildSoundEvent( $"debris/{x}.wav" ) ).ToArray();
		if ( breakable.MaterialType == 6 )
			breakable.MetalDamageSounds = [BuildSoundEvent( "debris/metal1.wav" ), BuildSoundEvent( "debris/metal3.wav" )];
		RegisterTarget( entity, breakable );
		WireTrigger( entity, breakable );
		if ( breakable.Pushable )
			Log.Warning( $"{FilePath}: {EntityName( entity )} requires push-step movement and buoyancy; only its breakable behavior is active." );
		if ( entity.GetInt( "explodemagnitude" ) > 0 )
			Log.Warning( $"{FilePath}: {EntityName( entity )} requires radial explosion damage and temporary explosion effects when broken." );
	}
}
