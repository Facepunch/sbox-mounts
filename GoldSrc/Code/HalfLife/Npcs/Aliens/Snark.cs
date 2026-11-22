namespace GoldSrc.HalfLife;

[Title( "Snark" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeSnark" )]
[NpcDefinition( "monster_snark", "w_squeak", MonsterClass.AlienWeapon, 2, 4, 8, 300,
	Movement = NpcMovement.Bouncing, Damage = 10, IdleSequence = "run", Blood = BloodColor.Yellow )]
public sealed class Snark : Npc, Component.ICollisionListener
{
	internal override float ViewDot => 0;
	internal override float SightRange => 512;

	const float Fuse = 15, PopDamage = 5;
	internal override void DeathSound() => EmitSound( Sounds.Death, WorldPosition, attenuation: 0.5f );

	internal override NpcSounds Sounds { get; } = new( Death: "squeek/sqk_blast1.wav" );

	[Sync( SyncFlags.FromHost )] TimeUntil Detonation { get; set; }
	[Sync( SyncFlags.FromHost )] float BlastDamage { get; set; } = PopDamage;

	internal override MonsterClass Classification => _enemy.IsValid() && _enemy.GetComponent<Snark>() is null && ClassOf( _enemy ) is MonsterClass.Player or MonsterClass.HumanPassive or MonsterClass.HumanMilitary
		? MonsterClass.AlienMilitary : MonsterClass.AlienWeapon;

	Rigidbody _body;
	ClassicBounce _classic;
	Vector3 _launchVelocity;
	[Property] internal GameObject Thrower { get; set; }
	GameObject DamageAttacker => Thrower.IsValid() ? Thrower : GameObject;
	GameObject _enemy;
	Vector3 _heading, _previousPosition;
	TimeUntil _nextHunt, _nextTouch, _nextBite, _nextBounceSound, _nextWaterCheck;
	bool _touched, _warned;
	[Sync( SyncFlags.FromHost )] Vector2 TumbleRate { get; set; }
	[Sync( SyncFlags.FromHost )] TimeSince TumbleStart { get; set; }

	protected override void OnStart()
	{
		if ( Networking.IsHost )
		{
			Detonation = Fuse;
			if ( ClassicBounce.Enabled )
			{
				var halfWidth = Definition.Radius;
				var hull = new BBox( new Vector3( -halfWidth, -halfWidth, 0 ), new Vector3( halfWidth, halfWidth, Definition.Height ) );
				_classic = new( _launchVelocity, default, 0.5f, 0.5f, hull );
			}
		}
		base.OnStart();
	}

	internal override void OnModelLoaded( ModelLoader loader )
	{
		_body = GetComponent<Rigidbody>();
		if ( !Networking.IsHost ) return;
		var hull = GetComponentInChildren<BoxCollider>();
		hull.Elasticity = 0.5f;
		hull.Friction = 0.5f;
		_body.GravityScale = 0.5f;
		_body.Gravity = true;
		_body.MotionEnabled = _classic is null;
		// GoldSrc bounces an axis-aligned box. Only the visual model tumbles.
		_body.Locking = new PhysicsLock { Pitch = true, Roll = true, Yaw = true };
		_previousPosition = WorldPosition;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Renderer.IsValid() )
			Renderer.LocalRotation = new Angles( TumbleRate.x * TumbleStart, 0, TumbleRate.y * TumbleStart ).ToRotation();
		if ( !Networking.IsHost || Health <= 0 ) return;
		if ( Detonation <= 0 ) { Health = 0; return; }
		if ( !_warned && Detonation <= 0.5f )
		{
			_warned = true;
			EmitSound( "squeek/sqk_die1.wav", WorldPosition, pitch: Game.Random.Int( 100, 163 ) * 0.01f );
			EmitStimulus( "combat", radius: 256, lifetime: 0.25f );
		}
	}

	internal override StudioSequence MovementSequence( float speed ) => RestSequence;

	internal void Launch( Vector3 velocity )
	{
		_launchVelocity = velocity;
		FlightVelocity = velocity;
	}

	Vector3 FlightVelocity
	{
		get => _classic?.Velocity ?? _body.Velocity;
		set
		{
			if ( _classic is { } classic ) classic.Velocity = value;
			else if ( GetComponent<Rigidbody>() is { } body ) body.Velocity = value;
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || _classic is not { } classic || !_body.IsValid() || Health <= 0 || PhysicsCarrier.IsValid() ) return;
		var hit = classic.Step( GameObject, _touched ? null : Thrower );
		if ( hit.Hit ) Touch( hit.GameObject, hit.EndPosition, hit.Normal );
	}

	protected override void Think()
	{
		if ( !_body.IsValid() || PhysicsCarrier.IsValid() ) return;
		if ( _nextWaterCheck <= 0 )
		{
			_nextWaterCheck = 0.1f;
			var submerged = WaterBody.Find( Scene, WorldPosition ).IsValid();
			if ( _classic is { } classic ) classic.GravityScale = submerged ? 0 : 0.5f;
			else _body.Gravity = !submerged;
			if ( submerged ) FlightVelocity = FlightVelocity * 0.9f + Vector3.Up * 8;
		}
		if ( !_touched || _nextHunt > 0 ) return;
		_nextHunt = 2;
		if ( !IsLiveTarget( _enemy ) ) _enemy = FindVisibleEnemy();
		var velocity = FlightVelocity;
		if ( _enemy.IsValid() )
		{
			var eye = TargetEyePosition( _enemy );
			var hit = Scene.Trace.Ray( EyePosition, eye ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
			if ( !hit.Hit || hit.GameObject?.Root == _enemy.Root ) _heading = (eye - WorldPosition).Normal;
			velocity = velocity * System.MathF.Min( 50 / (velocity.Length + 10), 1.2f ) + _heading * Definition.Speed;
		}
		if ( _previousPosition.DistanceSquared( WorldPosition ) < 1 )
			velocity = new Vector3( Game.Random.Float( -100, 100 ), Game.Random.Float( -100, 100 ), velocity.z );
		_previousPosition = WorldPosition;
		FlightVelocity = velocity;
		var grounded = System.MathF.Abs( velocity.z ) < 30 && Scene.Trace.Ray( WorldPosition + Vector3.Up, WorldPosition + Vector3.Down * 2 )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run().Hit;
		if ( grounded ) TumbleRate = Vector2.Zero;
		else if ( TumbleRate == Vector2.Zero ) TumbleRate = new Vector2( Game.Random.Float( -100, 100 ), Game.Random.Float( -100, 100 ) );
		TumbleStart = 0;
		if ( velocity.WithZ( 0 ).LengthSquared > 1 ) WorldRotation = Rotation.LookAt( velocity.WithZ( 0 ) );
	}

	void ICollisionListener.OnCollisionStart( Collision collision ) => Touch( collision );
	void ICollisionListener.OnCollisionUpdate( Collision collision ) => Touch( collision );

	void Touch( Collision collision )
	{
		if ( _classic is null ) Touch( collision.Other.GameObject, collision.Contact.Point, collision.Contact.Normal );
	}

	void Touch( GameObject other, Vector3 point, Vector3 normal )
	{
		if ( !Networking.IsHost || Health <= 0 || !_body.IsValid() ) return;
		if ( !_touched && other.IsValid() && Thrower.IsValid() && other.Root == Thrower.Root ) return;

		if ( !_touched ) WorldRotation = Rotation.FromYaw( WorldRotation.Yaw() );
		_touched = true;
		if ( TumbleRate != Vector2.Zero ) TumbleStart = 0;
		if ( _nextTouch > 0 ) return;
		_nextTouch = 0.1f;
		_nextHunt = 0;
		var pitch = (155 - 60 * (float)Detonation / Fuse) * 0.01f;
		if ( _nextBite <= 0 && other?.GetComponentInParent<IDamageable>() is { } victim && victim is not Snark )
		{
			_nextBite = 0.5f;
			BlastDamage += PopDamage;
			var hit = new DamageInfo { Damage = Definition.Damage, Attacker = DamageAttacker, Weapon = GameObject, Origin = WorldPosition, Position = point };
			hit.Tags.Add( "slash" );
			victim.OnDamage( hit );
			EmitSound( "squeek/sqk_deploy1.wav", WorldPosition, pitch: pitch );
		}
		if ( Connection.All.Count > 1 && _nextBounceSound > 0 ) return;
		_nextBounceSound = 0.5f;
		var grounded = _classic?.Grounded ?? (System.MathF.Abs( normal.z ) > 0.7f && System.MathF.Abs( FlightVelocity.z ) < 30);
		if ( !grounded ) EmitSound( "squeek/sqk_hunt", WorldPosition, pitch: pitch );
		EmitStimulus( "combat", radius: grounded ? 100 : 256, lifetime: grounded ? 0.1f : 0.25f );
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		BloodEffects.Spray( Definition.Blood, WorldPosition, 80 );
		EmitSound( "common/bodysplat.wav", WorldPosition, 0.75f, 2 );
		Blast.DamageInRadius( GameObject, DamageAttacker, WorldPosition, BlastDamage, BlastDamage * 2.5f, "blast" );
		base.OnDeath( death );
	}

	protected override GameObject CreateCorpse() => null;
}
