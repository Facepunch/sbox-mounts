namespace Halo2;

public sealed record BounceDefinition( float MinimumAngle, float MaximumAngle, float ParallelFriction, float NormalFriction, int FuseTicks );

public sealed record ShardDefinition( Vector2 TurnRate, float TrackingAngle, float TrackingRange, Vector2 FuseSeconds,
	float Damage, float Force, Dictionary<string, float> BodyScales );

public sealed record EnergyDischarge( BaseCombatWeapon.BallisticConfig Ballistics, float Speed, float MinimumAngle, float DryGravity, float WetGravity,
	float MinimumDamage, float DryFalloff, float WetFalloff, Dictionary<string, float> BodyScales, bool Explosive,
	ContrailAppearance Trail, float Heat, float Spent, SoundEvent Sound, string SoundMarker, float TurnRate, float TrackingAngle, float TrackingRange );

public abstract partial class Weapon
{
	[Property] public float MinimumPelletAngle { get; set; } = 2;
	[Property] public BounceDefinition Bounce { get; set; }
	[Property] public ShardDefinition Shard { get; set; }
	[Property] public string ProjectileModel { get; set; }
	[Property] public float PelletSpeed { get; set; }
	[Property] public float AirGravityScale { get; set; }
	[Property] public float WaterGravityScale { get; set; } = 0.2f;
	[Property] public float MinimumDamage { get; set; }
	[Property] public Dictionary<string, float> PelletBodyScales { get; set; }
	[Property] public bool PelletExplosive { get; set; }
	[Property] public float DamageFalloffRange { get; set; } = 960;
	[Property] public float UnderwaterDamageFalloffRange { get; set; } = 120;

	protected Ray PlayerFiringRay( PlayerController player, Vector3 aim )
	{
		var eye = player.EyeTransform.Position;
		var muzzle = WorldModel.IsValid() ? WorldModel.GetComponent<SkinnedModelRenderer>()?.GetAttachment( "primary_trigger" ) : null;
		if ( muzzle is null ) return new Ray( eye, aim );

		var offset = muzzle.Value.Position - eye;
		float distance = (aim.x * offset.x + aim.z * offset.z) + aim.y * offset.y;
		var trace = BulletTrace( new Ray( eye, distance < 0 ? -aim : aim ), System.MathF.Abs( distance ), 0 ).Run();
		return new Ray( trace.StartedSolid ? eye : trace.EndPosition, aim );
	}

	[Property] public ContrailAppearance Trail { get; set; }

	struct Pellet
	{
		public Vector3 Offset;
		public Vector3 Velocity;
		public float Travelled;
		public int DetonateAtTick;
		public float TurnRate;
		public float FuseSeconds;
		public ProjectileContrail Trail;
		public GameObject Model;
	}

	protected readonly record struct PelletResult( Vector3 Position, bool Hit = false, Vector3 Direction = default, Vector3 Normal = default,
		GameObject Target = null, string SurfacePath = null, string Bone = null );

	protected void LaunchProjectiles( Ray ray, float spreadDegrees, EnergyDischarge discharge = null )
	{
		float maximum = spreadDegrees.DegreeToRadian();
		float minimum = (discharge?.MinimumAngle ?? MinimumPelletAngle).DegreeToRadian();
		float speed = discharge?.Speed ?? PelletSpeed;
		var trail = discharge is null ? Trail : discharge.Trail;
		var aim = ray.Forward;
		var velocity = Sandbox.Npcs.BaseNpc.GetVelocity( Holder?.GameObject );
		// Project before spread, then apply the inherited speed along each pellet's direction.
		float inherited = System.MathF.Max( 0, (aim.x * velocity.x + aim.z * velocity.z) + aim.y * velocity.y );
		var ballistics = discharge?.Ballistics ?? Ballistics;
		if ( speed <= 0 || ballistics.Range < 0 ) return;
		var model = ProjectileModel is { Length: > 0 } path ? Model.Load( path ) : null;
		var pellets = new Pellet[ballistics.Pellets];
		ref uint randomState = ref BlamRandom.GameplayState( Scene );
		for ( int i = 0; i < pellets.Length; i++ )
		{
			var forward = Spread.Deviate( aim, minimum, maximum, ref randomState );
			pellets[i].Velocity = forward * speed + forward * inherited;
			pellets[i].TurnRate = discharge?.TurnRate ?? 0;
			if ( Shard is {} shard )
			{
				pellets[i].TurnRate = (float)SampleRange( shard.TurnRate, ref randomState );
				pellets[i].FuseSeconds = (float)(System.Math.Ceiling( SampleRange( shard.FuseSeconds, ref randomState ) * 30 ) / 30);
			}
			if ( model is { IsError: false } )
			{
				var body = new GameObject( false, model.Name );
				body.NetworkMode = NetworkMode.Object;
				body.WorldTransform = new Transform( ray.Position, Rotation.LookAt( forward ) );
				body.AddComponent<ModelRenderer>().Model = model;
				body.NetworkSpawn( true, null );
				pellets[i].Model = body;
			}
			if ( trail is { Lifetime: > 0, PointRate: > 0 } )
				pellets[i].Trail = ProjectileContrail.Create( trail, ray.Position, pellets[i].Velocity );
		}
		GameObject target = null;
		Vector3 localPoint = default;
		if ( (discharge is { TurnRate: > 0 } || Shard is not null) && Holder is PlayerController player )
		{
			target = FindAttackTarget( player, new Ray( player.EyeTransform.Position, aim ), new( discharge?.TrackingAngle ?? Shard.TrackingAngle, discharge?.TrackingRange ?? Shard.TrackingRange ), out var point );
			if ( target.IsValid() ) localPoint = target.WorldTransform.PointToLocal( point );
		}
		_ = FlyPellets( ray.Position, pellets, Attacker, discharge, target, localPoint );
	}

