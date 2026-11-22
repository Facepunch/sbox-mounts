using System;
using GoldSrc.Bsp;

namespace GoldSrc.Maps;

partial class MapBuilder
{
	private readonly Dictionary<(int Model, BlendMode Blend, bool PivotAtOrigin, char Surface, bool Aggregate), MapGeometry> _brushGeometry = [];

	private void SpawnBrushEntity( BspFile bsp, Entity entity )
	{
		var modelIndex = entity.BrushModelIndex;
		if ( modelIndex >= bsp.Models.Length )
			return;

		var bspModel = bsp.Models[modelIndex];
		var className = entity.ClassName;

		if ( className == "func_monsterclip" )
		{
			var clip = new GameObject( false, EntityName( entity ) ) { IsStatic = true, WorldPosition = entity.Origin };
			clip.Tags.Add( MonsterClip.BrushTag );
			var marker = clip.AddComponent<MonsterClip>();
			var collider = clip.AddComponent<PhysicsCollider>();
			collider.Physics = GetMonsterClipPhysics( bsp, bspModel, modelIndex );
			collider.Static = true;
			collider.ColliderFlags = ColliderFlags.IgnoreTraces;
			var area = clip.AddComponent<NavMeshArea>();
			area.IsBlocker = false;
			area.Area = MonsterClip.Area;
			area.SceneVolume = new Sandbox.Volumes.SceneVolume { Box = new BBox( bspModel.Mins, bspModel.Maxs ) };
			marker.TargetName = entity.TargetName;
			RegisterTarget( entity, marker, null, null, null, nameof( MapEntity.Kill ) );
			clip.Enabled = true;
			return;
		}

		if ( className == "func_ladder" )
		{
			var ladder = new GameObject( true, EntityName( entity ) ) { IsStatic = true };
			ladder.WorldPosition = entity.Origin + (bspModel.Mins + bspModel.Maxs) * 0.5f;
			ladder.Tags.Add( "ladder" );
			var collider = ladder.AddComponent<BoxCollider>();
			collider.Scale = bspModel.Maxs - bspModel.Mins;
			collider.IsTrigger = true;
			collider.Static = true;
			return;
		}

		if ( className == "func_friction" )
		{
			WarnUnsupportedEntity( entity );
			return;
		}

		if ( className.StartsWith( "trigger", StringComparison.OrdinalIgnoreCase ) )
		{
			SpawnTriggerVolume( entity, bspModel );
			return;
		}

		if ( className == "func_traincontrols" )
		{
			_wiring.Add( () =>
			{
				var train = _mapState.Find( entity.Target ) as TrackTrain;
				if ( !train.IsValid() ) return;
				var offset = entity.Origin - train.PlacedOrigin;
				train.ControlBounds = new BBox( bspModel.Mins + offset, bspModel.Maxs + offset );
			} );
			return;
		}

		var rotating = className is "func_door_rotating" or "func_rot_button" or "func_rotating"
			or "func_pendulum" or "func_platrot" or "momentary_rot_button" or "func_trackchange" or "func_trackautochange";
		var center = rotating || className is "func_tracktrain" or "func_train" ? Vector3.Zero : (bspModel.Mins + bspModel.Maxs) * 0.5f;

		var (blend, tint) = ResolveRenderMode( entity );

		var surfaceOverride = '\0';
		if ( className is "func_breakable" or "func_pushable" )
		{
			var material = entity.GetInt( "material" );
			if ( material is >= 0 and <= 9 )
				surfaceOverride = GoldSrcSurfaces.BreakableType( material );
			else
				Log.Warning( $"{FilePath}: {EntityName( entity )} has unsupported breakable material {material}; using its texture surface." );
		}

		var isStatic = className is "func_wall" or "func_illusionary"
			or "func_conveyor" or "func_buyzone" or "func_detail";
		var aggregate = isStatic && string.IsNullOrEmpty( entity.TargetName );
		var geometry = GetBrushGeometry( bsp, bspModel, modelIndex, center, blend, surfaceOverride, aggregate );
		if ( geometry.IsEmpty )
			return;

		var go = new GameObject( true, EntityName( entity ) ) { IsStatic = isStatic };
		go.WorldPosition = entity.Origin + center;
		if ( className == "func_pushable" )
			go.Tags.Add( "goldsrc_pushable" );
		if ( rotating || className == "func_train" )
			go.WorldRotation = entity.Rotation;

		AddRenderers( go, geometry, tint );
		var appearance = go.AddComponent<BrushAppearance>();

		// Illusionary brushes and water are walked straight through.
		var passable = className is "func_door" or "func_water" or "func_door_rotating" or "func_train" or "func_tracktrain" or "func_pendulum"
			? (entity.SpawnFlags & 8) != 0
			: className switch
			{
				"func_rot_button" => (entity.SpawnFlags & 1) != 0,
				"momentary_rot_button" => (entity.SpawnFlags & 1) == 0,
				"func_rotating" => (entity.SpawnFlags & 64) != 0,
				"func_conveyor" => (entity.SpawnFlags & 2) != 0,
				_ => false
			};
		if ( !passable && !(className is "func_door" or "func_water" && entity.GetInt( "skin" ) != 0)
			&& className is not ("func_illusionary" or "func_buyzone") )
		{
			AddCollider( go, geometry.Physics, isStatic );
			if ( tint.a > 0f )
				_decalReceivers.Add( (bspModel, go, center) );
		}

		switch ( className )
		{
			case "func_door" or "func_water" or "func_door_rotating" or "func_button" or "func_rot_button":
				AddBrushMover( go, entity, bspModel, rotating );
				break;

			case "func_healthcharger" or "func_recharge":
				var charger = go.AddComponent<WallCharger>();
				charger.TargetName = entity.TargetName;
				charger.Armour = className == "func_recharge";
				float volume = charger.Armour ? 0.85f : 1f;
				charger.StartSound = BuildSoundEvent( charger.Armour ? "items/suitchargeok1.wav" : "items/medshot4.wav", volume );
				charger.LoopSound = BuildSoundEvent( charger.Armour ? "items/suitcharge1.wav" : "items/medcharge4.wav", volume );
				charger.DenySound = BuildSoundEvent( charger.Armour ? "items/suitchargeno1.wav" : "items/medshotno1.wav", volume );
				RegisterTarget( entity, charger );
				break;

			case "func_wall_toggle":
				AddToggleBrush( go, entity );
				break;

			case "func_wall" or "func_conveyor":
				var wall = go.AddComponent<WallBrush>();
				wall.TargetName = entity.TargetName;
				wall.Appearance = appearance;
				wall.Conveyor = className == "func_conveyor";
				wall.VisualOnly = (entity.SpawnFlags & 1) != 0;
				wall.Direction = entity.MoveDirection;
				wall.Speed = DefaultSpeed( entity, 100f );
				RegisterTarget( entity, wall );
				break;

			case "func_rotating":
				AddRotatingBrush( go, entity );
				break;

			case "func_pendulum":
				AddPendulum( go, entity );
				break;

			case "func_plat" or "func_platrot" or "func_trackchange" or "func_trackautochange":
				AddPlatform( go, entity, bspModel );
				break;

			case "momentary_door":
				var momentary = go.AddComponent<MomentaryDoor>();
				momentary.TargetName = entity.TargetName;
				momentary.StartOpen = (entity.SpawnFlags & 1) != 0;
				momentary.SlideOffset = MoveDistance( entity, bspModel, 0f );
				momentary.MoveSound = entity.GetInt( "movesnd" ) <= 8 ? GetIndexedSound( entity, "movesnd", DoorMoveSounds ) : null;
				RegisterTarget( entity, momentary );
				break;

			case "momentary_rot_button":
				AddMomentaryButton( go, entity );
				break;

			case "button_target":
				var target = go.AddComponent<ButtonTarget>();
				target.TargetName = entity.TargetName;
				target.Appearance = appearance;
				target.UseEnabled = (entity.SpawnFlags & 1) != 0;
				target.StartOn = (entity.SpawnFlags & 2) != 0;
				RegisterTarget( entity, target );
				_wiring.Add( () =>
				{
					target.OnTurnOn = BuildFire( entity.Target, UseType.On );
					target.OnTurnOff = BuildFire( entity.Target, UseType.Off );
				} );
				break;

			case "func_breakable" or "func_pushable":
				AddBreakable( go, entity, bspModel );
				break;

			case "func_tracktrain":
				AddTrain( go, entity, bspModel, tracked: true );
				break;

			case "func_train":
				AddTrain( go, entity, bspModel, tracked: false );
				break;

			default:
				var passive = go.AddComponent<Relay>();
				passive.TargetName = entity.TargetName;
				RegisterTarget( entity, passive, null, null, null, nameof( MapEntity.Kill ) );
				break;
		}
	}

