namespace Halo2;

public sealed record EnergyDischarge( BaseCombatWeapon.BallisticConfig Ballistics, float Speed, float MinimumAngle, float DryGravity, float WetGravity,
	float MinimumDamage, float DryFalloff, float WetFalloff, Dictionary<string, float> BodyScales, bool Explosive,
	ContrailAppearance Trail, float Heat, float Spent, SoundEvent Sound, float TurnRate, float TrackingAngle, float TrackingRange );

public abstract partial class Weapon
{
	[Property] public float MinimumPelletAngle { get; set; } = 2;
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
		public ProjectileContrail Trail;
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
		var pellets = new Pellet[(discharge?.Ballistics ?? Ballistics).Pellets];
		ref uint randomState = ref BlamRandom.GameplayState( Scene );
		for ( int i = 0; i < pellets.Length; i++ )
		{
			var forward = Spread.Deviate( aim, minimum, maximum, ref randomState );
			pellets[i].Velocity = forward * speed + forward * inherited;
			if ( trail is { Lifetime: > 0, PointRate: > 0 } )
				pellets[i].Trail = ProjectileContrail.Create( trail, ray.Position, pellets[i].Velocity );
		}
		GameObject target = null;
		Vector3 localPoint = default;
		if ( discharge is { TurnRate: > 0 } && Holder is PlayerController player )
		{
			target = FindAttackTarget( player, new Ray( player.EyeTransform.Position, aim ), new( discharge.TrackingAngle, discharge.TrackingRange ), out var point );
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
		if ( (discharge?.Speed ?? PelletSpeed) <= 0 || ballistics.Range <= 0 ) return;
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
				if ( pellet.Velocity == Vector3.Zero ) continue;
				var start = origin + pellet.Offset;
				if ( target.IsValid() )
				{
					var desired = (target.WorldTransform.PointToWorld( localPoint ) - start).Normal;
					var direction = pellet.Velocity.Normal;
					float angle = System.MathF.Acos( Vector3.Dot( direction, desired ).Clamp( -1, 1 ) );
					if ( angle > 0 && desired.LengthSquared > 0 )
						pellet.Velocity = Vector3.Slerp( direction, desired, System.MathF.Min( 1, discharge.TurnRate * dt / angle ) ).Normal * pellet.Velocity.Length;
				}
				bool wet = tick == 0 ? originWet : InWater( start );
				float fall = (wet ? waterGravity : airGravity) * (4.1712594f * 120) * dt;
				float step = dt * System.MathF.Min( 1, (ballistics.Range - pellet.Travelled) / (pellet.Velocity.Length * dt) );
				var displacement = (pellet.Velocity + Vector3.Down * (0.5f * fall)) * step;
				pellet.Velocity.z -= fall;
				float distance = displacement.Length;
				if ( pellet.Trail.IsValid() && !pellet.Trail.Velocity.AlmostEqual( displacement / step ) )
					pellet.Trail.SetSegment( start, displacement / step, firedAt + tick / 30.0 );
				bool atRange = step < dt || pellet.Travelled + distance >= ballistics.Range;
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
				pellets[i].Velocity = Vector3.Zero;
				flying--;
			}
			if ( results is { Count: > 0 } )
			{
				ProjectileImpacts( origin, results.ToArray() );
				results.Clear();
			}

		}
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
