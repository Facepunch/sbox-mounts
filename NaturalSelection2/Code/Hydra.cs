using Sandbox.Npcs;
using System;

namespace NS2;

[Title( "Hydra" ), Category( "Natural Selection 2" )]
public sealed class Hydra : AlienStructure
{
	protected override GrowthStats Stats => new( 8, 0.375f, 145, 5, 140, 175, 25 );
	const float Range = 17.78f * Scale;
	internal const string AttackSound = "alien/structures/hydra/attack";
	static readonly SparkAnimationGraph.PoseParameters BodyLayer = new( 0 ), FlinchLayer = new( 1 );
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose bodyPose { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose flinchPose { get; set; }
	TimeUntil scan = 0.5f, nextFire, nextAlert;
	bool attacking, alerting;
	Vector3 Barrel => WorldPosition + WorldRotation.Up * Scale;

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer ) => Configure<Hydra>( root, renderer, "Hydra" );

	protected override void OnUpdate()
	{
		if ( Health <= 0 || GameObject.IsDestroyed ) return;
		base.OnUpdate();
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		output?.Set( "grow", Growth );
		float intensity = FlinchIntensity;
		output?.Set( "intensity", intensity );
		if ( !Networking.IsHost )
		{
			if ( output is null ) return;
			bodyPose.Write( BodyLayer, output, age );
			flinchPose.Write( FlinchLayer, output, age );
			return;
		}
		bool burning = GameObject.Tags.Has( "burning" );
		if ( scan < 0 )
		{
			scan = 0.5f;
			attacking = false;
			if ( IsBuilt && NpcConVars.Enabled && !burning && !GameObject.Tags.Has( "electrified" ) ) ScanTargets();
		}
		if ( graph is null ) return;
		graph.Set( "alive", 1 );
		graph.Set( "built", IsBuilt ? 1 : 0 );
		graph.Set( "attacking", attacking ? 1 : 0 );
		graph.Set( "alerting", alerting ? 1 : 0 );
		graph.Set( "onfire", burning ? 1 : 0 );
		graph.Set( "flinch", intensity > 0 ? 1 : 0 );
		graph.Update( age, output );
		bodyPose = graph.GetPose( 0 );
		flinchPose = graph.GetPose( 1 );
	}

	void ScanTargets()
	{
		GameObject target = null;
		float closest = Range * Range;
		bool enemyNearby = false;
		foreach ( var obj in Scene.FindInPhysics( new Sphere( WorldPosition, Range + 2 * Scale ) ) )
		{
			if ( obj.GetComponentInParent<IDamageable>() is not Component { Active: true } victim ) continue;
			var enemy = victim.GameObject;
			if ( AlienTargeting.IsIgnoredPlayer( enemy ) || GetDisposition( enemy ) != Disposition.Hostile ) continue;
			if ( victim is IDamageable { MaxHealth: > 0, Health: <= 0 } ) continue;
			var aim = CalculateAimPoint( enemy );
			float distance = WorldPosition.DistanceSquared( aim );
			if ( distance < Range * Range && (enemy.Tags.HasAny( "ns2_marine", "ns2_exo", "ns2_lifeform" ) || enemy.GetComponent<PlayerController>().IsValid()) )
				enemyNearby = true;
			if ( distance >= closest || Scene.Trace.Ray( Barrel, aim ).IgnoreGameObjectHierarchy( GameObject )
				.IgnoreGameObjectHierarchy( enemy ).WithoutTags( "ns2_hydra", "trigger", "weapon" ).Run().Hit ) continue;
			target = enemy;
			closest = distance;
		}
		if ( target.IsValid() )
		{
			attacking = !Scene.Trace.Ray( Barrel, WorldPosition ).WithTag( "world" ).Run().Hit;
			if ( attacking && nextFire <= 0 ) Shoot( target );
		}
		else if ( nextAlert <= 0 )
		{
			alerting = enemyNearby;
			if ( alerting ) nextAlert = 2;
		}
	}

	internal static Vector3 SpikeDirection( Vector3 offset, Vector3 velocity, float angleRoll, float radiusRoll )
	{
		float distance = offset.Length / Scale;
		// Spark subtracts the target's travel during the notional flight time before the hitscan.
		var aim = Rotation.LookAt( offset - velocity * (distance / 50) );
		float spread = 1 + Math.Clamp( (distance - 2) / 8, 0, 1 );
		float radius = radiusRoll * MathF.Tan( spread.DegreeToRadian() );
		float angle = angleRoll * MathF.Tau;
		return (aim.Forward + (aim.Left * MathF.Cos( angle ) + aim.Up * MathF.Sin( angle )) * radius).Normal;
	}

	void Shoot( GameObject target )
	{
		nextFire = 0.8f;
		var origin = Barrel;
		var direction = SpikeDirection( CalculateAimPoint( target ) - origin, GetVelocity( target ), Random.Shared.NextSingle(), Random.Shared.NextSingle() );
		var hit = Scene.Trace.Ray( origin, origin + direction * Range ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "ns2_hydra", "trigger" ).UseHitboxes().Run();
		if ( hit.GameObject?.GetComponentInParent<IDamageable>() is Component victim && GetDisposition( victim.GameObject ) == Disposition.Hostile )
			((IDamageable)victim).OnDamage( new DamageInfo { Damage = 15, Origin = origin, Position = hit.HitPosition, Attacker = GameObject, Weapon = GameObject } );
		if ( hit.Hit ) Lerk.ShowSpike( origin, hit.EndPosition );
		AttackEffect( origin );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void AttackEffect( Vector3 origin )
	{
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{AttackSound}.sound" ) is { } sound ) Sound.Play( sound, origin );
	}

}
