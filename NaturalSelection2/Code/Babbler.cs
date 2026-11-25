using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NS2;

[Title( "Babbler" ), Category( "Natural Selection 2" )]
public sealed partial class Babbler : BaseNpc
{
	const float Scale = MapLoader.Scale;
	const float Radius = 0.25f * Scale;
	const float Gravity = 9.7f;
	Rigidbody body;
	GameObject target;
	TimeUntil nextAttack, nextHit, nextNudge;
	TimeSince lastHit = 1;
	int nudges;
	Vector3 previousPosition, previousVelocity;
	Vector3 SphereCenter => WorldPosition + Vector3.Up * Radius;

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer )
	{
		renderer.LocalPosition = Vector3.Up * Radius;
		var shape = new GameObject( root, true, "Collider" ).AddComponent<SphereCollider>();
		shape.GameObject.Tags.Add( "playercontroller" );
		shape.Radius = Radius;
		shape.Center = renderer.LocalPosition;
		var body = root.AddComponent<Rigidbody>();
		body.MassOverride = 15;
		body.EnableImpactDamage = false;
		var agent = root.AddComponent<NavMeshAgent>();
		agent.Radius = Radius;
		agent.Height = 2 * Radius;
		agent.Enabled = false;
		root.AddComponent<NavigationLayer>().RunSpeed = 8.2f * Scale;
		var senses = root.AddComponent<SensesLayer>();
		senses.SightRange = 12 * Scale;
		senses.FieldOfView = 360;
		root.AddComponent<AnimationLayer>().EnableFootstepSounds = false;
		var npc = root.AddComponent<Babbler>();
		npc.Faction = Factions.Monster;
		npc.Renderer = renderer;
		npc.Health = 12;
		npc.EyeHeight = Radius + 0.2f * Scale;
		npc.DisplayName = "Babbler";
	}

	protected override void OnStart()
	{
		base.OnStart();
		body = GetComponent<Rigidbody>();
		previousRenderPosition = WorldPosition;
		if ( !Networking.IsHost ) return;
		StartGraph();
		if ( Scene.PhysicsWorld.Gravity.z < 0 ) body.GravityScale = Gravity * Scale / -Scene.PhysicsWorld.Gravity.z;
		Launch( new Vector3( Random.Shared.Float( -1, 1 ), Random.Shared.Float( -1, 1 ), Random.Shared.Float( 2, 4 ) ) * Scale );
	}

	protected override void Think()
	{
		target = null;
		float closest = 12 * 12 * Scale * Scale;
		foreach ( var candidate in Senses.VisibleHostiles )
		{
			if ( !candidate.IsValid() || AlienTargeting.IsIgnoredPlayer( candidate ) ) continue;
			float distance = WorldPosition.DistanceSquared( candidate.WorldPosition );
			if ( distance < closest ) (target, closest) = (candidate, distance);
		}
		Animation.SetLookTarget( target );
		if ( !target.IsValid() ) { Movement.Stop(); return; }
		var aim = CalculateAimPoint( target );
		var path = Scene.Trace.Ray( EyePosition, aim ).WithTag( "world" ).Run();
		if ( path.Fraction < 0.9f && (aim - EyePosition).WithZ( 0 ).Length >= 2 * Scale )
			Movement.MoveTo( target.WorldPosition, Radius, faceTarget: target );
		else Movement.Stop();
	}

	internal static (Vector3 Velocity, float FlightTime) JumpArc( Vector3 offset, float heightRoll, float speedRoll )
	{
		var flat = offset.WithZ( 0 );
		float distance = flat.Length;
		float height = Math.Max( offset.z + 0.75f * 6.7f / Gravity, 6.7f / Gravity ) * (1 + (heightRoll - 0.35f) * 0.15f);
		float vertical = MathF.Sqrt( 2 * Gravity * height );
		float time = vertical / Gravity + MathF.Sqrt( 2 * Math.Max( 0, height - offset.z ) / Gravity );
		float speed = Math.Clamp( distance / Math.Max( time, 0.0001f ), 5, 15 );
		speed = Math.Clamp( speed * (1 + (speedRoll - 0.5f) * 0.2f), 5, 15 );
		return ((distance > 0.0001f ? flat / distance : flat) * speed + Vector3.Up * Math.Min( vertical, 12 ), time);
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( !Networking.IsHost || !body.IsValid() || Health <= 0 || PhysicsCarrier.IsValid() ) return;
		var origin = SphereCenter;
		var velocity = body.Velocity;
		if ( previousVelocity.LengthSquared > 0 && (velocity - previousVelocity).WithZ( 0 ).LengthSquared > 0.0001f * Scale * Scale && nextHit <= 0 )
			CheckImpact( previousPosition, previousVelocity.Normal );
		previousVelocity = body.Velocity;
		previousPosition = origin;
		if ( !target.IsValid() || Movement.MoveTarget.HasValue ) return;
		var aim = CalculateAimPoint( target );
		var player = target.GetComponent<PlayerController>();
		var lead = target.GetComponent<Rigidbody>()?.Velocity ?? Vector3.Zero;
		bool playerTarget = player.IsValid() || target.Tags.HasAny( "ns2_marine", "ns2_exo", "ns2_lifeform" );
		aim += lead * (playerTarget ? 0.9f : 0.4f);
		var offset = (aim - origin) / Scale;
		if ( IsOnGround )
		{
			nudges = 0;
			if ( nextAttack > 0 ) return;
			if ( lead.LengthSquared > 0.0001f )
				offset += new Vector3( Random.Shared.Float( -0.55f, 0.55f ), Random.Shared.Float( -0.55f, 0.55f ), Random.Shared.Float( -0.5f, 0.5f ) );
			else if ( offset.z <= 0.5f )
				offset += Rotation.FromYaw( Random.Shared.Float( 0, 360 ) ).Forward * Random.Shared.Float( 0.2f, 0.4f ) + Vector3.Up * Random.Shared.Float( -0.075f, 0.075f );
			else offset.z += Random.Shared.Float( -0.2f, 0.2f );
			var arc = JumpArc( offset, Random.Shared.NextSingle(), Random.Shared.NextSingle() ).Velocity;
			var planar = arc.WithZ( 0 ).ClampLength( offset.WithZ( 0 ).Length / 0.4f );
			Launch( planar.WithZ( arc.z ) * Scale );
			nextAttack = 0.5f;
		}
		else if ( nudges < 2 && nextNudge <= 0 )
		{
			var flat = velocity.WithZ( 0 );
			var desired = offset.WithZ( 0 ).Normal * Math.Max( flat.Length, 3.5f * Scale );
			body.Velocity = Vector3.Lerp( flat, desired, 0.1f ).WithZ( velocity.z );
			nextNudge = 0.5f;
			nudges++;
		}
	}

	void CheckImpact( Vector3 start, Vector3 direction )
	{
		const float reach = 0.7f * Scale;
		var trace = Scene.Trace.Ray( start, start + direction * reach ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ns2_babbler" ).UseHitboxes();
		var hit = trace.Run();
		if ( !hit.Hit || hit.GameObject?.Tags.Has( "world" ) == true )
		{
			var extents = new Vector3( direction.WithX( 0 ).Length, direction.WithY( 0 ).Length, direction.WithZ( 0 ).Length ) * reach;
			hit = trace.FromTo( start, hit.EndPosition ).Size( -extents, extents ).Run();
		}
		if ( hit.GameObject?.GetComponentInParent<IDamageable>() is not Component victim || AlienTargeting.IsIgnoredPlayer( victim.GameObject ) || GetDisposition( victim.GameObject ) != Disposition.Hostile ) return;
		nextHit = 0.42f + Random.Shared.Float( -0.25f, 0.25f );
		lastHit = 0;
		((IDamageable)victim).OnDamage( new DamageInfo { Damage = 8, Position = SphereCenter, Origin = start, Attacker = GameObject, Weapon = GameObject } );
		if ( victim.IsValid() && (victim.GameObject.Tags.HasAny( "ns2_marine", "ns2_exo", "ns2_lifeform" ) || victim.GetComponent<PlayerController>().IsValid()) )
			Launch( (victim.WorldPosition - SphereCenter).Normal * (2 * Scale) );
	}

	protected override GameObject CreateRagdoll() => null;

	protected override void OnDeath( IDamageable.DeathEvent e )
	{
		DeathEffect( SphereCenter );
		base.OnDeath( e );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void DeathEffect( Vector3 position )
	{
		CinematicLoader.Play( "cinematics/alien/babbler/death.cinematic", position, Rotation.Identity );
		PlaySoundAt( "alien/structures/death_small", position );
	}

	static void PlaySoundAt( string id, Vector3 position )
	{
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{id}.sound" ) is { } sound )
			Sound.Play( sound, position );
	}

	internal static void RegisterSounds( MountContext context )
	{
		foreach ( var id in new[] { "alien/babbler/jump", "alien/babbler/attack_jump", "alien/babbler/idle", "alien/structures/death_small" } )
			context.Add( ResourceType.GameResource, $"sounds/{id}.sound", new SparkSoundLoader( id ) );
	}
}
