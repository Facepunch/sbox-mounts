namespace Halo2;

[System.Flags]
public enum ProjectileOrientation
{
	None = 0,
	AlongVelocity = 1,
	SteeringAdjustsOrientation = 1 << 6,
	CanTrackBehind = 1 << 8,
	RobotronSteering = 1 << 9
}

public sealed record ProjectileSpeedProfile( float FinalSpeed, float StartDistance, float InverseDistance, bool FasterForPlayer, float NpcScale );

public sealed record BounceDefinition( float MinimumAngle, float MaximumAngle, float ParallelFriction, float NormalFriction, int FuseTicks )
{
	public Dictionary<string, string[]> Sounds { get; init; }
}

public sealed record ShardDefinition( Vector2 TurnRate, float TrackingAngle, float TrackingRange, Vector2 FuseSeconds,
	BlastDamage AttachedDamage, BlastDamage DetonationDamage, string ProjectileTag, int CombineCount, BlastDamage CombinedDamage, BlastDamage CombinedBlast, string CombinedEffect )
{
	public string DetonationEffect { get; init; }
}

public sealed record EnergyDischarge( BaseCombatWeapon.BallisticConfig Ballistics, float Speed, float MinimumAngle, float DryGravity, float WetGravity,
	float MinimumDamage, float DryFalloff, float WetFalloff, Dictionary<string, float> BodyScales, bool Explosive,
	ContrailAppearance Trail, float Heat, float Spent, SoundEvent Sound, string SoundMarker, float TurnRate, float TrackingAngle, float TrackingRange )
{
	public string Volume { get; init; }
	public bool CanHeadshot { get; init; }
	public ProjectileSpeedProfile SpeedProfile { get; init; }
	public ProjectileOrientation Orientation { get; init; }
	public float TargetLeading { get; init; }
}

public abstract partial class Weapon
{
	[Property] public float MinimumPelletAngle { get; set; } = 2;
	[Property] public BounceDefinition Bounce { get; set; }
	[Property] public ShardDefinition Shard { get; set; }
	[Property] public string ProjectileVolume { get; set; }
	[Property] public string ProjectileModel { get; set; }
	[Property] public ProjectileOrientation Orientation { get; set; }
	[Property] public float TargetLeading { get; set; }
	[Property] public ProjectileSpeedProfile SpeedProfile { get; set; }
	[Property] public float PelletSpeed { get; set; }
	[Property] public float AirGravityScale { get; set; }
	[Property] public float WaterGravityScale { get; set; } = 0.2f;
	[Property] public float MinimumDamage { get; set; }
	[Property] public Dictionary<string, float> PelletBodyScales { get; set; }
	[Property] public bool PelletExplosive { get; set; }
	[Property] public bool CanHeadshot { get; set; }
	[Property] public string PlayerShieldMaterial { get; set; }

	[Property] public float DamageFalloffRange { get; set; } = 960;
	[Property] public float UnderwaterDamageFalloffRange { get; set; } = 120;

	internal void ApplyProjectileHit( SceneTraceResult hit, float damage, BodyMaterial.Impact impact, float force, GameObject attacker, TagSet tags, EnergyDischarge discharge = null )
	{
		if ( IsValid && (hit.GameObject.GetComponentInParent<PlayerController>().IsValid() || hit.GameObject.GetComponentInParent<Sandbox.Npcs.BaseNpc>().IsValid()) )
		{
			// Hit claims carry section tags. Send the material with the unscaled amount so the host can resolve shield overflow.
			var shot = new TagSet();
			if ( tags is not null ) shot.Add( tags );
			shot.Add( discharge is null ? ProjectileDamageSystem.Direct : ProjectileDamageSystem.Charged );
			if ( impact.Protected ) shot.Add( ProjectileDamageSystem.ProtectedBody );
			else if ( impact.Scale != 1 && impact.Material is {} material ) shot.Add( material );
			tags = shot;
		}
		else damage *= impact.Scale;
		ApplyDelayedHit( hit, damage, force, attacker, tags, canHeadshot: discharge?.CanHeadshot ?? CanHeadshot );
	}

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
	protected virtual (GameObject Target, Vector3 LocalPoint, float TurnRate) ProjectileLock => default;

