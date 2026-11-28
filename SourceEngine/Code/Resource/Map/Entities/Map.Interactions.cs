using System;
using Door = Sandbox.Mapping.Door;
using Button = Sandbox.Mapping.Button;

namespace Source1;

partial class MapLoader
{
	// MapInstance spawns networked roots when it instantiates the mounted scene.
	private static void MakeNetworkRoot( GameObject instance )
	{
		instance.Parent = instance.Scene;
		instance.NetworkMode = NetworkMode.Object;
	}

	private sealed class PropDoorLoader : EntityLoader
	{
		public override bool SupportsSkybox => false;
		public override void Load( EntityBuilder builder, MapEntity entity ) => builder.BuildPropDoor( entity );
	}

	private sealed partial class EntityBuilder
	{
		private readonly List<(MapEntity Entity, PropDoor Door)> propDoors = [];

		readonly List<(GameObject Child, string Parent)> parented = [];

		public void TrackParent( MapEntity entity, GameObject child )
		{
			if ( !skybox && entity?.Get( "parentname" ) is { Length: > 0 } name )
				parented.Add( (child, name) );
		}

		private void ParentMovers()
		{
			foreach ( var (child, name) in parented )
			{
				var parent = World.Scene.Directory.FindByName( name ).FirstOrDefault( o => o.Tags.Has( "source1_entity" ) );
				if ( !parent.IsValid() || parent.IsAncestor( child ) ) continue;
				if ( child.GetComponent<MovingBrush>( true ) is null && parent.GetComponent<MovingBrush>( true ) is null ) continue;
				if ( parent.NetworkMode != NetworkMode.Object ) MakeNetworkRoot( parent );
				child.IsStatic = false;
				if ( child.GetComponent<PropDoor>( true ) is { } door )
				{
					child.SetParent( parent, keepWorldPosition: true );
					door.ClosedAngles = (parent.WorldRotation.Inverse * door.ClosedAngles.ToRotation()).Angles();
					door.InitialAngles = child.LocalRotation.Angles();
				}
				else DoorInputs.Reparent( child, parent );
			}
		}

		private void LinkPropDoors()
		{
			var named = propDoors.ToLookup( x => x.Entity.Get( "targetname" ), x => x.Door, StringComparer.OrdinalIgnoreCase );
			foreach ( var (entity, door) in propDoors )
			{
				var name = entity.Get( "targetname" );
				if ( string.IsNullOrEmpty( name ) || door.Master is not null ) continue;
				var slave = entity.Get( "slavename" );
				foreach ( var leaf in named[string.IsNullOrEmpty( slave ) ? name : slave] )
				{
					if ( leaf == door || leaf.Master is not null || leaf.LinkedDoors.Count > 0 ) continue;
					leaf.Master = door;
					door.LinkedDoors.Add( leaf );
				}
			}
		}

		static int ForwardSwingSign( SkinnedModelRenderer renderer )
		{
			var rotation = renderer.WorldRotation;
			var bounds = renderer.Model.PhysicsBounds.Transform( new Transform( Vector3.Zero, rotation ) );
			var min = bounds.Mins.WithZ( 0 );
			var max = bounds.Maxs.WithZ( 0 );
			var farthest = min.LengthSquared > max.LengthSquared ? min : max;
			return Vector3.Dot( farthest, rotation.Right ) > 0 ? 1 : -1;
		}

		public SoundEvent MapSound( string name ) => Sounds.Sound( name, owner.Host.Ident );

