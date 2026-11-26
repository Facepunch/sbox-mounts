using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NS2;

[Title( "Babbler" ), Category( "Natural Selection 2" )]
public sealed partial class Babbler : Ns2Npc
{
	const float Scale = MapLoader.Scale;
	const float Radius = 0.25f * Scale;
	const float Gravity = 9.7f;
	Rigidbody body;
	GameObject target;
	bool reachedTarget;
	TimeUntil nextAttack, nextHit, nextNudge;
	TimeSince lastHit = 1;
	int nudges;
	Vector3 previousPosition, previousVelocity;
	Vector3 SphereCenter => WorldPosition + Vector3.Up * Radius;

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer )
	{
		renderer.LocalPosition = Vector3.Up * Radius;
		var hitbox = root.AddComponent<ManualHitbox>();
		hitbox.CenterA = renderer.LocalPosition;
		hitbox.Radius = 2 * Radius;
		var shape = new GameObject( root, true, "Collider" ).AddComponent<SphereCollider>();
		shape.GameObject.Tags.Add( "playercontroller", "ns2_movement" );
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

	[Sync( SyncFlags.FromHost )] internal int ClingSlot { get; private set; }
	internal AlienLifeform BroodOwner { get; set; }
	internal AlienLifeform ClingTarget { get; private set; }
	TimeUntil nextClingLeap, freeRoam;
	TimeUntil? ownerLossTimeout;
	Vector3? orderedPosition;
	bool ReturningToOwner => BroodOwner.IsValid() && orderedPosition is null && !target.IsValid() && freeRoam <= 0;

	internal void Dispatch( GameObject enemy, Vector3 destination )
	{
		ClingTarget = null;
		if ( BroodOwner.IsValid() && enemy == BroodOwner.GameObject )
		{
			target = null;
			orderedPosition = null;
			freeRoam = 0;
			return;
		}
		if ( ClingSlot != 0 )
		{
			BroodOwner.ReleaseSlot( ClingSlot );
			DetachFromSocket();
		}
		ClingTarget = enemy?.GetComponent<AlienLifeform>();
		if ( ClingTarget.IsValid() && ClingTarget.Faction != Faction ) ClingTarget = null;
		target = ClingTarget.IsValid() ? null : enemy;
		orderedPosition = enemy.IsValid() ? null : destination + new Vector3(
			Random.Shared.Float( -0.25f, 0.25f ), Random.Shared.Float( -0.25f, 0.25f ), Random.Shared.Float( 0, 0.5f ) ) * Scale;
		reachedTarget = false;
		freeRoam = ClingTarget.IsValid() ? 0 : 6;
	}

	internal void ClingTo( GameObject point, int slot )
	{
		Movement.Stop();
		GameObject.Parent = point;
		ClingSlot = slot;
		ClingTarget = null;
		LocalTransform = global::Transform.Zero;
		Renderer.LocalTransform = global::Transform.Zero;
		SetFreeMovement( false );
	}

	void SetFreeMovement( bool enabled )
	{
		GetComponent<Rigidbody>( true ).Enabled = enabled;
		GetComponentInChildren<SphereCollider>( true ).Enabled = enabled;
		GetComponent<ManualHitbox>( true ).Enabled = enabled;
		GetComponent<NavigationLayer>( true ).Enabled = enabled;
		GetComponent<SensesLayer>( true ).Enabled = enabled;
		GetComponent<AnimationLayer>( true ).Enabled = enabled;
	}

	public override void OnParentDestroy()
	{
		if ( Networking.IsHost && ClingSlot != 0 && !GameObject.IsDestroyed ) DetachFromSocket();
	}

	void DetachFromSocket()
	{
		var position = WorldPosition;
		GameObject.Parent = null;
		WorldPosition = position - Vector3.Up * Radius;
		Renderer.LocalPosition = Vector3.Up * Radius;
		ClingSlot = 0;
		SetFreeMovement( true );
		body = GetComponent<Rigidbody>();
		previousRenderPosition = WorldPosition;
		GameObject.Network.Refresh();
	}

	void ApproachOwner()
	{
		if ( !ClingTarget.IsValid() || ClingTarget.Health <= 0 ) ClingTarget = null;
		var host = ClingTarget ?? BroodOwner;
		var point = host.BabblerApproachPoint;
		if ( !point.IsValid() ) { ClingTarget = null; return; }
		var delta = point.WorldPosition - SphereCenter;
		float distance = delta.Length;
		if ( distance <= 0.7f * Scale )
		{
			host.AttachHatchling( this );
			return;
		}
		var wall = Scene.Trace.Ray( EyePosition, point.WorldPosition ).IgnoreGameObjectHierarchy( host.GameObject )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ns2_babbler" ).Run();
		if ( distance < 2.5f * Scale && wall.Fraction >= 0.9f )
		{
			body.Velocity = delta.ClampLength( 8.2f * Scale * Time.Delta ) / Time.Delta;
			return;
		}
		if ( IsOnGround ) Movement.MoveTo( point.WorldPosition, Radius, 8.2f * Scale );
		if ( wall.Fraction < 0.9f || delta.z <= -0.5f * Scale || nextClingLeap > 0 ) return;
		var arc = JumpArc( (delta / Scale).ClampLength( 5 ), Random.Shared.NextSingle(), Random.Shared.NextSingle() );
		Launch( arc.Velocity * Scale );
		nextClingLeap = arc.FlightTime * 0.5f + Random.Shared.Float( 0, 0.3f );
	}

	internal void Kill()
	{
		DeathEffect( WorldPosition );
		GameObject.Destroy();
	}

	protected override void OnDestroy()
	{
		if ( BroodOwner.IsValid() ) BroodOwner.BabblerRemoved( this );
		base.OnDestroy();
	}

	protected override void OnStart()
	{
		if ( ClingSlot != 0 )
		{
			if ( Networking.IsHost ) StartGraph();
			return;
		}
		base.OnStart();
		body = GetComponent<Rigidbody>();
		previousRenderPosition = WorldPosition;
		if ( !Networking.IsHost ) return;
		StartGraph();
		if ( Scene.PhysicsWorld.Gravity.z < 0 ) body.GravityScale = Gravity * Scale / -Scene.PhysicsWorld.Gravity.z;
		Launch( new Vector3( Random.Shared.Float( -1, 1 ), Random.Shared.Float( -1, 1 ), Random.Shared.Float( 2, 4 ) ) * Scale );
		if ( BroodOwner.IsValid() ) ApproachOwner();
	}

	protected override void Think()
	{
		if ( ClingSlot != 0 || ReturningToOwner ) return;
		if ( orderedPosition is { } destination )
		{
			if ( !IsOnGround ) return;
			bool finished = Movement.MoveTarget == destination && Movement.Status is MovementLayer.MovementStatus.Arrived or MovementLayer.MovementStatus.Failed;
			if ( !finished && (WorldPosition - destination).WithZ( 0 ).LengthSquared >= 0.25f * Scale * Scale )
			{
				Movement.MoveTo( destination, Radius, 8.2f * Scale );
				return;
			}
			orderedPosition = null;
			Movement.Stop();
		}
		var previous = target;
		if ( !CanHunt( target ) ) target = null;
		if ( target is null )
		{
			float closest = 12 * 12 * Scale * Scale;
			foreach ( var candidate in Senses.VisibleHostiles )
			{
				if ( !CanHunt( candidate ) ) continue;
				float distance = WorldPosition.DistanceSquared( candidate.WorldPosition );
				if ( distance < closest ) (target, closest) = (candidate, distance);
			}
			if ( target is null && IsOnGround && nextRoam <= 0 ) target = SiblingTarget();
		}
		if ( previous != target ) reachedTarget = false;
		Animation.SetLookTarget( target );
		if ( !target.IsValid() )
		{
			if ( previous is not null ) Movement.Stop();
			Roam();
			return;
		}
		feed = 0;
		var aim = CalculateAimPoint( target );
		var path = Scene.Trace.Ray( EyePosition, aim ).WithTag( "world" ).Run();
		if ( path.Fraction < 0.9f && (aim - EyePosition).WithZ( 0 ).Length >= 2 * Scale )
		{
			float distance = WorldPosition.DistanceSquared( target.WorldPosition );
			reachedTarget |= distance < 25 * Scale * Scale;
			if ( reachedTarget && distance > 30 * 30 * Scale * Scale )
			{
				target = null;
				reachedTarget = false;
				Animation.SetLookTarget( null );
				Movement.Stop();
				return;
			}
			Movement.MoveTo( target.WorldPosition, Radius, 8.2f * Scale, target );
		}
		else Movement.Stop();
	}

	bool CanHunt( GameObject enemy )
	{
		if ( !enemy.IsValid() || AlienTargeting.IsIgnoredPlayer( enemy ) || GetDisposition( enemy ) != Disposition.Hostile ) return false;
		var victim = enemy.GetComponentInChildren<IDamageable>();
		return victim is Component { Active: true } && (victim.MaxHealth <= 0 || victim.Health > 0);
	}

	GameObject SiblingTarget()
	{
		foreach ( var sibling in Scene.GetAllComponents<Babbler>() )
		{
			if ( sibling == this || sibling.Faction != Faction || sibling.BroodOwner != BroodOwner || sibling.ClingSlot != 0 || !sibling.target.IsValid() ) continue;
			if ( WorldPosition.DistanceSquared( sibling.WorldPosition ) < 30 * 30 * Scale * Scale && CanHunt( sibling.target ) )
				return sibling.target;
		}
		return null;
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
		if ( Networking.IsHost && BroodOwner is not null && !BroodOwner.IsValid() )
		{
			if ( ClingSlot != 0 ) DetachFromSocket();
			BroodOwner = null;
			ownerLossTimeout = 1;
		}
		if ( ClingSlot != 0 ) return;
		base.OnFixedUpdate();
		if ( !Networking.IsHost || !body.IsValid() || Health <= 0 ) return;
		if ( ownerLossTimeout is { } timeout )
		{
			if ( BroodOwner.IsValid() ) ownerLossTimeout = null;
			else if ( timeout <= 0 )
			{
				Kill();
				return;
			}
		}
		if ( PhysicsCarrier.IsValid() ) return;
		if ( ReturningToOwner )
		{
			ApproachOwner();
			return;
		}
		var origin = SphereCenter;
		var velocity = body.Velocity;
		if ( previousVelocity.LengthSquared > 0 && (velocity - previousVelocity).WithZ( 0 ).LengthSquared > 0.0001f * Scale * Scale && nextHit <= 0 )
			CheckImpact( previousPosition, previousVelocity.Normal );
		previousVelocity = body.Velocity;
		previousPosition = origin;
		if ( !target.IsValid() || Movement.MoveTarget.HasValue ) return;
		var aim = CalculateAimPoint( target );
		var player = target.GetComponent<PlayerController>();
		var lead = GetVelocity( target );
		bool playerTarget = player.IsValid() || target.Tags.HasAny( "ns2_marine", "ns2_exo", "ns2_lifeform" );
		aim += lead * (playerTarget ? 0.9f : 0.4f);
		var offset = (aim - origin) / Scale;
		if ( IsOnGround )
		{
			nudges = 0;
			if ( nextAttack > 0 ) return;
			if ( lead.LengthSquared > 0.0001f )
				offset += new Vector3( Random.Shared.Float( -0.55f, 0.55f ), Random.Shared.Float( -0.55f, 0.55f ), 0 );
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
		var trace = Scene.Trace.Ray( start, start + direction * reach ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ns2_babbler" ).UseDamageHitboxes();
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

	protected override void OnDamaging( IDamageable.DamageEvent e )
	{
		var tags = e.DamageInfo.Tags;
		if ( ClingSlot != 0 || tags.HasAny( "splash", "structuresonly", "gas", "nervegas", "armoronly", "biological" ) )
			e.AcceptedDamage = 0;
		else if ( tags.Has( "clusterflame" ) && e.AcceptedDamage > 0 )
			e.AcceptedDamage += SupportDamage.ClusterBurnDamage;
		else if ( tags.Has( "flame" ) )
			e.AcceptedDamage *= 2.5f;
		if ( e.AcceptedDamage <= 0 ) e.Cancelled = true;
		base.OnDamaging( e );
	}

	protected override GameObject CreateRagdoll() => null;

	protected override void OnDeath( IDamageable.DeathEvent e )
	{
		GetComponent<ManualHitbox>( true ).Enabled = false;
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