	async System.Threading.Tasks.Task FlyPellets( Vector3 origin, Pellet[] pellets, GameObject attacker, EnergyDischarge discharge, GameObject target, Vector3 localPoint )
	{
		var scene = Scene;
		bool InWater( Vector3 point )
		{
			if ( WaterBody.Find( scene, point ).IsValid() ) return true;
			var trace = scene.Trace.Ray( point, point ).WithTag( "water" ).HitTriggers();
			var hit = trace.Run();
			if ( !hit.StartedSolid ) return false;
			// A wave's trigger covers its entire height envelope. Only independent water volumes count here.
			static bool IsLegacyWater( SceneTraceResult hit ) => hit.StartedSolid && !hit.GameObject.GetComponentInParent<WaterBody>().IsValid();
			return IsLegacyWater( hit ) || trace.RunAll().Any( IsLegacyWater );
		}
		var ballistics = discharge?.Ballistics ?? Ballistics;
		float minDamage = discharge?.MinimumDamage ?? MinimumDamage;
		float dryRange = discharge?.DryFalloff ?? DamageFalloffRange, wetRange = discharge?.WetFalloff ?? UnderwaterDamageFalloffRange;
		var bodyScales = discharge?.BodyScales ?? PelletBodyScales;
		bool explosive = discharge?.Explosive ?? PelletExplosive;
		float airGravity = discharge?.DryGravity ?? AirGravityScale, waterGravity = discharge?.WetGravity ?? WaterGravityScale;
		var tags = AttackDamageTags;
		var blast = ImpactBlast;
		var bounce = Bounce;
		float maxRange = ballistics.Range > 0 ? ballistics.Range : float.PositiveInfinity;
		var detonation = DetonationEffectPath;
		bool originWet = InWater( origin );
		int flying = pellets.Length;
		var shooter = Holder?.GameObject ?? GameObject;
		// Holstering disables the weapon, but pellets already in flight keep travelling.
		var flight = TaskSource.Create( scene.EnabledToken );
		System.Collections.Generic.List<PelletResult> results = null;
		// Halo advances a new projectile once during the firing tick, then at 30 Hz.
		const float dt = 1f / 30;
		double firedAt;
		using ( scene.Push() ) firedAt = Time.NowDouble;
		for ( int tick = 0; flying > 0; tick++ )
		{
			while ( tick > 0 && scene.IsValid() )
			{
				using ( scene.Push() )
					if ( Time.NowDouble >= firedAt + tick / 30.0 ) break;
				await flight.Frame();
			}
			if ( !scene.IsValid() ) return;
			using var sceneScope = scene.Push();
			for ( int i = 0; i < pellets.Length; i++ )
			{
				var pellet = pellets[i];
				var start = origin + pellet.Offset;
				if ( pellet.DetonateAtTick > 0 && tick >= pellet.DetonateAtTick )
				{
					if ( pellet.Model.IsValid() ) pellet.Model.Destroy();
					pellet.Trail?.Finish( start, firedAt + tick / 30.0 );
					if ( blast is not null ) Detonate( scene, blast, start, Vector3.Zero, attacker );
					if ( detonation is not null ) PresentDetonation( detonation, scene, start, Vector3.Up, pellet.Velocity.Normal );
					pellets[i] = default;
					flying--;
					continue;
				}
				if ( pellet.Velocity == Vector3.Zero ) continue;
				if ( target.IsValid() )
				{
					var desired = (target.WorldTransform.PointToWorld( localPoint ) - start).Normal;
					var direction = pellet.Velocity.Normal;
					float angle = System.MathF.Acos( Vector3.Dot( direction, desired ).Clamp( -1, 1 ) );
					if ( angle > 0 && desired.LengthSquared > 0 )
						pellet.Velocity = Vector3.Slerp( direction, desired, System.MathF.Min( 1, pellet.TurnRate * dt / angle ) ).Normal * pellet.Velocity.Length;
				}
				bool wet = tick == 0 ? originWet : InWater( start );
				float fall = (wet ? waterGravity : airGravity) * (4.1712594f * 120) * dt;
				float step = dt * System.MathF.Min( 1, (maxRange - pellet.Travelled) / (pellet.Velocity.Length * dt) );
				var displacement = (pellet.Velocity + Vector3.Down * (0.5f * fall)) * step;
				pellet.Velocity.z -= fall;
				float distance = displacement.Length;
				if ( pellet.Trail.IsValid() && !pellet.Trail.Velocity.AlmostEqual( displacement / step ) )
					pellet.Trail.SetSegment( start, displacement / step, firedAt + tick / 30.0 );
				bool atRange = step < dt || pellet.Travelled + distance >= maxRange;
				// A trace excludes its endpoint. Halo includes world geometry at maximum range,
				// but requires an object contact to be strictly nearer.
				var sweep = atRange ? displacement * 2 : displacement;
				var hit = scene.Trace.Ray( start, start + sweep ).IgnoreGameObjectHierarchy( shooter )
					.WithCollisionRules( "bullet" ).WithoutTags( "playercontroller" ).UseHitboxes().Run();
				if ( atRange && hit.Hit )
				{
					float remaining = Vector3.Dot( start + displacement - hit.HitPosition, displacement );
					hit.Hit = remaining > 0 || (remaining == 0 && (hit.Collider is Terrain || hit.GameObject.Tags.Has( "world" )));
				}
				if ( !hit.Hit )
				{
					pellet.Offset += displacement;
					pellet.Travelled += distance;
					if ( pellet.Model.IsValid() )
					{
						if ( atRange ) pellet.Model.Destroy();
						else pellet.Model.WorldTransform = new Transform( origin + pellet.Offset, Rotation.LookAt( pellet.Velocity ) );
					}
					if ( atRange )
					{
						pellet.Trail?.Finish( origin + pellet.Offset, firedAt + tick / 30.0 + step );
						(results ??= new()).Add( new( origin + pellet.Offset ) );
						pellet.Velocity = Vector3.Zero;
						flying--;
					}
					pellets[i] = pellet;
					continue;
				}
				if ( bounce is not null && !hit.StartedSolid && hit.GameObject.IsValid() &&
					!hit.GameObject.GetComponentInParent<PlayerController>().IsValid() && !hit.GameObject.GetComponentInParent<Sandbox.Npcs.BaseNpc>().IsValid() )
				{
					float dot = Vector3.Dot( hit.Normal, pellet.Velocity );
					float angle = System.MathF.Asin( (-dot / pellet.Velocity.Length).Clamp( -1, 1 ) );
					if ( angle >= bounce.MinimumAngle && angle <= bounce.MaximumAngle )
					{
						var perpendicular = hit.Normal * dot;
						pellet.Velocity = (pellet.Velocity - perpendicular) * (1 - bounce.ParallelFriction) - perpendicular * (1 - bounce.NormalFriction);
						pellet.Offset = OutsideSurface( hit.HitPosition, hit.Normal ) - origin;
						pellet.Travelled += hit.Distance;
						if ( pellet.DetonateAtTick == 0 ) pellet.DetonateAtTick = tick + bounce.FuseTicks;
						if ( pellet.Model.IsValid() ) pellet.Model.WorldTransform = new Transform( hit.HitPosition, Rotation.LookAt( pellet.Velocity ) );
						pellets[i] = pellet;
						continue;
					}
				}
				// Halo switches the range at water crossings and retains total flight distance.
				var range = dryRange;
				if ( range != wetRange )
				{
					// Sample the incoming side of a coincident water face without skipping a nearby crossing.
					static float Before( float value, float direction ) => direction > 0 ? System.MathF.BitDecrement( value )
						: direction < 0 ? System.MathF.BitIncrement( value ) : value;
					var p = hit.HitPosition;
					var d = hit.Direction;
					if ( InWater( new Vector3( Before( p.x, d.x ), Before( p.y, d.y ), Before( p.z, d.z ) ) ) )
						range = wetRange;
				}
				pellet.Trail?.Finish( hit.HitPosition, firedAt + tick / 30.0 + step * (hit.Distance / distance) );
				float fraction = range > 0 ? (1 - (pellet.Travelled + hit.Distance) / range).Clamp( 0, 1 ) : 1;
				float damage = (1 - fraction) * minDamage + fraction * ballistics.Damage;
				damage *= BodyMaterial.DamageScale( hit, bodyScales, explosive );
				hit.StartPosition = origin;
				hit.Direction = pellet.Velocity.Normal;
				(results ??= new()).Add( new( hit.HitPosition, true, hit.Direction, hit.Normal, hit.GameObject, hit.Surface?.ResourcePath, hit.Hitbox?.Bone?.Name ) );
				ApplyDelayedHit( hit, damage, ballistics.Force, attacker, tags is { IsEmpty: false } ? new TagSet( tags.TryGetAll() ) : null );
				if ( Shard is {} shard && hit.GameObject.IsValid() && (hit.GameObject.GetComponentInParent<PlayerController>().IsValid() || hit.GameObject.GetComponentInParent<Sandbox.Npcs.BaseNpc>().IsValid()) )
					_ = AttachShard( scene, shard, pellet, hit, attacker );
				else if ( pellet.Model.IsValid() ) pellet.Model.Destroy();
				if ( blast is not null ) Detonate( scene, blast, hit.HitPosition, hit.Normal, attacker );
				if ( detonation is not null ) PresentDetonation( detonation, hit.GameObject, hit.HitPosition, hit.Normal, hit.Direction );
				pellets[i] = default;
				flying--;
			}
			if ( results is { Count: > 0 } )
			{
				ProjectileImpacts( origin, results.ToArray() );
				results.Clear();
			}

		}
	}

