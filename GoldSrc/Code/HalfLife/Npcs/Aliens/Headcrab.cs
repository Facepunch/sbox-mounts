namespace GoldSrc.HalfLife;

[Title( "Headcrab" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeHeadcrab" )]
[NpcDefinition( "monster_headcrab", "headcrab", MonsterClass.AlienPrey, 10, 12, 24, 90, Attack = NpcAttack.Leap, Damage = 10, Range = 256, Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public class Headcrab : VoicedNpc
{
	internal override NpcSounds Sounds { get; } = new( "headcrab/hc_idle", "headcrab/hc_alert1.wav", "headcrab/hc_pain", "headcrab/hc_die",
				"headcrab/hc_attack", "headcrab/hc_headbite" );

	internal override Vector3 HullCenter => WorldPosition + Vector3.Up * 6;
	internal override Vector3 BodyTarget => HullCenter;
	internal virtual float FacingThreshold => 0.65f;
	internal virtual bool PerchedOnActor => false;
	int _pounceAction = -1;
	GameObject _prey, _turnTarget;
	internal virtual string LeapCall( int variation ) => $"headcrab/hc_attack{variation}.wav";
	internal virtual float LeapTurnSpeed => 120;
	internal virtual float LeapRecovery => Game.Random.Float( 0.1f, 0.5f );
	TimeUntil _leapRecovery, _nextCooCheck;
	bool _biting;

	internal override void OnModelLoaded( ModelLoader loader )
	{
		EyeHeight = loader.EyePosition.z;
		if ( Networking.IsHost ) GameObject.GetOrAddComponent<HeadcrabBite>().Crab = this;
	}

	internal override void IdleMovement()
	{
		if ( Movement?.MoveTarget.HasValue == true ) Movement.Stop();
	}

	void Call( string sample ) => EmitSound( sample, WorldPosition, VoiceVolume, VoicePitch, this, GoldSrcSoundEvents.Idle );

	internal override void IdleSound() => Call( $"{Sounds.Idle}{Game.Random.Int( 1, 3 )}.wav" );
	internal override void OnEnemySpotted( GameObject target ) => Call( Sounds.Alert );
	internal override void PainSound() => Call( Sounds.Pain );
	internal override void DeathSound() => Call( Sounds.Death );

	protected override void Think()
	{
		if ( _turnTarget.IsValid() )
		{
			if ( Action is null && IsOnGround && IsLiveTarget( _turnTarget ) && CanHit( _turnTarget, AimAt( _turnTarget ) ) )
			{
				FaceForLeap();
				return;
			}
			_turnTarget = null;
		}
		base.Think();
		if ( !InCombat || _nextCooCheck > 0 ) return;
		_nextCooCheck = 0.1f;
		if ( Game.Random.Float( 0, 5 ) < 0.1f ) IdleSound();
	}

	internal override bool InAttackRange( GameObject target, Vector3 aim ) =>
		PerchedOnActor || WorldPosition.DistanceSquared( target.WorldPosition ) <= AttackRange * AttackRange;

	internal override void BeginAttack( GameObject target )
	{
		var towards = (target.WorldPosition - WorldPosition).WithZ( 0 ).Normal;
		if ( !PerchedOnActor && WorldRotation.Forward.Dot( towards ) < FacingThreshold ) return;
		PrepareLeap( target );
	}

	internal void PrepareLeap( GameObject target )
	{
		if ( _leapRecovery > 0 || !IsOnGround ) return;
		Movement?.Stop();
		_turnTarget = target;
		FaceForLeap();
	}

	void FaceForLeap()
	{
		var direction = (_turnTarget.WorldPosition - WorldPosition).WithZ( 0 );
		if ( !direction.IsNearlyZero() )
		{
			var facing = Rotation.LookAt( direction.Normal );
			var angle = WorldRotation.Distance( facing );
			var step = LeapTurnSpeed * Time.Delta;
			if ( angle > step )
			{
				WorldRotation = Rotation.Slerp( WorldRotation, facing, step / angle );
				return;
			}
			WorldRotation = facing;
		}

		_prey = _turnTarget;
		_turnTarget = null;
		if ( WeightedActivity( Ranged ) is not { } jump ) return;
		EmitSound( LeapCall( 1 ), WorldPosition, VoiceVolume, VoicePitch, attenuation: GoldSrcSoundEvents.Idle );
		PlayAction( jump );
		_pounceAction = ActionSerial;
	}

	internal override void HandleActionEvent( int id )
	{
		if ( id != 2 ) return;
		WorldPosition += Vector3.Up;
		var prey = _pounceAction == ActionSerial ? _prey : FindVisibleEnemy();
		var velocity = WorldRotation.Forward.WithZ( WorldRotation.Up.z ) * 350;
		if ( prey.IsValid() )
		{
			var gravity = System.MathF.Max( -Scene.PhysicsWorld.Gravity.z, 1 );
			var offset = TargetEyePosition( prey ) - WorldPosition;
			var riseSpeed = System.MathF.Sqrt( 2 * gravity * System.MathF.Max( offset.z, 16 ) );
			velocity = (offset * (gravity / riseSpeed)).WithZ( riseSpeed );
			if ( velocity.Length > 650 ) velocity = velocity.Normal * 650;
		}
		_biting = _pounceAction == ActionSerial;

		var call = Game.Random.Int( 0, 2 );
		if ( call != 0 ) Call( LeapCall( call + 1 ) );
		Launch( velocity );
	}

	internal override void FinishAction( string name )
	{
		if ( ActionSerial != _pounceAction ) return;
		_biting = false;
		_leapRecovery = LeapRecovery;
	}

	internal void Touch( Collision collision )
	{
		if ( !Networking.IsHost || !_biting ) return;
		if ( _pounceAction != ActionSerial || PhysicsCarrier.IsValid() || IsOnGround || Find( Action ) is not { } jump || ActionTime >= jump.Duration )
		{
			_biting = false;
			return;
		}
		var other = collision.Other.GameObject;
		if ( other?.GetComponentInParent<IDamageable>() is not Component victim || ClassOf( victim.GameObject ) == Classification ) return;
		_biting = false;
		var damage = new DamageInfo { Attacker = GameObject, Weapon = GameObject, Origin = WorldPosition, Position = collision.Contact.Point, Damage = Definition.Damage };
		damage.Tags.Add( "slash" );
		EmitSound( Sounds.Hit, WorldPosition, VoiceVolume, VoicePitch, attenuation: GoldSrcSoundEvents.Idle );
		((IDamageable)victim).OnDamage( damage );
	}

	internal override void ModifyDamage( Component.IDamageable.DamageEvent damage )
	{
		base.ModifyDamage( damage );
		if ( damage.DamageInfo.Tags.Has( "acid" ) )
			damage.Cancelled = true;
	}
}
