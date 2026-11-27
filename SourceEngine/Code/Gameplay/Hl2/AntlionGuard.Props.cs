using System;
using Sandbox.Npcs;
using MoveStatus = Sandbox.Npcs.Layers.MovementLayer.MovementStatus;

namespace Source1.Hl2;

/// <summary>
/// Hitting props at its enemy: finding one worth hitting, walking round behind it, turning to its enemy and swatting it
/// at the enemy's feet.
/// </summary>
public sealed partial class AntlionGuard
{
	const float MinPropMass = 8, MaxPropMass = 750, MinPropRadius = 6, MaxPropSpeed = 16;
	const float ChaseCone = 45;
	const float SearchRadius = 350, UnreachableSearchRadius = 3000;
	const float LineTolerance = 240, UnreachableLineTolerance = 480;
	const float HitSpotGap = 32, HitSpotFloor = 48;
	/// <summary>The guard's bounding radius, which sets how far behind a prop it stands.</summary>
	const float BoundingRadius = 75;
	const int FailedMemory = 8;
	const float PropDrift = 360, PropGiveUp = 30;
	const float SwatReach = 256, SwatCone = 55, SwatMiss = 300, ThrowPoseLimit = 45;
	const float LaunchMin = 512, LaunchMax = 1024, LaunchScale = 3, LaunchLift = 350;
	/// <summary>No physics object moves faster than this.</summary>
	const float MaxPhysicsSpeed = 2000;
	const float SwatPushRadius = 150, SwatPushStrength = 250;

	/// <summary>Props any guard has claimed, so two don't go for the same one.</summary>
	static readonly HashSet<Rigidbody> Claimed = [];

	Rigidbody _prop;
	Vector3 _propFrom, _hitSpot;
	readonly List<Rigidbody> _failed = [];
	TimeSince _sincePath;

	/// <summary>Remembers a prop as no good, the last few at most.</summary>
	void Fail( Rigidbody prop )
	{
		_failed.Add( prop );
		if ( _failed.Count > FailedMemory )
			_failed.RemoveAt( 0 );
	}

	/// <summary>A prop to hit at an enemy it can't reach: near itself, else near the enemy.</summary>
	Rigidbody PropOutOfReach( GameObject enemy ) =>
		FindProp( enemy, WorldPosition, chasing: false, UnreachableSearchRadius, UnreachableLineTolerance )
		?? FindProp( enemy, enemy.WorldPosition, chasing: false, UnreachableSearchRadius, UnreachableLineTolerance );

	/// <summary>
	/// The cheapest prop worth hitting at its enemy. Chasing, one closer than the enemy and within 45 degrees of the line to it,
	/// costing more toward the cone's edge; otherwise the nearest to <paramref name="centre"/> within the radius. Either way it can
	/// see it, the prop sits still, a clear line from it ends near the enemy, and there is room to stand behind it.
	/// </summary>
	Rigidbody FindProp( GameObject enemy, Vector3 centre, bool chasing, float radius, float tolerance )
	{
		var me = WorldPosition;
		var foe = enemy.WorldPosition;
		var enemyDistance = (foe - me).WithZ( 0 ).Length;
		var reach = chasing ? enemyDistance : radius;
		if ( reach < 1 )
			return null;

		Rigidbody best = null;
		var bestCost = chasing ? enemyDistance * enemyDistance : float.MaxValue;
		var examined = new HashSet<Rigidbody>();
		foreach ( var go in Scene.FindInPhysics( new Sphere( chasing ? me : centre, reach ) ) )
		{
			var body = go.GetComponentInParent<Rigidbody>();
			if ( !body.IsValid() || !examined.Add( body ) || examined.Count > 200 || !IsProp( body ) || _failed.Contains( body ) || Claimed.Contains( body ) )
				continue;

			var bounds = body.GetWorldBounds();
			var c = bounds.Center;
			float cost;
			if ( chasing )
			{
				var toProp = (c - me).WithZ( 0 );
				var angle = Vector3.GetAngle( toProp, (foe - me).WithZ( 0 ) );
				if ( toProp.Length >= enemyDistance || angle > ChaseCone )
					continue;
				// 1 dead ahead to 4 at the cone's edge, in the cosine of the angle
				var edge = 1 - MathF.Cos( ChaseCone * MathF.PI / 180 );
				cost = toProp.LengthSquared * (1 + 3 * (1 - MathF.Cos( angle * MathF.PI / 180 )) / edge);
			}
			else
			{
				cost = (c - centre).LengthSquared;
				if ( cost > radius * radius )
					continue;
			}

			if ( cost >= bestCost || !Visible( Npc.EyePosition, c, body.GameObject ) || !LinesUp( c, enemy, tolerance ) || HitSpot( body, foe ) is null )
				continue;
			best = body;
			bestCost = cost;
		}
		return best;
	}

