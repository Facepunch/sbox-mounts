using Sandbox.Npcs;
using System;

namespace NS2;

[Title( "Hydra" ), Category( "Natural Selection 2" )]
public sealed class Hydra : AlienStructure
{
	protected override GrowthStats Stats => new( 8, 0.375f, 145, 5, 140, 175, 25 );
	protected override int HealthPerBiomass => 18;
	const float Range = 17.78f * Scale;
	internal const string AttackSound = "alien/structures/hydra/attack";
	const string MetalImpact = "alien/lerk/spikes_structure", ArmorImpact = "alien/common/spike_hit_marine", FleshImpact = "alien/lerk/spikes_pierce";
	internal const string PainSound = "alien/structures/hydra/wound";
	static readonly SparkAnimationGraph.PoseParameters BodyLayer = new( 0 ), FlinchLayer = new( 1 );
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose bodyPose { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose flinchPose { get; set; }
	TimeUntil scan = 0.5f, nextFire, nextAlert;
	bool attacking, alerting;
	Vector3 Barrel => WorldPosition + WorldRotation.Up * Scale;

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer ) => Configure<Hydra>( root, renderer, "Hydra" );

	internal static void RegisterSounds( MountContext context )
	{
		foreach ( var cue in new[] { AttackSound, MetalImpact, ArmorImpact, FleshImpact } )
			context.Add( ResourceType.GameResource, $"sounds/{cue}.sound", new SparkSoundLoader( cue ) );
	}

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

	protected override Vector3 CalculateAimPoint( GameObject target ) => target.GetComponent<BaseNpc>() switch
	{
		Marine marine => marine.EngagementPoint,
		Mac mac => mac.EyePosition,
		Exo => target.WorldPosition + Vector3.Up * (1.5f * Scale),
		_ => base.CalculateAimPoint( target )
	};

	void ScanTargets()
	{
		GameObject target = null;
		float closest = float.MaxValue;
		bool targetCanAttack = false;
		bool enemyNearby = false;
		var alertOrigin = ModelOrigin;
		foreach ( var obj in Scene.FindInPhysics( new Sphere( alertOrigin, Range + 2 * Scale ) ) )
		{
			if ( obj.GetComponentInParent<IDamageable>() is not Component { Active: true } victim ) continue;
			var enemy = victim.GameObject;
			if ( AlienTargeting.IsIgnoredPlayer( enemy ) || GetDisposition( enemy ) != Disposition.Hostile ) continue;
			if ( victim is IDamageable { MaxHealth: > 0, Health: <= 0 } ) continue;
			var aim = CalculateAimPoint( enemy );
			float distance = alertOrigin.DistanceSquared( aim );
			if ( distance < Range * Range && (enemy.Tags.HasAny( "ns2_marine", "ns2_exo", "ns2_lifeform" ) || enemy.GetComponent<PlayerController>().IsValid()) )
				enemyNearby = true;
			// Spark checks mobile target origins and static engagement points from the eye.
			float range = Barrel.DistanceSquared( enemy.WorldPosition );
			if ( (enemy.Tags.Has( "structure" ) ? Barrel.DistanceSquared( aim ) : range) > Range * Range ) continue;
			bool canAttack = victim is BaseNpc and not (Mac or Drifter or Harvester) || enemy.GetComponent<PlayerController>().IsValid();
			if ( targetCanAttack && !canAttack || canAttack == targetCanAttack && range >= closest ) continue;
			if ( Scene.Trace.Ray( Barrel, aim ).IgnoreGameObjectHierarchy( GameObject )
				.IgnoreGameObjectHierarchy( enemy ).WithoutTags( "ns2_hydra", "trigger", "weapon" ).Run().Hit ) continue;
			target = enemy;
			closest = range;
			targetCanAttack = canAttack;
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
		GameObject targetHit = null;
		if ( hit.GameObject?.GetComponentInParent<IDamageable>() is Component victim && GetDisposition( victim.GameObject ) == Disposition.Hostile )
		{
			targetHit = victim.GameObject;
			var damage = new DamageInfo { Damage = 15, Origin = origin, Position = hit.HitPosition, Attacker = GameObject, Weapon = GameObject };
			damage.Tags.Add( AlienWounds.ExtraFlinch );
			((IDamageable)victim).OnDamage( damage );
		}
		string surface = null;
		if ( hit.Hit )
		{
			surface = hit.Surface?.ResourceName is { } name && name.StartsWith( "ns2_" ) ? name[4..] : "metal";
			if ( targetHit.IsValid() )
			{
				surface = targetHit.Tags.Has( "burning" ) ? "flame" :
					targetHit.Tags.Has( "ns2_exo" ) ? "robot" :
					targetHit.Tags.Has( "ns2_marine" ) || targetHit.GetComponent<PlayerController>().IsValid() ? "flesh" : "metal";
			}
		}
		AttackEffect( origin, hit.EndPosition, surface, targetHit );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void AttackEffect( Vector3 origin, Vector3 end, string surface, GameObject victim )
	{
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{AttackSound}.sound" ) is { } sound ) Sound.Play( sound, origin );
		if ( surface is null || victim?.GetComponent<PlayerController>() is { IsProxy: false } ) return;

		var cue = surface switch { "metal" => MetalImpact, "armor" => ArmorImpact, "flesh" => FleshImpact, _ => "surfaces/materials/rock/ricochet" };
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{cue}.sound" ) is { } impact ) Sound.Play( impact, end );
		CinematicLoader.Play( $"cinematics/materials/{surface}/sting.cinematic", end, Rotation.Identity );
		Lerk.ShowSpike( origin, end );
	}

	protected override GameObject CreateRagdoll() => graph is null ? NpcCorpse.Attach( base.CreateRagdoll(), 1.5f ) : NpcCorpse.Animate( Renderer, graph, age );
}