		public void BuildPropDoor( MapEntity entity )
		{
			var prop = ReadProp( entity ) with { Physics = PropPhysics.Kinematic };
			var instance = BuildProp( prop, entity, animated: true );
			if ( !instance.IsValid() ) return;

			var flags = entity.Integer( "spawnflags" );
			var direction = entity.Integer( "opendir" );
			var spawn = entity.Integer( "spawnpos" );
			MakeNetworkRoot( instance );
			var renderer = instance.GetComponent<SkinnedModelRenderer>( true );
			var door = instance.AddComponent<PropDoor>();
			door.Renderer = renderer;
			door.Collider = instance.GetComponent<ModelCollider>( true );
			door.ClosedAngles = prop.Angles;
			door.InitialAngles = door.ClosedAngles;
			door.OpensBackward = direction == 2;
			var distance = Math.Abs( entity.Number( "distance" ) );
			if ( distance == 0 ) distance = 90;
			door.OpenBothWays = direction == 0;
			door.Speed = entity.Number( "speed" );
			if ( door.Speed <= 0 ) door.Speed = 100;
			door.IsLocked = (flags & 2048) != 0;
			door.IsUsable = (flags & 32768) == 0;
			door.UseCloses = (flags & 8192) != 0;
			door.ForceClosed = entity.Integer( "forceclosed" ) != 0;
			door.ReturnDelay = entity.Number( "returndelay", -1 );
			if ( (flags & 1) != 0 || spawn is 1 or 2 )
			{
				door.State = PropDoor.DoorState.Open;
				var angle = (flags & 1) == 0 && spawn == 2 ? distance : -distance;
				door.InitialAngles = door.ClosedAngles.WithYaw( door.ClosedAngles.yaw + angle );
			}
			else if ( spawn == 3 )
			{
				door.State = PropDoor.DoorState.Ajar;
				var ajar = entity.Vector( "ajarangles" );
				door.InitialAngles = new Angles( ajar.x, ajar.y, ajar.z );
			}
			instance.LocalRotation = door.InitialAngles.ToRotation();
			door.TargetAngle = distance * (direction == 2 ? -1 : 1) * ForwardSwingSign( renderer );
			var hardware = entity.Integer( "hardware" );
			if ( renderer.Model.Parts.Count > 1 ) renderer.SetBodyGroup( 1, hardware );
			var options = renderer.Model.GetData<DoorOptions>();
			var skin = $"skin{entity.Integer( "skin" )}";
			var modelHealth = renderer.Model.Data.Health;
			var health = modelHealth >= 0 ? (int)modelHealth : entity.Integer( "health" );
			if ( health > 0 )
			{
				var damage = instance.AddComponent<DoorDamage>();
				damage.Door = door;
				damage.MaxHealth = damage.Health = health;
				damage.Breakable = (flags & 524288) != 0;
				var group = options?.Groups.GetValueOrDefault( skin ) ?? options?.Groups.GetValueOrDefault( "defaults" );
				for ( var i = 1; group?.GetValueOrDefault( $"damage{i}" ) is { Length: > 0 } name; i++ )
				{
					var model = LoadModel( $"models/{name.Replace( '\\', '/' )}.mdl" );
					if ( model is null ) break;
					damage.Stages.Add( model );
				}
			}
			if ( options?.Get( skin, "surfaceprop" ) is { Length: > 0 } surface && door.Collider is { } collider )
				collider.Surface = owner.Host.GetSurface( surface, renderer.Model.ResourcePath );
			if ( (flags & 4096) == 0 )
			{
				SoundEvent Sound( string group, string role ) => Sounds.Sound(
					entity.Get( $"sound{role}override" ) ?? options?.Get( group, role ), owner.Host.Ident );
				door.MoveSound = Sound( skin, "move" );
				door.OpenFinishedSound = Sound( skin, "open" );
				door.CloseFinishedSound = Sound( skin, "close" );
				door.LockedSound = Sound( $"hardware{hardware}", "locked" );
				door.UnlockedSound = Sound( $"hardware{hardware}", "unlocked" );
			}
			RegisterEntityIO( entity, door );
			propDoors.Add( (entity, door) );
		}
	}

	private class LinearMoverLoader : BrushEntityLoader
	{
		protected virtual LinearMover Create( GameObject instance, MapEntity entity, BspModel model ) => instance.AddComponent<LinearMover>();

		public override bool IsSolid( MapEntity entity ) => (entity.Integer( "spawnflags" ) & 8) == 0;

		public override bool AddInteraction( EntityBuilder builder, MapEntity entity, GameObject instance, BspModel model )
		{
			var mover = Create( instance, entity, model );
			mover.BlockDamage = entity.Number( "blockdamage" );
			mover.Travel = BrushMove( entity, model, 0, entity.Number( "movedistance" ) );
			mover.Closed = instance.LocalPosition - mover.Travel * entity.Number( "startposition" );
			mover.Speed = entity.Number( "speed" );
			if ( mover.Speed <= 0 ) mover.Speed = 100;
			mover.StartSound = builder.MapSound( entity.Get( "startsound" ) );
			mover.StopSound = builder.MapSound( entity.Get( "stopsound" ) );
			MakeNetworkRoot( instance );
			builder.RegisterEntityIO( entity, mover );
			return true;
		}
	}

	private sealed class LookDoorLoader : LinearMoverLoader
	{
		protected override LinearMover Create( GameObject instance, MapEntity entity, BspModel model )
		{
			var door = instance.AddComponent<Hl2.LookDoor>();
			door.Looker = entity.Get( "target" );
			door.CenterOffset = instance.LocalRotation * ((model.Mins + model.Maxs) * 0.5f);
			door.Proximity = entity.Number( "ProximityDistance" );
			door.Offset = entity.Number( "ProximityOffset" );
			door.FieldOfView = entity.Number( "FieldOfView" );
			var flags = entity.Integer( "spawnflags" );
			door.Threshold = (flags & 8192) != 0;
			door.Inverted = (flags & 16384) != 0;
			door.FromOpen = (flags & 32768) != 0;
			return door;
		}
	}

	private sealed class DoorEntityLoader( Door.DoorMode mode ) : BrushEntityLoader
	{
		public override bool IsSolid( MapEntity entity ) => (entity.Integer( "spawnflags" ) & 8) == 0;

