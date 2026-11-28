using System;

namespace Source1;

partial class MapLoader
{
	private class PlayerTriggerLoader : EntityLoader
	{
		public override bool SupportsSkybox => false;

		public override void Load( EntityBuilder builder, MapEntity entity )
		{
			var className = entity.Get( "classname" );
			var gravity = className == "trigger_gravity";
			var trigger = new GameObject( builder.World, false, entity.Get( "targetname", entity.Get( "classname" ) ) );
			trigger.LocalTransform = new( entity.Vector( "origin" ), Rotation.From( entity.Angles ) );
			builder.TrackParent( entity, trigger );
			var touch = Create( trigger, entity );
			touch.ActorFlags = (PlayerTrigger.Actors)entity.Integer( "spawnflags" );
			if ( gravity ) trigger.AddComponent<GravityTrigger>().Gravity = entity.Number( "gravity" );
			builder.RegisterEntityIO( entity, touch );
		}

		protected virtual PlayerTrigger Create( GameObject trigger, MapEntity entity )
		{
			var className = entity.Get( "classname" );
			if ( className is "trigger_teleport" or "trigger_teleport_relative" )
			{
				var teleport = trigger.AddComponent<TeleportTrigger>();
				teleport.Destination = entity.Get( "target" );
				teleport.Landmark = entity.Get( "landmark" );
				teleport.PreserveAngles = (entity.Integer( "spawnflags" ) & 32) != 0;
				teleport.Relative = className == "trigger_teleport_relative";
				teleport.Offset = entity.Vector( "teleportoffset" );
				return teleport;
			}
			else if ( className == "trigger_soundscape" )
			{
				MakeNetworkRoot( trigger );
				return trigger.AddComponent<SoundscapeVolume>();
			}
			else if ( className == "trigger_push" )
			{
				MakeNetworkRoot( trigger );
				var push = trigger.AddComponent<PushTrigger>();
				var flags = entity.Integer( "spawnflags" );
				push.Once = (flags & 128) != 0;
				push.UseMass = (flags & 4096) != 0;
				push.FallingOnly = entity.Integer( "OnlyFallingPlayers" ) != 0;
				push.FallSpeedThreshold = entity.Number( "FallingSpeedThreshold" );
				push.Speed = entity.Number( "speed" );
				if ( push.Speed == 0 ) push.Speed = 100;
				push.SetPushDirection( entity.Vector( "pushdir" ) );
				return push;
			}
			else if ( className == "trigger_hurt" )
			{
				var hurt = trigger.AddComponent<HurtTrigger>();
				hurt.Damage = entity.Number( "damage" );
				hurt.Cap = entity.Number( "damagecap" );
				hurt.Doubles = entity.Integer( "damagemodel" ) == 1;
				long.TryParse( entity.Get( "damagetype" ), out var bits );
				hurt.DamageTypes = HurtTrigger.ReadTags( bits );
				return hurt;
			}
			return trigger.AddComponent<PlayerTrigger>();
		}
	}

	private sealed class SimpleTriggerLoader<T> : PlayerTriggerLoader where T : PlayerTrigger, new()
	{
		protected override PlayerTrigger Create( GameObject trigger, MapEntity entity ) => trigger.AddComponent<T>();
	}

	private sealed class MovementTriggerLoader : PlayerTriggerLoader
	{
		protected override PlayerTrigger Create( GameObject trigger, MapEntity entity )
		{
			MakeNetworkRoot( trigger );
			var movement = trigger.AddComponent<MovementTrigger>();
			var flags = entity.Integer( "spawnflags" );
			movement.Settings = (MovementTrigger.Restrictions)flags & (MovementTrigger.Restrictions.NoStep | MovementTrigger.Restrictions.Duck | MovementTrigger.Restrictions.Walk | MovementTrigger.Restrictions.NoJump);
			if ( (flags & 16) != 0 ) movement.Settings |= MovementTrigger.Restrictions.NoStep;
			return movement;
		}
	}

	private sealed class LookTriggerLoader : PlayerTriggerLoader
	{
		protected override PlayerTrigger Create( GameObject trigger, MapEntity entity )
		{
			var look = trigger.AddComponent<LookTrigger>();
			look.Target = entity.Get( "target" );
			look.FieldOfView = entity.Number( "FieldOfView" );
			look.LookTime = entity.Number( "LookTime" );
			look.Timeout = entity.Number( "timeout" );
			look.Once = (entity.Integer( "spawnflags" ) & 128) != 0;
			look.UseVelocity = (entity.Integer( "spawnflags" ) & 256) != 0;
			return look;
		}
	}