	/// <summary>
	/// Translates a brush entity's rendermode into a blend mode plus the per-instance tint that
	/// carries rendercolor and renderamt. Only the translucent and
	/// additive modes read renderamt, everything else draws solid.
	/// </summary>
	private static (BlendMode Blend, Color Tint) ResolveRenderMode( Entity entity )
	{
		const int kRenderTransColor = 1;
		const int kRenderTransTexture = 2;
		const int kRenderGlow = 3;
		const int kRenderTransAdd = 5;

		var mode = entity.RenderMode;

		var blend = mode switch
		{
			kRenderTransColor or kRenderTransTexture => BlendMode.Alpha,
			kRenderGlow or kRenderTransAdd => BlendMode.Additive,
			_ => BlendMode.Opaque
		};

		if ( blend == BlendMode.Opaque )
			return (BlendMode.Opaque, Color.White);

		var alpha = entity.RenderAmt / 255f;

		// rendercolor replaces the texture entirely for kRenderTransColor; the blended modes
		// ignore it. A colour of 0 0 0 means "unset" rather than black.
		var color = Color.White;
		if ( mode == kRenderTransColor )
		{
			var rgb = entity.RenderColor / 255f;
			if ( rgb.Length > 0f )
				color = new Color( rgb.x, rgb.y, rgb.z );
		}

		return (blend, color.WithAlpha( alpha ));
	}

