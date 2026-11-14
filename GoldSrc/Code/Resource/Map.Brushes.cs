using System;
using GoldSrc.Bsp;

namespace GoldSrc;

partial class MapLoader
{
	private readonly Dictionary<(int Model, BlendMode Blend, bool PivotAtOrigin, char Surface, bool Aggregate), MapGeometry> _brushGeometry = [];

	private void SpawnBrushEntity( BspFile bsp, Entity entity )
	{
		var modelIndex = entity.BrushModelIndex;
		if ( modelIndex >= bsp.Models.Length )
			return;

		var bspModel = bsp.Models[modelIndex];
		var className = entity.ClassName;

		if ( className.StartsWith( "trigger", StringComparison.OrdinalIgnoreCase ) )
		{
			SpawnTriggerVolume( entity, bspModel );
			return;
		}

		var rotating = className is "func_door_rotating" or "func_rot_button";
		var center = rotating || className == "func_tracktrain" ? Vector3.Zero : (bspModel.Mins + bspModel.Maxs) * 0.5f;

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
			or "func_conveyor" or "func_ladder" or "func_buyzone" or "func_water" or "func_detail";
		var geometry = GetBrushGeometry( bsp, bspModel, modelIndex, center, blend, surfaceOverride, isStatic );
		if ( geometry.IsEmpty )
			return;

		var go = new GameObject( true, EntityName( entity ) ) { IsStatic = isStatic };
		go.WorldPosition = entity.Origin + center;
		if ( className == "func_pushable" )
			go.Tags.Add( "goldsrc_pushable" );
		if ( rotating )
			go.WorldRotation = entity.Rotation;

		// GoldSrc's R_DrawBrushModel bails out entirely once the blend reaches zero.
		if ( tint.a > 0f )
			AddRenderers( go, geometry, tint );

		// Illusionary brushes and water are walked straight through.
		var passable = className is "func_door" or "func_door_rotating" or "func_train" or "func_tracktrain"
			? (entity.SpawnFlags & 8) != 0
			: className == "func_rot_button" && (entity.SpawnFlags & 1) != 0;
		if ( !passable && !(className == "func_door" && entity.GetInt( "skin" ) != 0)
			&& className is not ("func_illusionary" or "func_water" or "func_ladder" or "func_buyzone") )
		{
			AddCollider( go, geometry.Physics, isStatic );
			if ( tint.a > 0f )
				_decalReceivers.Add( (bspModel, go, center) );
		}

		switch ( className )
		{
			case "func_door" or "func_door_rotating" or "func_button" or "func_rot_button":
				AddBrushMover( go, entity, bspModel, rotating );
				break;

			case "func_wall_toggle":
				AddToggleBrush( go, entity );
				break;

			case "func_tracktrain":
				AddTrain( go, entity, tracked: true );
				break;

			case "func_train":
				AddTrain( go, entity, tracked: false );
				break;

			default:
				if ( !string.IsNullOrEmpty( entity.TargetName ) )
				{
					var passive = go.AddComponent<Relay>();
					passive.TargetName = entity.TargetName;
					RegisterTarget( entity, passive, null, null, null, nameof( MapEntity.Kill ) );
				}
				break;
		}
	}

	/// <summary>
	/// Translates a brush entity's rendermode into a blend mode plus the per-instance tint that
	/// carries rendercolor and renderamt. Mirrors R_DrawBrushModel: only the translucent and
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
		const int SF_TRIGGER_NOCLIENTS = 2;

		var className = entity.ClassName;
		if ( className is "trigger_changelevel" or "trigger_transition" or "trigger_camera" )
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
		trigger.StartOff = (entity.SpawnFlags & 2) != 0;
		trigger.ClientsOnly = (entity.SpawnFlags & 32) != 0;
		trigger.ClientsOnlyFire = (entity.SpawnFlags & 16) != 0;
		trigger.TargetOnce = (entity.SpawnFlags & 1) != 0;
		trigger.FacingDirection = entity.Angles.AsVector3().IsNearlyZero() ? Vector3.Zero : entity.MoveDirection;

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
		mover.Rotating = rotating;
		mover.StartAngles = rotating ? entity.Angles : default;
		mover.StartOpen = !button && (flags & 1) != 0;
		mover.Toggle = (flags & 32) != 0;
		mover.OneWay = (flags & 16) != 0;
		mover.UseEnabled = button ? (flags & 256) == 0 : (flags & 256) != 0;
		mover.TouchEnabled = button ? (flags & 256) != 0 : (flags & 256) == 0 && string.IsNullOrEmpty( entity.TargetName );
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

		var silent = !button && (entity.ClassName == "func_door" && entity.GetInt( "skin" ) != 0 || flags < 0);
		mover.MoveSound = button ? GetIndexedSound( entity, "sounds", ButtonSounds )
			: silent ? null : GetIndexedSound( entity, "movesnd", DoorMoveSounds );
		mover.StopSound = button || silent ? null : GetIndexedSound( entity, "stopsnd", DoorStopSounds );

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