	bool IsProp( Rigidbody body )
	{
		if ( body.GameObject == GameObject || !body.MotionEnabled || body.PhysicsBody is not { IsValid: true } )
			return false;
		var tags = body.GameObject.Tags;
		if ( tags.Has( "debris" ) || tags.Has( "ragdoll" ) || tags.Has( "npc" ) || tags.Has( "player" ) || tags.Has( "projectile" ) || tags.Has( ZombieProps.DebrisTag )
			|| body.GetComponentInParent<BaseNpc>().IsValid() || body.GetComponentInParent<PlayerController>().IsValid()
			|| body.GetComponentInParent<ModelPhysics>().IsValid() || body.GetComponentInParent<BaseCombatWeapon>().IsValid() )
			return false;
		var mass = MathF.Max( body.Mass, body.MassOverride );
		return mass is >= MinPropMass and <= MaxPropMass && body.Velocity.Length < MaxPropSpeed && body.GetWorldBounds().Size.Length * 0.5f >= MinPropRadius;
	}

	/// <summary>A clear line through the world from the prop toward the enemy ends close enough to the enemy.</summary>
	bool LinesUp( Vector3 from, GameObject enemy, float tolerance )
	{
		var aim = Npcs.CapturePoint( enemy );
		var tr = Scene.Trace.Ray( from, aim ).IgnoreDynamic().WithoutTags( Npcs.PastCharacters ).Run();
		return tr.EndPosition.Distance( aim ) <= tolerance;
	}

	bool Visible( Vector3 from, Vector3 to, GameObject seen )
	{
		var tr = Scene.Trace.Ray( from, to ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "debris" ).Run();
		return !tr.Hit || tr.GameObject.IsValid() && (tr.GameObject == seen || seen.IsDescendant( tr.GameObject ) || tr.GameObject.IsDescendant( seen ));
	}

	/// <summary>
	/// Where it stands to hit a prop at the enemy: on the far side of the prop from the enemy, the prop's extent plus its own
	/// bounding radius plus 32 out, or 45 degrees round either way; its body, dropped from 48 above the prop's middle, has to
	/// land on floor within 48 below it.
	/// </summary>
	Vector3? HitSpot( Rigidbody prop, Vector3 enemy )
	{
		var agent = GetComponent<NavMeshAgent>();
		var radius = agent?.Radius ?? 45;
		var height = agent?.Height ?? 130;
		var bounds = prop.GetWorldBounds();
		var centre = bounds.Center;
		var half = bounds.Size * 0.5f;
		var away = (centre - enemy).WithZ( 0 ).Normal;
		if ( away.IsNearlyZero() )
			return null;

		foreach ( var yaw in new[] { 0f, 45, -45 } )
		{
			var direction = Rotation.FromYaw( yaw ) * away;
			var extent = MathF.Abs( direction.x ) * half.x + MathF.Abs( direction.y ) * half.y;
			var spot = centre + direction * (extent + BoundingRadius + HitSpotGap);
			var hull = new BBox( new Vector3( -radius, -radius, 0 ), new Vector3( radius, radius, height ) );
			var floor = Scene.Trace.Box( hull, spot + Vector3.Up * HitSpotFloor, spot - Vector3.Up * HitSpotFloor ).IgnoreGameObjectHierarchy( GameObject )
				.IgnoreGameObjectHierarchy( prop.GameObject ).WithoutTags( Npcs.PastCharacters ).Run();
			if ( !floor.Hit || floor.StartedSolid )
				continue;
			if ( Scene.NavMesh.GetClosestPoint( floor.EndPosition, 32 ) is { } onMesh && onMesh.WithZ( 0 ).Distance( floor.EndPosition.WithZ( 0 ) ) < 24 )
				return onMesh;
		}
		return null;
	}

