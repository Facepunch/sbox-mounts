using System;
using DoorMode = Sandbox.Mapping.Door.DoorMode;

namespace Source1;

partial class MapLoader
{
	private static EntityLoader FindEntityLoader( string className, SourceGameMount mount = null ) =>
		className.ToLowerInvariant() switch
		{
			"worldspawn" => new WorldEntityLoader(),
			"info_player_start" or "info_player_deathmatch" or "info_player_counterterrorist" or "info_player_terrorist" or "info_player_teamspawn" => new SpawnEntityLoader(),
			"prop_static" or "prop_ragdoll_attached" or "prop_detail" or "prop_vehicle"
				or "prop_vehicle_driveable" or "prop_vehicle_jeep" or "prop_vehicle_airboat" or "prop_vehicle_prisoner_pod" => new PropEntityLoader(),
			"prop_door_rotating" or "prop_door_rotating_checkpoint" or "dz_door" => new PropDoorLoader(),
			"prop_dynamic" or "prop_dynamic_override" => new DynamicPropEntityLoader(),
			"prop_physics" or "prop_physics_override" or "prop_physics_multiplayer" or "prop_physics_respawnable" => new PhysicsPropEntityLoader(),
			"prop_ragdoll" => new RagdollEntityLoader(),
			"func_brush" or "func_monitor" => new ToggleBrushEntityLoader(),
			"func_wall" => new WallEntityLoader(),
			"func_wall_toggle" => new ToggleWallEntityLoader(),
			"func_illusionary" => new IllusionaryEntityLoader(),
			"func_lod" => new LodBrushEntityLoader(),
			"func_useableladder" => new EndpointLadderLoader(),
			"func_conveyor" => new ConveyorEntityLoader(),
			"func_lookdoor" => new LookDoorLoader(),
			"momentary_door" or "func_movelinear" => new LinearMoverLoader(),
			"func_rot_button" or "func_rotating" or "func_train" or "func_tracktrain" => new UnsimulatedBrushEntityLoader(),
			"func_breakable" or "func_breakable_surf" => new BreakableEntityLoader(),
			"func_door" => new DoorEntityLoader( DoorMode.Sliding ),
			"func_door_rotating" => new DoorEntityLoader( DoorMode.Rotating ),
			"func_button" => new ButtonEntityLoader(),
			"dod_bomb_target" => new BombTargetLoader(),
			"info_target" => new NamedTargetLoader(),
			"npc_antlion_template_maker" => new AntlionMakerLoader(),
			"info_node_hint" => new NodeHintLoader(),
			"trigger_multiple" or "trigger_once" => new PlayerTriggerLoader(),
			"trigger_hurt" => new HurtTriggerLoader(),
			"logic_relay" => new RelayLoader(),
			"point_bugbait" => new BugbaitSensorLoader(),
			"infodecal" => new DecalEntityLoader(),
			"info_projecteddecal" => new ProjectedDecalEntityLoader(),
			"env_soundscape" => new SoundscapeEntityLoader(),
			var spawn when mount?.PlayerStarts?.Contains( spawn ) == true => new SpawnEntityLoader(),
			_ => null
		};

	private abstract class EntityLoader
	{
		public virtual bool IsWorld => false;
		public virtual bool SupportsSkybox => true;
		public virtual Angles ReadAngles( MapEntity entity ) => entity.Angles;
		public abstract void Load( EntityBuilder builder, MapEntity entity );
	}

	private sealed class WorldEntityLoader : EntityLoader
	{
		public override bool IsWorld => true;
		public override void Load( EntityBuilder builder, MapEntity entity ) => builder.BuildSky( entity.Get( "skyname" ) );
	}

	internal static Angles ReadEntityAngles( MapEntity entity ) =>
		FindEntityLoader( entity.Get( "classname", "" ) )?.ReadAngles( entity ) ?? entity.Angles;