	private sealed partial class EntityBuilder
	{
		void WireTrigger( MapEntity entity, PlayerTrigger trigger )
		{
			trigger.Enter = BuildOutput( entity, "OnStartTouch", true );
			trigger.Leave = BuildOutput( entity, "OnEndTouch", true );
			trigger.First = BuildOutput( entity, "OnStartTouchAll", true );
			trigger.Last = BuildOutput( entity, "OnEndTouchAll", true );
			if ( trigger is RemoveTrigger remove ) remove.OnRemove = BuildOutput( entity, "OnRemove", true );
			if ( trigger is ChangeLevel change )
			{
				var name = entity.Get( "map" );
				if ( name is { Length: > 0 } && owner.Host.GetContentPath( $"maps/{name}.bsp" ) is { } path ) change.Destination = path + ".scene";
				var landmark = entity.Get( "landmark" );
				change.Landmark = World.Scene.Directory.FindByName( landmark ?? "" ).FirstOrDefault( o => o.Tags.Has( "source1_landmark" ) );
				change.ActorFlags = PlayerTrigger.Actors.Players;
				change.TouchEnabled = (entity.Integer( "spawnflags" ) & 2) == 0;
				change.OnChangeLevel = BuildOutput( entity, "OnChangeLevel", true );
				foreach ( var volume in bsp.Entities )
				{
					if ( volume.Get( "classname" ) != "trigger_transition" || !string.Equals( volume.Get( "targetname" ), landmark, StringComparison.OrdinalIgnoreCase ) ) continue;
					var pathToModel = volume.Get( "model" );
					if ( pathToModel?.StartsWith( '*' ) != true || !int.TryParse( pathToModel.AsSpan( 1 ), out var i ) || i <= 0 || i >= bsp.Models.Length ) continue;
					var bounds = new BBox( bsp.Models[i].Mins, bsp.Models[i].Maxs );
					change.Volumes.Add( bounds.Transform( new Transform( volume.Vector( "origin" ), Rotation.From( volume.Angles ) ) ) );
				}
			}
			if ( trigger is LookTrigger look ) look.OnTimeout = BuildOutput( entity, "OnTimeout", true );
			trigger.Activate = BuildOutput( entity, "OnTrigger", true );
			trigger.Touching = BuildOutput( entity, "OnTouching", true );
			trigger.NotTouching = BuildOutput( entity, "OnNotTouching", true );
			trigger.Wait = entity.Get( "classname" ) == "trigger_once" ? -1 : entity.Number( "wait", 0.2f );
			var gravity = trigger.GetComponent<GravityTrigger>();
			if ( trigger is HurtTrigger hurt )
			{
				hurt.OnHurt = BuildOutput( entity, "OnHurt", true );
				hurt.OnHurtPlayer = BuildOutput( entity, "OnHurtPlayer", true );
			}
			if ( trigger.GetType() == typeof( PlayerTrigger ) && gravity is null && trigger.Enter is null && trigger.Leave is null && trigger.First is null && trigger.Last is null && trigger.Activate is null && trigger.Touching is null && trigger.NotTouching is null ) return;

			var model = entity.Get( "model" );
			if ( model?.StartsWith( '*' ) != true || !int.TryParse( model.AsSpan( 1 ), out var index ) || index <= 0 || index >= bsp.Models.Length ) return;
			if ( trigger is SoundscapeVolume soundscape ) BuildSoundscapeVolume( entity, soundscape, bsp.Models[index] );
			if ( trigger is TeleportTrigger { Relative: true } teleport )
				teleport.LocalCenter = (bsp.Models[index].Mins + bsp.Models[index].Maxs) * 0.5f;
			if ( trigger is HurtTrigger hazard ) hazard.LocalCenter = (bsp.Models[index].Mins + bsp.Models[index].Maxs) * 0.5f;
			owner.BuildWorldPhysics( bsp, trigger.GameObject, modelIndex: index, staticCollider: string.IsNullOrEmpty( entity.Get( "parentname" ) ) );
			foreach ( var collider in trigger.GetComponentsInChildren<PhysicsCollider>( true ) )
			{
				collider.IsTrigger = true;
				gravity?.Volumes.Add( collider );
			}
			trigger.GameObject.Enabled = true;
			if ( entity.Integer( "StartDisabled" ) != 0 ) trigger.TurnOff();
		}
	}
}