		public override bool AddInteraction( EntityBuilder builder, MapEntity entity, GameObject instance, BspModel model )
		{
			var flags = entity.Integer( "spawnflags" );
			MakeNetworkRoot( instance );
			var door = instance.AddComponent<BrushDoor>( false );
			var defaults = mode == Door.DoorMode.Rotating ? "RotDoorSound" : "DoorSound";
			SoundEvent ReadSound( string key, string fallback )
			{
				var name = entity.Get( key );
				return builder.MapSound( string.IsNullOrEmpty( name ) || name == "0" ? fallback : name );
			}
			door.LockedSound = ReadSound( "locked_sound", $"{defaults}.DefaultLocked" );
			if ( (flags & 4096) == 0 )
			{
				var opening = ReadSound( "noise1", $"{defaults}.DefaultMove" );
				var closing = entity.Get( "startclosesound" ) is { Length: > 0 } startClose ? builder.MapSound( startClose ) : opening;
				door.OpenSound = opening;
				door.CloseSound = closing;
				door.OpenFinishedSound = ReadSound( "noise2", $"{defaults}.DefaultArrive" );
				door.CloseFinishedSound = entity.Get( "closesound" ) is { Length: > 0 } close ? builder.MapSound( close ) : door.OpenFinishedSound;
			}
			door.Speed = entity.Number( "speed", 100 );
			if ( door.Speed == 0 ) door.Speed = 100;
			door.ForceClosed = entity.Integer( "forceclosed" ) != 0;
			door.BlockDamage = entity.Number( "dmg" );
			door.ChainTarget = entity.Get( "chainstodoor" );
			door.MovementGroup = entity.Get( "targetname" );
			door.LegacyStartOpen = (flags & 1) != 0;
			door.StartOpen = door.LegacyStartOpen ? mode == Door.DoorMode.Sliding : entity.Integer( "spawnpos" ) == 1;
			door.IsLocked = entity.Integer( "locked" ) != 0 || (flags & 2048) != 0;
			door.IsUsable = (flags & 256) != 0 && (flags & 32768) == 0;
			door.TouchOpens = (flags & 1024) != 0;
			door.ToggleUse = (flags & 32) != 0;
			door.Wait = entity.Number( "wait", 4 );
			if ( mode == Door.DoorMode.Rotating )
			{
				door.Rotates = true;
				var distance = entity.Number( "distance", 90 ) * ((flags & 2) != 0 ? -1 : 1);
				var rotation = (flags & 64) != 0 ? new Angles( 0, 0, distance )
					: (flags & 128) != 0 ? new Angles( distance, 0, 0 ) : new Angles( 0, distance, 0 );
				door.ClosedAngles = door.LegacyStartOpen ? entity.Angles + rotation : entity.Angles;
				door.OpenAngles = door.LegacyStartOpen ? entity.Angles : entity.Angles + rotation;
				door.OpenBothWays = (flags & (16 | 64 | 128)) == 0;
				door.BrushBounds = new BBox( model.Mins, model.Maxs );
			}
			else door.Travel = BrushMove( entity, model, 0 );
			builder.RegisterEntityIO( entity, door );
			door.Enabled = true;
			return true;
		}
	}

	private sealed class ButtonEntityLoader : BrushEntityLoader
	{
		public override bool AddInteraction( EntityBuilder builder, MapEntity entity, GameObject instance, BspModel model )
		{
			var flags = entity.Integer( "spawnflags" );
			var button = instance.AddComponent<Button>( false );
			button.Mode = Button.ButtonMode.Toggle;
			button.MoveDelta = BrushMove( entity, model, 4 );
			button.Move = (flags & 1) == 0 && button.MoveDelta.Length >= 1;
			var speed = entity.Number( "speed", 40 );
			if ( speed == 0 ) speed = 40;
			// even a button that doesn't move needs some animation time, or it never finishes animating and ignores later presses
			button.AnimationTime = Math.Max( button.Move ? button.MoveDelta.Length / Math.Max( 1, speed ) : 0, 0.05f );
			var wait = entity.Number( "wait", 1 );
			if ( wait == 0 ) wait = 1;
			button.AutoReset = wait >= 0 && (flags & 32) == 0;
			button.ResetTime = Math.Max( 0, wait );
			builder.RegisterEntityIO( entity, button );
			button.Enabled = true;
			return true;
		}
	}

	internal static Vector3 BrushMove( MapEntity entity, BspModel model, float defaultLip, float distance = 0 )
	{
		var angles = entity.Vector( "movedir" );
		var direction = Rotation.From( new Angles( angles.x, angles.y, angles.z ) ).Forward;
		if ( distance > 0 ) return direction * distance;
		var lip = entity.Number( "lip", defaultLip );
		if ( lip == 0 ) lip = defaultLip;
		distance = Vector3.Dot( direction.Abs(), model.Maxs - model.Mins ) - lip;
		return direction * distance;
	}
}