	struct Pellet
	{
		public Vector3 Forward;
		public Vector3 Offset;
		public Vector3 Velocity;
		public float ForwardSpeed;
		public float ForwardDistance;
		public float Travelled;
		public int DetonateAtTick;
		public PhysicsShape BounceSurface;
		public PhysicsShape PenetratedShape;
		public WaterBody PenetratedWater;
		public float PenetrationLimit;
		public Vector3 BounceNormal;
		public float TurnRate;
		public float FuseSeconds;
		public ProjectileContrail Trail;
		public GameObject Body;
		public ProjectileExhaust Exhaust;

		public void Reflect( SceneTraceResult hit, Vector3 origin, float parallelFriction, float normalFriction, bool alongVelocity, Vector3 velocityChange = default )
		{
			var normal = hit.Normal * hit.Normal.Dot( Velocity );
			Velocity = (Velocity - normal) * (1 - parallelFriction) - normal * (1 - normalFriction) + velocityChange;
			Offset = hit.HitPosition - origin;
			BounceSurface = hit.Shape;
			BounceNormal = hit.Normal;
			Travelled += hit.Distance;
			if ( alongVelocity && Velocity.LengthSquared > 0 ) Forward = Velocity.Normal;
			if ( Body.IsValid() ) MoveProjectileBody( this, hit.HitPosition );
		}
	}

	protected readonly record struct PelletResult( Vector3 Position, bool Hit = false, Vector3 Direction = default, Vector3 Normal = default,
		GameObject Target = null, string SurfacePath = null, string Bone = null, bool Charged = false, bool? Alternate = null );

	protected void LaunchProjectiles( Ray ray, Vector2 cone, EnergyDischarge discharge = null )
	{
		float maximum = cone.y.DegreeToRadian();
		float minimum = cone.x.DegreeToRadian();
		float initialSpeed = discharge?.Speed ?? PelletSpeed;
		var profile = discharge is null ? SpeedProfile : discharge.SpeedProfile;
		float scale = profile is { FasterForPlayer: true } ? (Holder is PlayerController ? 1.5f : profile.NpcScale) : 1;
		float speed = initialSpeed * scale;
		var trail = discharge is null ? Trail : discharge.Trail;
		var aim = ray.Forward;
		var velocity = Sandbox.Npcs.BaseNpc.GetVelocity( Holder?.GameObject );
		// Project before spread, then apply the inherited speed along each pellet's direction.
		float inherited = System.MathF.Max( 0, (aim.x * velocity.x + aim.z * velocity.z) + aim.y * velocity.y );
		var ballistics = discharge?.Ballistics ?? Ballistics;
		if ( speed <= 0 || ballistics.Range < 0 ) return;
		var model = ProjectileModel is { Length: > 0 } path ? Model.Load( path ) : null;
		var volumePath = discharge is null ? ProjectileVolume : discharge.Volume;
		var volume = volumePath is null ? null : GameObject.GetPrefab( volumePath );
		var trailOffset = model is { IsError: false } && !string.IsNullOrEmpty( trail?.Marker )
			? model.Attachments.GetTransform( trail.Marker )?.Position ?? Vector3.Zero : Vector3.Zero;
		var pellets = new Pellet[ballistics.Pellets];
		var guidance = ProjectileLock;
		ref uint randomState = ref BlamRandom.GameplayState( Scene );
		for ( int i = 0; i < pellets.Length; i++ )
		{
			var forward = Spread.Deviate( aim, minimum, maximum, ref randomState );
			pellets[i].Forward = forward;
			pellets[i].Velocity = forward * speed + forward * inherited;
			pellets[i].ForwardSpeed = speed;
			pellets[i].TurnRate = discharge?.TurnRate ?? guidance.TurnRate;
			if ( Shard is {} shard )
			{
				pellets[i].TurnRate = (float)SampleRange( shard.TurnRate, ref randomState );
				pellets[i].FuseSeconds = (float)(System.Math.Ceiling( SampleRange( shard.FuseSeconds, ref randomState ) * 30 ) / 30);
			}
			if ( volume.IsValid() || model is { IsError: false } )
			{
				var body = volume.IsValid() ? volume.Clone( new CloneConfig { StartEnabled = false } ) : new GameObject( false, model.Name );
				body.NetworkMode = NetworkMode.Object;
				body.WorldTransform = new Transform( ray.Position, Rotation.LookAt( forward, Perpendicular( forward ) ) );
				if ( model is { IsError: false } ) body.AddComponent<ModelRenderer>().Model = model;
				pellets[i].Exhaust = body.GetComponentInChildren<ProjectileExhaust>( true );
				if ( pellets[i].Exhaust.IsValid() ) pellets[i].Exhaust.FiredAt = Time.NowDouble;
				if ( volume.IsValid() )
					foreach ( var light in body.GetComponentsInChildren<ProjectileLightVolume>( true ) )
						if ( light.EndTick > 0 ) light.FiredAt = Time.NowDouble;
				body.NetworkSpawn( true, null );
				pellets[i].Body = body;
			}
			if ( trail is { Lifetime: > 0, PointRate: > 0 } )
				pellets[i].Trail = ProjectileContrail.Create( trail, ray.Position, pellets[i].Velocity, pellets[i].Body, trailOffset );
		}
		var target = guidance.Target;
		var localPoint = guidance.LocalPoint;
		if ( (discharge is { TurnRate: > 0 } || Shard is not null) && Holder is PlayerController player )
		{
			target = FindAttackTarget( player, new Ray( player.EyeTransform.Position, aim ), new( discharge?.TrackingAngle ?? Shard.TrackingAngle, discharge?.TrackingRange ?? Shard.TrackingRange ), out var point );
			if ( target.IsValid() ) localPoint = target.WorldTransform.PointToLocal( point );
		}
		_ = FlyPellets( ray.Position, pellets, Attacker, discharge, target, localPoint, profile, initialSpeed, scale );
	}