	private MapGeometry GetBrushGeometry( BspFile bsp, BspModel bspModel, int modelIndex, Vector3 center, BlendMode blend, char surfaceOverride, bool aggregate )
	{
		var pivoted = center == Vector3.Zero;
		var key = (modelIndex, blend, pivoted, surfaceOverride, aggregate);

		if ( _brushGeometry.TryGetValue( key, out var geometry ) )
			return geometry;

		var name = $"{BrushModelName( modelIndex )}_{(int)blend}_{(pivoted ? 1 : 0)}_{(int)surfaceOverride}{(aggregate ? "_static" : "")}";
		geometry = CreateGeometry( bsp, bspModel, name, center, blend, surfaceOverride, aggregate );
		_brushGeometry.Add( key, geometry );
		return geometry;
	}

	private void SpawnTriggerVolume( Entity entity, BspModel bspModel )
	{
		if ( SpawnSpecialTriggerVolume( entity, bspModel ) ) return;

		const int SF_TRIGGER_NOCLIENTS = 2;

		var className = entity.ClassName;
		if ( className is "trigger_cdaudio" or "trigger_autosave" )
		{
			var volume = new GameObject( true, EntityName( entity ) ) { IsStatic = true };
			volume.WorldPosition = entity.Origin + (bspModel.Mins + bspModel.Maxs) * 0.5f;
			AddCollider( volume, GetTriggerPhysics( entity.BrushModelIndex, bspModel.Maxs - bspModel.Mins ), true, trigger: true );
			if ( className == "trigger_autosave" )
			{
				var checkpoint = volume.AddComponent<AutoSave>();
				checkpoint.TargetName = entity.TargetName;
				RegisterTarget( entity, checkpoint, null, null, null, nameof( MapEntity.Kill ) );
				return;
			}
			var cue = volume.AddComponent<CdAudio>();
			cue.TargetName = entity.TargetName;
			cue.Track = entity.GetInt( "health" );
			var file = CdAudio.FileName( cue.Track );
			if ( file is not null && Host.FindMountPath( $"media/{file}.mp3" ) is {} path )
				cue.SoundPath = $"mount://{Host.Ident}/{path}.vsnd";
			RegisterTarget( entity, cue );
			return;
		}
		if ( SpawnLevelTrigger( entity, bspModel ) || className == "trigger_camera" )
			return;
		if ( className is not ("trigger_once" or "trigger_multiple" or "trigger_hurt") )
		{
			WarnUnsupportedEntity( entity );
			return;
		}

		var center = (bspModel.Mins + bspModel.Maxs) * 0.5f;

		var go = new GameObject( true, EntityName( entity ) ) { IsStatic = true };
		go.WorldPosition = entity.Origin + center;

		AddCollider( go, GetTriggerPhysics( entity.BrushModelIndex, bspModel.Maxs - bspModel.Mins ), true, trigger: true );

		var trigger = go.AddComponent<TriggerVolume>();
		trigger.TargetName = entity.TargetName;
		trigger.FireOnce = className is "trigger_once";
		trigger.Hurt = className == "trigger_hurt";
		trigger.NoClients = (entity.SpawnFlags & (trigger.Hurt ? 8 : SF_TRIGGER_NOCLIENTS)) != 0;
		trigger.AllowMonsters = (entity.SpawnFlags & 1) != 0;
		trigger.AllowPushables = (entity.SpawnFlags & 4) != 0;
		trigger.Wait = entity.GetFloat( "wait" );
		if ( trigger.Wait == 0f ) trigger.Wait = 0.2f;
		trigger.Damage = entity.GetFloat( "dmg" );
		trigger.DamageTypes = entity.GetInt( "damagetype" );
		trigger.StartOff = (entity.SpawnFlags & 2) != 0;
		trigger.ClientsOnly = (entity.SpawnFlags & 32) != 0;
		trigger.ClientsOnlyFire = (entity.SpawnFlags & 16) != 0;
		trigger.TargetOnce = (entity.SpawnFlags & 1) != 0;

		if ( trigger.Hurt ) RegisterTarget( entity, trigger );
		else RegisterTarget( entity, trigger, null, null, null, nameof( MapEntity.Kill ) );
		WireTrigger( entity, trigger );
	}