	private void BuildEntities( BspFile bsp, GameObject world, bool skybox = false )
	{
		var builder = new EntityBuilder( this, world, bsp, skybox );
		foreach ( var prop in bsp.StaticProps )
			if ( bsp.IsSkyboxProp( prop, skybox ) )
				builder.BuildProp( prop );
		List<MapEntity> portalWindows = [];
		foreach ( var entity in bsp.Entities )
		{
			var className = entity.Get( "classname" );
			if ( !skybox && className == "func_areaportalwindow" ) portalWindows.Add( entity );
			var loader = className is null ? null : FindEntityLoader( className, Host );
			if ( loader is not null )
			{
				if ( skybox && !loader.SupportsSkybox )
					continue;
				if ( !loader.IsWorld && (skybox ? !bsp.IsActiveSkyboxPosition( entity.Vector( "origin" ) ) : bsp.IsSkyboxPosition( entity.Vector( "origin" ) )) )
					continue;
				loader.Load( builder, entity );
			}
			else if ( !skybox && Host.CreateMapPoint( entity, world, bsp, ContentRoot ) is { } point )
			{
				builder.RegisterEntityIO( entity, point );
			}
			else if ( !skybox && Host.TriggerTag( className ) is { } tag )
			{
				var path = entity.Get( "model" );
				if ( path?.StartsWith( '*' ) != true || !int.TryParse( path.AsSpan( 1 ), out var index ) || index <= 0 || index >= bsp.Models.Length ) continue;
				var trigger = new GameObject( world, false, entity.Get( "targetname", className ) );
				trigger.LocalTransform = new Transform( entity.Vector( "origin" ), Rotation.From( entity.Angles ) );
				BuildWorldPhysics( bsp, trigger, modelIndex: index );
				foreach ( var collider in trigger.GetComponentsInChildren<PhysicsCollider>( true ) )
				{
					collider.IsTrigger = true;
					collider.GameObject.Tags.Add( tag );
				}
				trigger.Enabled = true;
			}
		}

		foreach ( var entity in portalWindows )
		{
			ModelRenderer FindBrush( string key ) => entity.Get( key ) is { Length: > 0 } name
				? world.Scene.Directory.FindByName( name ).FirstOrDefault( o => o.Tags.Has( "source1_entity" ) )?.GetComponent<ModelRenderer>( true )
				: null;
			if ( FindBrush( "target" ) is not { } target ) continue;
			var window = target.GameObject.AddComponent<AreaPortalWindow>();
			window.Renderer = target;
			window.Background = FindBrush( "BackgroundBModel" );
			window.FadeStart = entity.Number( "FadeStartDist" );
			window.FadeEnd = entity.Number( "FadeDist" );
			window.MinimumOpacity = entity.Number( "TranslucencyLimit" );
		}
		builder.Finish();
	}

	private sealed class BombTargetLoader : EntityLoader
	{
		public override bool SupportsSkybox => false;
		public override void Load( EntityBuilder builder, MapEntity entity )
		{
			var prefab = GameObject.GetPrefab( "mount://dods/entities/items/bomb_target.prefab" );
			if ( !prefab.IsValid() ) return;
			var point = prefab.Clone( new CloneConfig { Parent = builder.World, StartEnabled = false } );
			point.Name = entity.Get( "targetname", "dod_bomb_target" );
			point.LocalTransform = new Transform( entity.Vector( "origin" ), Rotation.From( entity.Angles ) );
			var target = point.GetComponent<Dods.BombTarget>( true );
			target.State = entity.Integer( "StartDisabled" ) == 0 ? Dods.BombTarget.States.Ready : Dods.BombTarget.States.Inactive;
			builder.RegisterEntityIO( entity, target );
			point.Enabled = true;
		}
	}

	private sealed class NamedTargetLoader : EntityLoader
	{
		public override bool SupportsSkybox => false;
		public override void Load( EntityBuilder builder, MapEntity entity )
		{
			if ( entity.Get( "targetname" ) is not { Length: > 0 } name ) return;
			_ = new GameObject( true, name )
			{
				Parent = builder.World,
				LocalPosition = entity.Vector( "origin" ),
				LocalRotation = Rotation.From( entity.Angles )
			};
		}
	}

	private sealed class AntlionMakerLoader : EntityLoader
	{
		public override bool SupportsSkybox => false;
		public override void Load( EntityBuilder builder, MapEntity entity ) => builder.BuildAntlionMaker( entity );
	}