	bool GoForProp( Rigidbody prop, GameObject enemy )
	{
		if ( HitSpot( prop, enemy.WorldPosition ) is not { } spot )
			return false;
		_prop = prop;
		_propFrom = prop.WorldPosition;
		_hitSpot = spot;
		Claimed.Add( prop );
		Npc.Movement?.MoveTo( spot, 16, Npc.PaceFor( true ) );
		_sincePath = 0;
		Begin( Doing.Approach );
		return true;
	}

	/// <summary>A prop it went for was moved far off, took too long, or vanished: it gives up on it.</summary>
	bool PropLost() => !_prop.IsValid() || !_enemy.IsValid() || _prop.WorldPosition.Distance( _propFrom ) > PropDrift || _actionTime > PropGiveUp;

	bool Approaching()
	{
		if ( PropLost() )
			return End( failed: true );

		var arrived = (WorldPosition - _hitSpot).WithZ( 0 ).Length <= 24 || Npc.Movement?.Status == MoveStatus.Arrived;
		if ( Npc.Movement?.Status == MoveStatus.Failed && _actionTime > 0.5f )
			return End( failed: true );
		if ( !arrived )
		{
			if ( _sincePath > 1 )
			{
				_sincePath = 0;
				Npc.Movement?.MoveTo( _hitSpot, 16, Npc.PaceFor( true ) );
			}
			return true;
		}

		Npc.Movement?.Stop();
		Begin( Doing.Turn );
		return true;
	}

	/// <summary>At its spot it turns to face the enemy, then needs the prop close and in front to swat it.</summary>
	bool TurningToSwat()
	{
		if ( PropLost() )
			return End( failed: true );

		if ( Npcs.TurnToward( GameObject, YawTo( _enemy.WorldPosition ), StandingTurnRate ) > 3 && _actionTime < 1.5f )
			return true;

		var centre = Npc.Centre;
		var prop = _prop.GetWorldBounds().Center;
		var off = Angles.NormalizeAngle( YawTo( prop ) - WorldRotation.Yaw() );
		if ( prop.Distance( centre ) > SwatReach || MathF.Abs( off ) > SwatCone || _swat is null )
			return End( failed: true );

		ThrowYaw = Math.Clamp( off, -ThrowPoseLimit, ThrowPoseLimit );
		Begin( Doing.Swat, _swat );
		return true;
	}

	bool Swatting()
	{
		var at = _swatEvent * Duration( _swat, 1.4f );
		if ( _lastTime < at && _actionTime >= at )
			Launch();
		_lastTime = _actionTime;
		return _actionTime < Duration( _swat, 1.4f ) || End( false );
	}

	/// <summary>
	/// The swat: the prop flies at the enemy's feet, fast and nearly flat, spinning, with a thud and a push to the props around
	/// it. A prop that has rolled off out of reach is missed.
	/// </summary>
	void Launch()
	{
		if ( !_prop.IsValid() || !_enemy.IsValid() )
		{
			GetComponent<Charge>()?.ImpactPush( WorldPosition, SwatPushRadius, SwatPushStrength );
			return;
		}

		var prop = _prop;
		var centre = prop.GetWorldBounds().Center;
		if ( centre.Distance( Npc.Centre ) > SwatMiss )
		{
			Claimed.Remove( prop );
			Fail( prop );
			_prop = null;
			return;
		}

		var feet = _enemy.WorldPosition;
		var distance = centre.Distance( feet );
		var velocity = ((feet - centre).Normal * (Math.Clamp( distance, LaunchMin, LaunchMax ) * LaunchScale) + Vector3.Up * LaunchLift).ClampLength( MaxPhysicsSpeed );
		PlaySound( "NPC_AntlionGuard.HitHard" );
		GetComponent<Charge>()?.ImpactPush( centre, SwatPushRadius, SwatPushStrength );
		// Now it is the guard's throw, and doesn't hurt it if it bounces back
		PlayerThrows.Forget( prop.GameObject );
		prop.Sleeping = false;
		prop.Velocity = velocity;
		var spin = new Vector3( Game.Random.Float( -180, 180 ), 100, Game.Random.Float( -360, 360 ) ) * (MathF.PI / 180);
		prop.AngularVelocity = prop.WorldRotation * spin;

		Claimed.Remove( prop );
		_failed.Clear();
		_prop = null;
	}
}