	private void AddBrushMover( GameObject go, Entity entity, BspModel bspModel, bool rotating )
	{
		var flags = entity.SpawnFlags;
		var button = entity.ClassName is "func_button" or "func_rot_button";
		var mover = go.AddComponent<BrushMover>();
		mover.TargetName = entity.TargetName;
		mover.IsButton = button;
		mover.Appearance = go.GetComponent<BrushAppearance>();
		mover.Shootable = button && entity.GetFloat( "health" ) > 0f;
		mover.Damage = entity.GetFloat( "dmg" );
		mover.Rotating = rotating;
		mover.StartAngles = rotating ? entity.Angles : default;
		mover.StartOpen = !button && (flags & 1) != 0;
		mover.Toggle = (flags & 32) != 0;
		mover.OneWay = (flags & 16) != 0;
		mover.UseEnabled = button ? (flags & 256) == 0 : (flags & 256) != 0;
		mover.TouchEnabled = button ? (flags & 256) != 0 : (flags & 256) == 0;
		mover.Speed = entity.GetFloat( "speed" );
		if ( mover.Speed == 0f ) mover.Speed = button ? 40f : 100f;
		mover.Wait = entity.GetFloat( "wait", button ? 1f : 0f );
		if ( button && mover.Wait == 0f ) mover.Wait = 1f;

		if ( rotating )
		{
			var distance = entity.GetFloat( "distance" ) * ((flags & 2) != 0 ? -1f : 1f);
			mover.AngleOffset = (flags & 64) != 0 ? new Angles( 0f, 0f, distance )
				: (flags & 128) != 0 ? new Angles( distance, 0f, 0f ) : new Angles( 0f, distance, 0f );
		}
		else
		{
			mover.SlideOffset = MoveDistance( entity, bspModel, button ? 4f : 0f );
			if ( button && ((flags & 1) != 0 || mover.SlideOffset.Length < 1f) )
				mover.SlideOffset = Vector3.Zero;
		}

		var silent = !button && (entity.ClassName is "func_door" or "func_water" && entity.GetInt( "skin" ) != 0 || flags < 0);
		mover.MoveSound = button ? GetIndexedSound( entity, "sounds", ButtonSounds )
			: silent ? null : GetIndexedSound( entity, "movesnd", DoorMoveSounds );
		mover.StopSound = button || silent ? null : GetIndexedSound( entity, "stopsnd", DoorStopSounds );
		mover.LockedSound = GetIndexedSound( entity, "locked_sound", ButtonSounds );
		mover.UnlockedSound = GetIndexedSound( entity, "unlocked_sound", ButtonSounds );
		if ( button && (flags & 64) != 0 )
		{
			mover.Sparks = go.AddComponent<SparkEffect>();
			mover.Sparks.Toggleable = true;
			mover.Sparks.StartOn = false;
			mover.Sparks.Sounds = Enumerable.Range( 1, 6 ).Select( i => BuildSoundEvent( $"buttons/spark{i}.wav" ) ).ToArray();
		}

		if ( button && !mover.UseEnabled )
			RegisterTarget( entity, mover, null, null, null, nameof( MapEntity.Kill ) );
		else
			RegisterTarget( entity, mover );
		WireTrigger( entity, mover );
		if ( !button )
			_wiring.Add( () => mover.OnClose = BuildFire( entity.GetString( "netname" ) ) );
	}

	/// <summary>
	/// How far a door or button travels: the size of the brush along its move axis, less the lip.
	/// </summary>
	private static Vector3 MoveDistance( Entity entity, BspModel bspModel, float lipDefault )
	{
		var direction = entity.MoveDirection;
		// BSP bounds omit the engine's one-unit expansion on each side.
		var size = bspModel.Maxs - bspModel.Mins;
		var lip = entity.GetFloat( "lip", lipDefault );
		if ( lip == 0f ) lip = lipDefault;
		var distance = MathF.Abs( direction.x * size.x ) + MathF.Abs( direction.y * size.y )
			+ MathF.Abs( direction.z * size.z ) - lip;
		return direction * distance;
	}

	private void AddToggleBrush( GameObject go, Entity entity )
	{
		var toggle = go.AddComponent<ToggleBrush>();
		toggle.TargetName = entity.TargetName;
		toggle.StartHidden = (entity.SpawnFlags & 1) != 0;
		RegisterTarget( entity, toggle );
	}
}
