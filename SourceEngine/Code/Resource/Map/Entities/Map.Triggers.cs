using System;

namespace Source1;

partial class MapLoader
{
	private sealed class PlayerTriggerLoader : EntityLoader
	{
		public override bool SupportsSkybox => false;

		public override void Load( EntityBuilder builder, MapEntity entity )
		{
			if ( entity.Integer( "spawnflags" ) != 1 || !string.IsNullOrEmpty( entity.Get( "filtername" ) ) ) return;
			var trigger = new GameObject( builder.World, false, entity.Get( "targetname", entity.Get( "classname" ) ) );
			trigger.LocalTransform = new( entity.Vector( "origin" ), Rotation.From( entity.Angles ) );
			builder.TrackParent( entity, trigger );
			builder.RegisterEntityIO( entity, trigger.AddComponent<PlayerTrigger>() );
		}
	}

	private sealed class HurtTriggerLoader : EntityLoader
	{
		public override bool SupportsSkybox => false;
		public override void Load( EntityBuilder builder, MapEntity entity ) => builder.BuildHurtTrigger( entity );
	}

	private sealed partial class EntityBuilder
	{
		public void BuildHurtTrigger( MapEntity entity )
		{
			var model = entity.Get( "model" );
			if ( model?.StartsWith( '*' ) != true || !int.TryParse( model.AsSpan( 1 ), out var index ) || index <= 0 || index >= bsp.Models.Length ) return;

			var trigger = new GameObject( World, false, entity.Get( "targetname", entity.Get( "classname" ) ) );
			trigger.LocalTransform = new( entity.Vector( "origin" ), Rotation.From( entity.Angles ) );
			TrackParent( entity, trigger );
			var hurt = trigger.AddComponent<HurtTrigger>();
			hurt.Damage = entity.Number( "damage", 10 );
			hurt.Cycle = entity.Number( "damagecycle", 1 );
			hurt.SpawnFlags = entity.Integer( "spawnflags" );
			RegisterEntityIO( entity, hurt );

			owner.BuildWorldPhysics( bsp, trigger, modelIndex: index, staticCollider: string.IsNullOrEmpty( entity.Get( "parentname" ) ) );
			foreach ( var collider in trigger.GetComponentsInChildren<PhysicsCollider>( true ) ) collider.IsTrigger = true;
			trigger.Enabled = true;
			if ( entity.Integer( "StartDisabled" ) != 0 ) hurt.TurnOff();
		}

		void WireTrigger( MapEntity entity, PlayerTrigger trigger )
		{
			trigger.Enter = BuildOutput( entity, "OnStartTouch", true );
			trigger.Leave = BuildOutput( entity, "OnEndTouch", true );
			trigger.First = BuildOutput( entity, "OnStartTouchAll", true );
			trigger.Last = BuildOutput( entity, "OnEndTouchAll", true );
			trigger.Activate = BuildOutput( entity, "OnTrigger", true );
			trigger.Wait = entity.Get( "classname" ) == "trigger_once" ? -1 : entity.Number( "wait", 0.2f );
			if ( trigger.Enter is null && trigger.Leave is null && trigger.First is null && trigger.Last is null && trigger.Activate is null ) return;

			var model = entity.Get( "model" );
			if ( model?.StartsWith( '*' ) != true || !int.TryParse( model.AsSpan( 1 ), out var index ) || index <= 0 || index >= bsp.Models.Length ) return;
			owner.BuildWorldPhysics( bsp, trigger.GameObject, modelIndex: index, staticCollider: string.IsNullOrEmpty( entity.Get( "parentname" ) ) );
			foreach ( var collider in trigger.GetComponentsInChildren<PhysicsCollider>( true ) ) collider.IsTrigger = true;
			trigger.GameObject.Enabled = true;
			if ( entity.Integer( "StartDisabled" ) != 0 ) trigger.TurnOff();
		}
	}
}
