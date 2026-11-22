using System;
using Sandbox.Npcs;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	RabbitAttack strike;
	[Sync] float AttackHeight { get; set; }
	[Sync( SyncFlags.Interpolate )] float AttackPhase { get; set; }
	bool impactPassed;
	bool preparePassed;
	float predictability;
	readonly AttackHistory attackHistory = new();
	TimeUntil repath;
	float cancelDelay;
	bool AnimationCanCancel => Renderer.Animation.Debug.TryGetFloatChannel( "status", "cancel", out var value ) && value >= 1f - 0.000001f;

	void UpdateCombat()
	{
		if ( strike is not null )
		{
			if ( enemy.IsValid() )
			{
				AttackHeight = GetAttackHeight( enemy );
				TurnDuringStrike( TargetPosition( enemy ) - WorldPosition );
			}
			var (frequency, clip) = strike.Sample( AttackHeight );
			bool blocked = Stance == strike.Blocked;
			if ( blocked )
			{
				clip = boundAnimations.Clips[Stance];
				frequency = 1f / clip.Duration;
			}
			AttackPhase = Math.Min( 1f, AttackPhase + Time.Delta * frequency );
			if ( !preparePassed && !blocked && clip.Prepare is { } prepare && AttackPhase >= prepare.Time / clip.Duration )
			{
				preparePassed = true;
				if ( CanAttack( enemy ) && WorldPosition.Distance( TargetPosition( enemy ) ) < 64f && HasSight( enemy ) &&
					enemy.GetComponent<RabbitGuard>() is { } defender && defender.TryActiveBlock( this, strike, predictability ) )
				{
					dontAttackBefore = Random.Shared.Float( 0f, 1.4f );
					Stance = strike.Blocked;
					clip = boundAnimations.Clips[Stance];
					blocked = true;
				}
			}
			if ( !impactPassed && (blocked ? clip.Blocked : clip.Impact) is { } impact && AttackPhase >= impact.Time / clip.Duration )
			{
				impactPassed = true;
				if ( blocked )
				{
					var pos = Renderer.TryGetBoneTransform( $"bone_{impact.Bone}", out var bone ) ? bone.Position : EyePosition;
					PlayCombatSound( "Data/Sounds/hit/hit_block.xml", pos );
				}
				else TryHit( clip, impact.Bone );
			}
			if ( AttackPhase < 1f )
			{
				if ( !AnimationCanCancel )
				{
					cancelDelay = 0.01f;
					return;
				}
				if ( cancelDelay > 0f )
				{
					cancelDelay -= Time.Delta;
					return;
				}
			}
			strike = null;
			Stance = Threat;
			return;
		}

		if ( !CanAttack( enemy ) || (!visible && sinceEnemySeen > 12f) )
		{
			enemy = null;
			if ( investigatePoint is { } point && WorldPosition.DistanceSquared( point ) >= 40f * 40f )
			{
				if ( Movement.MoveTarget != point ) Movement.MoveTo( point, 40f, 0.2f * 8f * 40f );
				Stance = Locomotion;
				return;
			}
			investigatePoint = null;
			if ( Movement.MoveTarget.HasValue ) Movement.Stop();
			Stance = AlertStage > 0 ? Watch : Idle;
			return;
		}

		if ( startled > 0 )
		{
			Movement.Stop();
			Stance = Watch;
			return;
		}
		var destination = visible ? TargetPosition( enemy ) : lastSeen;
		var delta = destination - WorldPosition;
		float distance = delta.Length;
		if ( !visible && distance < 40 )
		{
			if ( Movement.MoveTarget.HasValue ) Movement.Stop();
			Stance = sinceEnemySeen < 2f ? Threat : Watch;
			return;
		}

		if ( visible ) UpdateTactic( distance );
		bool attacking = tactic is CombatTactic.Rush or CombatTactic.Wait;
		if ( visible && distance <= preferredRange && (!attacking || distance >= 1.5f * 40) )
		{
			Movement.Stop();
			Stance = Threat;
			return;
		}

		if ( visible && attacking && distance < 1.5f * 40 && HasSight( enemy ) )
		{
			Movement.Stop();

			bool close = distance < 40;
			// enemycontrol.as ChooseAttack, followed by aschar.as GetAttackPath for an unarmed fighter.
			Stance = Random.Shared.Next( 3 ) switch
			{
				1 when enemy.GetComponent<RabbitGuard>() is { Downed: true } => SoccerKick,
				1 when !IsCrouching( enemy ) => close ? Jab : Roundhouse,
				2 => Sweep,
				_ => close ? Knee : Kick
			};
			strike = boundAnimations.Attacks[Stance];
			if ( strike.Height == "low" ) duckAmount = 1f;
			punishGroundedTarget = null;
			Speak( "attack" );
			AttackPhase = 0;
			cancelDelay = 0.01f;
			AttackHeight = GetAttackHeight( enemy );
			impactPassed = false;
			preparePassed = false;
			predictability = attackHistory.Record( strike );
			return;
		}

		if ( repath <= 0 )
		{
			repath = 0.2f;
			Movement.MoveTo( destination, 0.45f * 40, visible || sinceEnemySeen < 2f ? 8f * 40 : 2.2f * 40 );
		}
		Stance = Locomotion;
	}

	// aschar.as UpdateAttacking and interpdirection.as, for an unarmed non-throw attack.
	void TurnDuringStrike( Vector3 direction )
	{
		var target = direction.WithZ( 0 ).Normal;
		if ( target.IsNearlyZero() ) return;

		var facing = WorldRotation.Forward;
		if ( facing.Dot( target ) < -0.8f )
		{
			var perpendicular = Vector3.Up.Cross( facing );
			target = perpendicular.Dot( target ) < 0f ? -perpendicular : perpendicular;
		}

		float inertia = MathF.Pow( 0.9f, Time.Delta * 120f );
		WorldRotation = Rotation.LookAt( facing * inertia + target * (1f - inertia) );
	}

	float GetAttackHeight( GameObject target )
	{
		// Overgrowth targets the mean position of the rig's physics bones.
		var model = target.GetComponent<BaseNpc>()?.Renderer;
		float sum = 0;
		int count = 0;
		if ( model.IsValid() && model.Model?.Physics is { } physics )
		{
			foreach ( var part in physics.Parts )
			{
				if ( !model.TryGetBoneTransform( part.BoneName, out var bone ) ) continue;
				sum += bone.Position.z;
				count++;
			}
		}

		var targetZ = count > 0 ? sum / count : target.GetComponent<PlayerController>() is { } player
			? player.WorldPosition.z + player.CurrentHeight * 0.5f : target.GetBounds().Center.z;
		// The movement object's origin is one leg-sphere radius above our ground origin.
		return (targetZ - WorldPosition.z) / 40f - (0.45f + 0.45f);
	}

	static bool IsCrouching( GameObject target ) => target.GetComponent<RabbitGuard>() is { duckAmount: >= 0.5f }
		|| target.GetComponent<PlayerController>() is { IsDucking: true };

	static bool AvoidsStrike( GameObject target, string height ) => height switch
	{
		"high" => IsCrouching( target ),
		"low" => target.GetComponent<BaseNpc>() is { IsAirborne: true } || target.GetComponent<PlayerController>() is { IsOnGround: false },
		_ => false
	};

	void TryHit( RabbitClip clip, int bone )
	{
		var position = Renderer.TryGetBoneTransform( $"bone_{bone}", out var contact ) ? contact.Position : EyePosition;
		// HandleAnimationEvent allows an extra 0.1 m at contact for an unarmed strike.
		if ( !CanAttack( enemy ) || WorldPosition.Distance( TargetPosition( enemy ) ) >= 1.6f * 40 || !HasSight( enemy ) || AvoidsStrike( enemy, strike.Height ) )
		{
			PlayCombatSound( "Data/Sounds/whoosh/hit_whoosh.xml", position );
			return;
		}
		if ( enemy.GetComponent<RabbitGuard>() is { } rabbit && rabbit.TryPassiveBlock( this, strike ) )
		{
			PlayCombatSound( "Data/Sounds/hit/hit_normal.xml", position );
			return;
		}
		var victim = enemy.GetComponentInParent<IDamageable>();
		if ( victim is null ) return;
		var direction = (TargetPosition( enemy ) - WorldPosition).Normal;
		float force = strike.Force;
		if ( enemy.GetComponent<RabbitGuard>() is { MaxHealth: > 0 } defender )
			force *= 1f - Math.Clamp( defender.Health / defender.MaxHealth, 0f, 1f ) * 0.5f;
		var damage = new StrikeDamage
		{
			// Overgrowth applies the authored force for one 120 Hz physics step (40 units/metre).
			Impulse = strike.WorldImpact( direction ) * (force * 40f / 120f),
			Damage = Random.Shared.Float( clip.DamageMin, clip.DamageMax ) * 100,
			Origin = WorldPosition,
			Position = position,
			Attacker = GameObject,
			Weapon = GameObject
		};
		damage.Tags.Add( "melee" );
		damage.Tags.Add( "nevergib" );
		victim.OnDamage( damage );
		bool killed = enemy.IsValid() && enemy.GetComponent<Sandbox.Npcs.BaseNpc>() is { Health: < 1 };
		PlayCombatSound( killed ? "Data/Sounds/hit/hit_hard.xml" : "Data/Sounds/hit/hit_normal.xml", damage.Position );
	}

	void Speak( string cue )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ||
			mount.GetByPath( Renderer.Model.ResourcePath ) is not CharacterLoader character ) return;
		var (group, pitch) = character.GetVoice( cue );
		if ( group is null ) return;
		var (file, volume) = mount.PickSound( group );
		if ( file is not null ) PlayVoice( file.ResourcePath, volume, pitch );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayVoice( string path, float volume, float pitch )
	{
		if ( Renderer.IsValid() ) Renderer.SpeakSound( SoundFile.Load( path ), volume, pitch );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayCombatSound( string group, Vector3 position )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount )
			mount.PlayGroup( group, position );
	}
}