	internal static bool InWater( Scene scene, Vector3 point )
	{
		if ( WaterBody.Find( scene, point ).IsValid() ) return true;
		var trace = scene.Trace.Ray( point, point ).WithTag( "water" ).HitTriggers();
		var hit = trace.Run();
		if ( !hit.StartedSolid ) return false;
		// A wave's trigger covers its entire height envelope. Only independent water volumes count here.
		static bool IsLegacyWater( SceneTraceResult hit ) => hit.StartedSolid && !hit.GameObject.GetComponentInParent<WaterBody>().IsValid();
		return IsLegacyWater( hit ) || trace.RunAll().Any( IsLegacyWater );
	}

	async System.Threading.Tasks.Task FlyPellets( Vector3 origin, Pellet[] pellets, GameObject attacker, EnergyDischarge discharge, GameObject target, Vector3 localPoint, ProjectileSpeedProfile profile, float initialSpeed, float scale )
	{
		var scene = Scene;
		var ballistics = discharge?.Ballistics ?? Ballistics;
		var orientation = discharge?.Orientation ?? Orientation;
		float leading = discharge?.TargetLeading ?? TargetLeading;
		var targetBody = target.IsValid() ? target.GetComponentInParent<Sandbox.Npcs.BaseNpc>()?.GameObject
			?? target.GetComponentInParent<PlayerController>()?.GameObject ?? target : null;
		bool alongVelocity = (orientation & ProjectileOrientation.AlongVelocity) != 0;
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
		bool originWet = InWater( scene, origin );
		bool traceWater = Shard is not null && scene.GetAllComponents<WaterBody>().Any();
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
				if ( pellet.Body is not null && !pellet.Body.IsValid() )
				{
					// A world cleanup can remove a live projectile before it reaches a contact.
					if ( pellet.Trail.IsValid() ) pellet.Trail.Finish( start, firedAt + tick / 30.0 );
					pellets[i] = default;
					flying--;
					continue;
				}
				if ( pellet.DetonateAtTick > 0 && tick >= pellet.DetonateAtTick )
				{
					if ( pellet.Body.IsValid() ) pellet.Body.Destroy();
					pellet.Trail?.Finish( start, firedAt + tick / 30.0 );
					if ( blast is not null ) Detonate( scene, blast, start, Vector3.Zero, attacker );
					if ( detonation is not null ) PresentDetonation( detonation, scene, start, Vector3.Up, pellet.Velocity.Normal );
					pellets[i] = default;
					flying--;
					continue;
				}
				if ( pellet.Velocity == Vector3.Zero ) continue;
				var startingVelocity = pellet.Velocity;
				if ( target.IsValid() && pellet.TurnRate > 0 )
				{
					var delta = target.WorldTransform.PointToWorld( localPoint ) - start;
					if ( leading > 0 )
					{
						var velocity = Sandbox.Npcs.BaseNpc.GetVelocity( targetBody );
						float seconds = System.MathF.Min( 2, delta.Length / pellet.Velocity.Length );
						// Halo caps the prediction at two seconds and two world units.
						delta += (velocity * (leading * seconds)).ClampLength( 240 );
					}
					if ( (orientation & ProjectileOrientation.RobotronSteering) != 0 )
					{
						var forward = delta.Normal;
						var side = Vector3.Cross( Vector3.Up, forward ).Normal;
						double phase = (System.Math.Floor( firedAt * 30 ) + tick) * (6.5 / 30);
						var (sin, cos) = System.Math.SinCos( phase );
						// The orbit follows the shared 30 Hz clock and narrows as the rocket approaches.
						delta += (side * (float)cos + Vector3.Cross( forward, side ) * (float)sin)
							* System.MathF.Min( delta.Length * 0.05f, 240 );
					}
					var desired = delta.Normal;
					var direction = pellet.Velocity.Normal;
					var axis = Vector3.Cross( direction, desired );
					if ( axis.LengthSquared > 0 && ((orientation & ProjectileOrientation.CanTrackBehind) != 0 || direction.Dot( desired ) > 0) )
					{
						var turn = Rotation.FromAxis( axis.Normal, (pellet.TurnRate * dt).RadianToDegree() );
						pellet.Velocity = turn * pellet.Velocity;
						if ( (orientation & ProjectileOrientation.SteeringAdjustsOrientation) != 0 )
						{
							pellet.Forward = desired;
							if ( pellet.Body.IsValid() )
								pellet.Body.WorldRotation = ProjectileRotation( desired, pellet.Body.WorldRotation.Up );
						}
					}
				}
				float duration = dt;
				double segmentTime = firedAt + tick / 30.0;
			AdvanceSegment:
				bool wet = tick == 0 && duration == dt ? originWet : InWater( scene, start );
				float fall = (wet ? waterGravity : airGravity) * (4.1712594f * 120) * duration;
				float step = duration * System.MathF.Min( 1, (maxRange - pellet.Travelled) / (pellet.Velocity.Length * duration) );
				var velocityChange = Vector3.Down * fall;
				if ( profile is { InverseDistance: > 0 } && initialSpeed != profile.FinalSpeed )
				{
					float progress = ((pellet.ForwardDistance - profile.StartDistance * scale) * profile.InverseDistance).Clamp( 0, 1 );
					float nextSpeed = (initialSpeed + (profile.FinalSpeed - initialSpeed) * progress) * scale;
					velocityChange += pellet.Forward * (nextSpeed - pellet.ForwardSpeed);
					pellet.ForwardSpeed = nextSpeed;
					pellet.ForwardDistance += nextSpeed * duration;
				}
				// Guidance, acceleration and gravity all contribute over the same native tick.
				var displacement = (startingVelocity + ((pellet.Velocity - startingVelocity) + velocityChange) * 0.5f) * step;
				pellet.Velocity += velocityChange;
				if ( alongVelocity && pellet.Velocity.LengthSquared > 0 ) pellet.Forward = pellet.Velocity.Normal;
				float distance = displacement.Length;
				bool rangeLimited = step < duration;
				bool penetrationEnds = false;
				if ( pellet.PenetratedShape is {} medium && pellet.Travelled + distance >= pellet.PenetrationLimit )
				{
					float remaining = pellet.PenetrationLimit - pellet.Travelled;
					var limit = start + displacement.Normal * remaining;
					bool inside = pellet.PenetratedWater.IsValid() ? pellet.PenetratedWater.Contains( limit )
						: scene.Trace.Ray( limit, limit ).WithCollisionRules( "bullet" ).RunAll().Any( h => h.Shape == medium && h.StartedSolid );
					if ( inside )
					{
						step *= remaining / distance;
						displacement = limit - start;
						distance = remaining;
						penetrationEnds = true;
					}
				}
				if ( pellet.Trail.IsValid() )
					pellet.Trail.SetSegment( start, displacement / step, segmentTime );
				bool atRange = penetrationEnds || rangeLimited || pellet.Travelled + distance >= maxRange;
				// A trace excludes its endpoint. Halo includes world geometry at maximum range,
				// but requires an object contact to be strictly nearer.
				var sweep = atRange ? displacement * 2 : displacement;
				var trace = scene.Trace.Ray( start, start + sweep ).IgnoreGameObjectHierarchy( shooter )
					.WithCollisionRules( "bullet" ).WithoutTags( "playercontroller" ).UseHitboxes();
				var hit = trace.Run();
				if ( !pellet.PenetratedWater.IsValid() && pellet.PenetratedShape is {} penetrated )
				{
					if ( hit.Hit && hit.Shape == penetrated )
						hit = trace.RunAll().FirstOrDefault( contact => contact.Shape != penetrated );
					else if ( !hit.StartedSolid ) pellet.PenetratedShape = null;
				}
				if ( pellet.BounceSurface is {} surface && hit.StartedSolid && hit.Shape == surface && Vector3.Dot( sweep, pellet.BounceNormal ) > 0 )
				{
					// A cast starting on the reflecting face can report that face at fraction zero.
					hit = trace.RunAll().FirstOrDefault( contact => !(contact.StartedSolid && contact.Shape == surface) );
				}
				pellet.BounceSurface = null;
				if ( traceWater )
					TraceWaterEntry( scene, start, start + sweep, pellet.PenetratedWater, ref hit );
				if ( atRange && hit.Hit )
				{
					float remaining = Vector3.Dot( start + displacement - hit.HitPosition, displacement );
					hit.Hit = remaining > 0 || (remaining == 0 && (hit.Collider is Terrain || hit.GameObject.Tags.Has( "world" )));
				}
				if ( !hit.Hit )
				{
					pellet.Offset += displacement;
					pellet.Travelled += distance;
					if ( pellet.PenetratedWater.IsValid() && !pellet.PenetratedWater.Contains( start + displacement ) )
					{
						pellet.PenetratedWater = null;
						pellet.PenetratedShape = null;
					}
					if ( pellet.Exhaust.IsValid() )
						pellet.Exhaust.AccelerationRange = profile is null ? 1 : ((pellet.Travelled - profile.StartDistance) * profile.InverseDistance).Clamp( 0, 1 );
					if ( pellet.Body.IsValid() )
					{
						if ( atRange ) pellet.Body.Destroy();
						else MoveProjectileBody( pellet, origin + pellet.Offset );
					}
					if ( atRange )
					{
						pellet.Trail?.Finish( origin + pellet.Offset, segmentTime + step );
						(results ??= new()).Add( new( origin + pellet.Offset ) );
						pellet = default;
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
						if ( bounce.Sounds is {} sounds && ContactMaterial.Lookup( sounds, hit.Surface, true ) is { Length: > 0 } cues )
							PlayBounceSounds( cues, hit.HitPosition );
						float contactFraction = (hit.Distance / distance).Clamp( 0, 1 );
						// The original reflects the incoming velocity before applying acceleration up to contact.
						pellet.Velocity = startingVelocity;
						pellet.Reflect( hit, origin, bounce.ParallelFriction, bounce.NormalFriction, alongVelocity, velocityChange * contactFraction );
						if ( pellet.DetonateAtTick == 0 ) pellet.DetonateAtTick = tick + bounce.FuseTicks;
						float contactTime = step * contactFraction;
						float remainingTime = step - contactTime;
						if ( remainingTime > 0 && remainingTime < step && pellet.Travelled < maxRange && pellet.Velocity.LengthSquared > 0 )
						{
							segmentTime += contactTime;
							start = hit.HitPosition;
							duration = remainingTime;
							startingVelocity = pellet.Velocity;
							goto AdvanceSegment;
						}
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
					if ( InWater( scene, new Vector3( Before( p.x, d.x ), Before( p.y, d.y ), Before( p.z, d.z ) ) ) )
						range = wetRange;
				}
				float fraction = range > 0 ? (1 - (pellet.Travelled + hit.Distance) / range).Clamp( 0, 1 ) : 1;
				float damage = (1 - fraction) * minDamage + fraction * ballistics.Damage;
				var impact = BodyMaterial.ResolveHit( hit, bodyScales, explosive, out var impactSurface );
				hit.Direction = pellet.Velocity.Normal;
				bool? alternate = null;
				ShardResponse? shardResponse = null;
				if ( Shard is not null && this is HandheldWeapon { BulletContacts: {} contacts }
					&& ContactMaterial.Lookup( contacts, impactSurface, true ) is { Shard: {} rule } contact )
				{
					bool unit = hit.GameObject.GetComponentInParent<PlayerController>().IsValid() || hit.GameObject.GetComponentInParent<Sandbox.Npcs.BaseNpc>().IsValid();
					float angle = System.MathF.Asin( (-hit.Normal.Dot( hit.Direction )).Clamp( -1, 1 ) );
					alternate = rule.SelectAlternate( scene, angle, unit, contact.AlternateChance );
					shardResponse = alternate.Value ? rule.Alternate : rule.Response;
					if ( shardResponse == ShardResponse.Overpenetrate && hit.Shape is not null )
					{
						pellet.Offset = hit.HitPosition - origin;
						pellet.Travelled += hit.Distance;
						pellet.Velocity *= 1 - rule.EntryFriction;
						pellet.PenetratedShape = hit.Shape;
						pellet.PenetratedWater = hit.Collider is { IsTrigger: true } ? hit.GameObject.GetComponentInParent<WaterBody>() : null;
						pellet.PenetrationLimit = pellet.Travelled + rule.PenetrationDistance;
						if ( pellet.Body.IsValid() ) MoveProjectileBody( pellet, hit.HitPosition );
						(results ??= new()).Add( new( hit.HitPosition, true, hit.Direction, hit.Normal, hit.GameObject, impactSurface?.ResourcePath, Alternate: alternate ) );
						pellets[i] = pellet;
						continue;
					}
					if ( shardResponse == ShardResponse.Bounce && !hit.StartedSolid )
					{
						(results ??= new()).Add( new( hit.HitPosition, true, hit.Direction, hit.Normal, hit.GameObject, impactSurface?.ResourcePath, hit.Hitbox?.Bone?.Name, Alternate: alternate ) );
						pellet.Reflect( hit, origin, rule.Friction.x, rule.Friction.y, alongVelocity );
						pellets[i] = pellet;
						continue;
					}
				}
				pellet.Trail?.Finish( hit.HitPosition, segmentTime + step * (hit.Distance / distance) );
				hit.StartPosition = origin;
				(results ??= new()).Add( new( hit.HitPosition, true, hit.Direction, hit.Normal, hit.GameObject, impactSurface?.ResourcePath, hit.Hitbox?.Bone?.Name, discharge is not null, alternate ) );
				ApplyProjectileHit( hit, damage, impact, ballistics.Force, attacker, tags, discharge );
				if ( Shard is {} shard && shardResponse == ShardResponse.Attach && hit.GameObject.IsValid() )
					_ = AttachShard( scene, shard, pellet, hit, attacker );
				else if ( pellet.Body.IsValid() ) pellet.Body.Destroy();
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

	static void TraceWaterEntry( Scene scene, Vector3 start, Vector3 end, WaterBody current, ref SceneTraceResult hit )
	{
		foreach ( var candidate in scene.Trace.Ray( start, end ).WithTag( "water" ).HitTriggers().RunAll() )
		{
			if ( !candidate.Collider.IsValid() || !candidate.Collider.IsTrigger ) continue;
			var water = candidate.GameObject.GetComponentInParent<WaterBody>();
			if ( !water.IsValid() || water == current || water.Contains( start ) ) continue;
			var entry = candidate;
			if ( !water.Contains( entry.HitPosition ) )
			{
				// The trigger encloses the wave crests. Locate the fluid surface inside it.
				if ( !water.Contains( end ) ) continue;
				float dry = entry.Fraction, wet = 1;
				while ( true )
				{
					float mid = (dry + wet) * 0.5f;
					if ( mid == dry || mid == wet ) break;
					if ( water.Contains( Vector3.Lerp( start, end, mid ) ) ) wet = mid;
					else dry = mid;
				}
				entry.Fraction = wet;
				entry.EndPosition = entry.HitPosition = Vector3.Lerp( start, end, wet );
				water.SampleSurface( water.ToSurface( entry.HitPosition ), out entry.Normal );
			}
			if ( !hit.Hit || entry.Fraction < hit.Fraction ) hit = entry;
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void PlayBounceSounds( string[] cues, Vector3 position )
	{
		if ( Application.IsDedicatedServer ) return;
		foreach ( string cue in cues )
			if ( GameResource.Load<SoundEvent>( cue ) is {} sound ) Sound.Play( sound, position );
	}

	static void MoveProjectileBody( Pellet pellet, Vector3 position )
	{
		var exhaust = pellet.Exhaust;
		var start = exhaust.IsValid() ? exhaust.WorldPosition : default;
		var rotation = ProjectileRotation( pellet.Forward, pellet.Body.WorldRotation.Up );
		pellet.Body.WorldTransform = new Transform( position, rotation );
		if ( exhaust.IsValid() ) exhaust.StepFlight( start );
	}

	static Rotation ProjectileRotation( Vector3 forward, Vector3 up ) =>
		Rotation.LookAt( forward, Vector3.Cross( forward, up ).LengthSquared < 1e-8f ? Perpendicular( forward ) : up );

	static double SampleRange( Vector2 range, ref uint random ) => range.x == range.y ? range.x
		: range.x + ((double)range.y - range.x) * (BlamRandom.Next( ref random ) * (double)(1f / 65535));

	sealed class AttachedShard( string tag, float fuse, GameObject target, PhysicsBody body, Transform contact )
	{
		public readonly string Tag = tag;
		public readonly float Fuse = fuse;
		public readonly GameObject Target = target;
		Transform targetLocal = target.WorldTransform.ToLocal( contact );
		readonly Transform boneLocal = body.IsValid() ? body.Transform.ToLocal( contact ) : default;
		public double Due = Time.NowDouble + fuse;
		public bool Consumed;

		public Transform GetContact()
		{
			if ( !body.IsValid() ) return Target.WorldTransform.ToWorld( targetLocal );
			var current = body.Transform.ToWorld( boneLocal );
			targetLocal = Target.WorldTransform.ToLocal( current );
			return current;
		}
	}

	static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameObject, List<AttachedShard>> attachedShards = new();

	static bool TryCombine( List<AttachedShard> siblings, ShardDefinition shard, Scene scene, out Vector3 center )
	{
		center = Vector3.Zero;
		int count = siblings.Count( s => !s.Consumed && s.Target.IsValid() && s.Tag == shard.ProjectileTag );
		if ( shard.CombineCount <= 0 || count <= shard.CombineCount ) return false;
		// The child list is newest first in Halo. Its oldest qualifying needles form the center.
		ref uint random = ref BlamRandom.GameplayState( scene );
		for ( int i = siblings.Count - 1; i >= 0; i-- )
		{
			var sibling = siblings[i];
			if ( sibling.Consumed || !sibling.Target.IsValid() || sibling.Tag != shard.ProjectileTag ) continue;
			if ( count-- > shard.CombineCount )
			{
				double elapsed = System.Math.Clamp( Time.NowDouble + sibling.Fuse - sibling.Due, 0, sibling.Fuse );
				sibling.Due = Time.NowDouble + sibling.Fuse - elapsed * (BlamRandom.Next( ref random ) / 65535.0);
				BlamRandom.Next( ref random ); // The second draw scales arming progress.
				continue;
			}
			sibling.Consumed = true;
			sibling.Due = Time.NowDouble + sibling.Fuse;
			center += sibling.GetContact().Position;
		}
		center /= shard.CombineCount;
		return true;
	}

	async System.Threading.Tasks.Task AttachShard( Scene scene, ShardDefinition shard, Pellet pellet, SceneTraceResult hit, GameObject attacker )
	{
		var target = hit.GameObject;
		if ( !target.IsValid() ) { if ( pellet.Body.IsValid() ) pellet.Body.Destroy(); return; }
		var body = hit.Hitbox?.Body;
		var contact = new Transform( hit.HitPosition, Rotation.LookAt( hit.Direction ) );
		var localNormal = contact.Rotation.Inverse * hit.Normal;
		if ( pellet.Body.IsValid() )
		{
			pellet.Body.WorldTransform = contact;
			pellet.Body.SetParent( target, true );
		}
		var clock = TaskSource.Create( scene.EnabledToken );
		var owner = target.GetComponentInParent<PlayerController>()?.GameObject ?? target.GetComponentInParent<Sandbox.Npcs.BaseNpc>()?.GameObject ?? target;
		var siblings = attachedShards.GetValue( owner, static _ => new() );
		int reset = 0;
		for ( int i = siblings.Count - 1; i >= 0 && reset < shard.CombineCount; i-- )
		{
			var sibling = siblings[i];
			if ( sibling.Consumed || sibling.Tag != shard.ProjectileTag ) continue;
			sibling.Due = Time.NowDouble + sibling.Fuse;
			reset++;
		}
		var needle = new AttachedShard( shard.ProjectileTag, pellet.FuseSeconds, target, body, contact );
		siblings.Add( needle );
		while ( target.IsValid() )
		{
			using ( scene.Push() )
			{
				contact = needle.GetContact();
				if ( body.IsValid() && pellet.Body.IsValid() ) pellet.Body.WorldTransform = contact;
				if ( Time.NowDouble >= needle.Due )
				{
					if ( needle.Consumed ) break;
					bool combined = TryCombine( siblings, shard, scene, out var center );
					hit.StartPosition = hit.HitPosition = combined ? center : contact.Position;
					hit.Direction = contact.Rotation.Forward;
					hit.Normal = contact.Rotation * localNormal;
					string path = combined ? shard.CombinedEffect : shard.DetonationEffect;
					if ( path is not null ) PresentDetonation( path, target, hit.HitPosition, hit.Normal, hit.Direction );
					void DamageAttachedTarget( BlastDamage effect )
					{
						ApplyBlastHit( hit, effect, effect.Maximum, attacker );
					}
					DamageAttachedTarget( combined ? shard.CombinedDamage : shard.AttachedDamage );
					var blast = combined ? shard.CombinedBlast : shard.DetonationDamage;
					if ( blast is { OuterRadius: > 0 } ) Detonate( scene, blast, hit.HitPosition, hit.Normal, attacker );
					else if ( blast is not null && target.IsValid() ) DamageAttachedTarget( blast );
					break;
				}
			}
			await clock.Frame();
		}
		siblings.Remove( needle );
		if ( pellet.Body.IsValid() ) pellet.Body.Destroy();
	}

	protected virtual void ProjectileImpacts( Vector3 origin, PelletResult[] results ) =>
		PlayProjectileImpacts( IsValid ? this : null, origin, results );

	protected virtual void OnProjectileHit( in ShotEffect shot, in PelletResult hit ) => OnShootEffects( shot );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void PlayProjectileImpacts( Weapon weapon, Vector3 origin, PelletResult[] results )
	{
		if ( Application.IsDedicatedServer || !weapon.IsValid() ) return;
		foreach ( var hit in results )
		{
			if ( !hit.Hit ) continue;
			var surface = hit.SurfacePath is {} path ? GameResource.Load<Surface>( path ) : null;
			weapon.OnProjectileHit( new ShotEffect( hit.Position, true, hit.Normal, hit.Target, surface, origin, NoEvents: true ), hit );
		}
	}
}