	private sealed partial class EntityBuilder
	{
		public void BuildAntlionMaker( MapEntity entity )
		{
			var group = entity.Get( "spawngroup" );
			var template = entity.Get( "TemplateName" );
			if ( string.IsNullOrEmpty( group ) || string.IsNullOrEmpty( template ) ) return;
			var child = bsp.Entities.FirstOrDefault( e => e.Get( "classname" ) == "npc_antlion"
				&& string.Equals( e.Get( "targetname" ), template, StringComparison.OrdinalIgnoreCase ) );
			if ( child is null ) return;

			var point = new GameObject( true, entity.Get( "targetname", "npc_antlion_template_maker" ) )
			{
				Parent = World,
				LocalPosition = entity.Vector( "origin" ),
				LocalRotation = Rotation.From( entity.Angles )
			};
			var maker = point.AddComponent<Hl2.AntlionMaker>();
			var flags = entity.Integer( "spawnflags" );
			var ignore = entity.Get( "ignorebugbait" );
			maker.IgnoreBugbait = ignore == "1" || string.Equals( ignore, "yes", StringComparison.OrdinalIgnoreCase );
			maker.Group = group;
			maker.TemplateName = template;
			var prefab = (child.Integer( "spawnflags" ) & (1 << 18)) != 0 ? "antlion_worker" : "antlion";
			maker.ChildPrefab = $"mount://{owner.Host.Ident}/entities/npcs/{prefab}.prefab";
			maker.FollowTarget = entity.Get( "followtarget" );
			maker.FightTarget = entity.Get( "fighttarget" );
			maker.SpawnTarget = entity.Get( "spawntarget", "" );
			maker.Radius = entity.Number( "spawnradius" );
			maker.Interval = entity.Number( "SpawnFrequency" );
			maker.MaxAlive = entity.Integer( "MaxLiveChildren" );
			maker.Remaining = (flags & 32) != 0 ? -1 : entity.Integer( "MaxNPCCount" );
			maker.RandomBurrow = (flags & 1024) != 0;
			maker.Spawning = entity.Integer( "StartDisabled" ) == 0;
			RegisterEntityIO( entity, maker );
		}
	}

	private sealed class NodeHintLoader : EntityLoader
	{
		public override bool SupportsSkybox => false;

		public override void Load( EntityBuilder builder, MapEntity entity )
		{
			const int antlionBurrowPoint = 400;
			if ( entity.Integer( "hinttype" ) != antlionBurrowPoint ) return;

			var point = new GameObject( true, entity.Get( "targetname", "info_node_hint" ) )
			{
				Parent = builder.World,
				LocalPosition = entity.Vector( "origin" ),
				LocalRotation = Rotation.From( entity.Angles )
			};
			var burrow = point.AddComponent<Hl2.AntlionBurrow>();
			burrow.Group = entity.Get( "Group" );
			burrow.Available = entity.Integer( "StartHintDisabled" ) == 0;
			builder.RegisterEntityIO( entity, burrow );
		}
	}

	private sealed class RelayLoader : EntityLoader
	{
		public override bool SupportsSkybox => false;
		public override void Load( EntityBuilder builder, MapEntity entity )
		{
			var point = new GameObject( true, entity.Get( "targetname", "logic_relay" ) )
			{
				Parent = builder.World,
				LocalPosition = entity.Vector( "origin" )
			};
			var relay = point.AddComponent<MapRelay>();
			relay.Disabled = entity.Integer( "StartDisabled" ) != 0;
			var flags = entity.Integer( "spawnflags" );
			relay.Once = (flags & 1) != 0;
			relay.FastRetrigger = (flags & 2) != 0;
			builder.RegisterEntityIO( entity, relay );
		}
	}
	private sealed class BugbaitSensorLoader : EntityLoader
	{
		public override bool SupportsSkybox => false;

		public override void Load( EntityBuilder builder, MapEntity entity )
		{
			var point = new GameObject( true, entity.Get( "targetname", "point_bugbait" ) )
			{
				Parent = builder.World,
				LocalPosition = entity.Vector( "origin" )
			};
			var sensor = point.AddComponent<Hl2.BugbaitSensor>();
			var flags = entity.Integer( "spawnflags" );
			sensor.Radius = entity.Number( "radius" );
			sensor.Listening = entity.Integer( "Enabled" ) != 0;
			sensor.SuppressCall = (flags & 1) != 0;
			sensor.DetectThrows = (flags & 2) == 0;
			sensor.DetectSqueezes = (flags & 4) == 0;
			builder.RegisterEntityIO( entity, sensor );
		}
	}

	private sealed class SpawnEntityLoader : EntityLoader
	{
		public override void Load( EntityBuilder builder, MapEntity entity )
		{
			var instance = new GameObject( true, entity.Get( "classname" ) )
			{
				Parent = builder.World,
				LocalPosition = entity.Vector( "origin" ),
				LocalRotation = Rotation.From( ReadAngles( entity ) )
			};
			var spawn = instance.AddComponent<SpawnPoint>();
			if ( instance.Name == "info_player_start" && (entity.Integer( "spawnflags" ) & 1) != 0 )
				spawn.Priority = 1;
		}
	}
}