	static double SampleRange( Vector2 range, ref uint random ) => range.x == range.y ? range.x
		: range.x + ((double)range.y - range.x) * (BlamRandom.Next( ref random ) * (double)(1f / 65535));

	async System.Threading.Tasks.Task AttachShard( Scene scene, ShardDefinition shard, Pellet pellet, SceneTraceResult hit, GameObject attacker )
	{
		var target = hit.GameObject;
		if ( !target.IsValid() ) { if ( pellet.Model.IsValid() ) pellet.Model.Destroy(); return; }
		var local = target.WorldTransform.PointToLocal( hit.HitPosition );
		if ( pellet.Model.IsValid() )
		{
			pellet.Model.WorldPosition = hit.HitPosition;
			pellet.Model.SetParent( target, true );
		}
		var clock = TaskSource.Create( scene.EnabledToken );
		double due = Time.NowDouble + pellet.FuseSeconds;
		while ( target.IsValid() )
		{
			using ( scene.Push() )
			{
				if ( Time.NowDouble >= due )
				{
					hit.StartPosition = hit.HitPosition = target.WorldTransform.PointToWorld( local );
					float damage = shard.Damage * BodyMaterial.DamageScale( hit, shard.BodyScales, true );
					ApplyDelayedHit( hit, damage, shard.Force, attacker, new TagSet { "explosion" }, false );
					break;
				}
			}
			await clock.Frame();
		}
		if ( pellet.Model.IsValid() ) pellet.Model.Destroy();
	}

	protected virtual void ProjectileImpacts( Vector3 origin, PelletResult[] results ) =>
		PlayProjectileImpacts( IsValid ? this : null, origin, results );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void PlayProjectileImpacts( Weapon weapon, Vector3 origin, PelletResult[] results )
	{
		if ( Application.IsDedicatedServer || !weapon.IsValid() ) return;
		foreach ( var hit in results )
		{
			if ( !hit.Hit ) continue;
			var surface = hit.SurfacePath is {} path ? GameResource.Load<Surface>( path ) : null;
			weapon.OnShootEffects( new ShotEffect( hit.Position, true, hit.Normal, hit.Target, surface, origin, NoEvents: true ) );
		}
	}
}
