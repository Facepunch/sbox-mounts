using System;
using Sandbox.Npcs;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string SwordSlashRight = "sword_slash_right";
	RabbitAttack strike;
	[Property, Range( 0.1f, 2f )] public float AttackSpeed { get; set; } = 1f;
	[Property] public float AttackDamage { get; set; } = 1f;
	[Property] public float AttackKnockback { get; set; } = 1f;
	[Property] public bool LeftHanded { get; set; }
	[Sync] bool MirroredStance { get; set; }
	[Sync] bool MirroredAction { get; set; }
	bool flippedAttack;
	[Sync] float AttackHeight { get; set; }
	[Sync( SyncFlags.Interpolate )] float AttackPhase { get; set; }
	bool impactPassed;
	bool preparePassed;
	RabbitGuard blockingOpponent;
	float predictability;
	readonly AttackHistory attackHistory = new();
	TimeUntil repath;
	float cancelDelay;
	[Property, Sync] public LevelItem LeftHandItem { get; set; }
	[Property, Sync] public LevelItem RightHandItem { get; set; }
	LevelItem PrimaryItem => LeftHanded ? LeftHandItem : RightHandItem;
	internal bool EquipsForAnimation( LevelItem item ) => PrimaryItem == item && equippedForAnimation == item;
	void DropHandItem()
	{
		if ( LeftHandItem.IsValid() )
		{
			seekTime = 0f;
			LeftHandItem.Drop();
			LeftHandItem = null;
		}
		else if ( RightHandItem.IsValid() )
		{
			seekTime = 0f;
			RightHandItem.Drop();
			RightHandItem = null;
		}
	}

	float RangeExtension => PrimaryItem.IsValid() ? PrimaryItem.RangeExtension : 0f;
	float AttackRange => (1.5f + RangeExtension) * (PrimaryItem.IsValid() ? PrimaryItem.RangeMultiplier : 1f) * 40f * boundAnimations.CharacterScale;
	float CloseAttackRange => (1f + RangeExtension * 0.5f) * 40f * boundAnimations.CharacterScale;
	float ContactRange => (1.6f + RangeExtension) * 40f * boundAnimations.CharacterScale;
	bool AnimationCanCancel => Renderer.Animation.Debug.TryGetFloatChannel( "status", "cancel", out var value ) && value >= 1f - 0.000001f;

	protected override void OnStart()
	{
		base.OnStart();
		initialKnockoutShield = KnockoutShield;
		if ( Networking.IsHost ) MirroredStance = LeftHanded;
	}

	bool CanEndAttack( bool canCancel )
	{
		if ( !canCancel )
		{
			cancelDelay = 0.01f;
			return false;
		}
		if ( cancelDelay <= 0f ) return true;
		cancelDelay -= Time.Delta;
		return false;
	}

	void UpdateCombat()
	{
		if ( strike is { AsLayer: false } )
		{
			float previousHeight = AttackHeight;
			if ( enemy.IsValid() )
			{
				AttackHeight = GetAttackHeight( enemy );
				TurnDuringStrike( TargetPosition( enemy ) - WorldPosition );
			}
			bool blocked = Stance == strike.Blocked;
			var blockClip = blocked ? boundAnimations.Clips[Stance].ForSide( MirroredAction ) : null;
			var blend = blocked ? new RabbitAttack.Blend( blockClip, blockClip, 0f ) : strike.Sample( AttackHeight, MirroredAction );
			var clip = blend.Events;
			var previous = !blocked && previousHeight != AttackHeight ? strike.Sample( previousHeight, MirroredAction ) : blend;
			var from = previous.Motion( AttackPhase );
			AttackPhase = Math.Min( 1f, AttackPhase + Time.Delta * AttackSpeed * blend.Frequency );
			ApplyRootMotion( from, blend.Motion( AttackPhase ) );
			if ( !preparePassed && !blocked && clip.Prepare is { } prepare && AttackPhase >= prepare.Time / clip.Duration )
			{
				preparePassed = true;
				if ( CanAttack( enemy ) && CharacterCenter( GameObject ).Distance( CharacterCenter( enemy ) ) < ContactRange && HasSight( enemy ) &&
					enemy.GetComponent<RabbitGuard>() is { } defender && defender.TryActiveBlock( this, strike ) )
				{
					dontAttackBefore = Random.Shared.Float( 0f, 1.4f );
					blockingOpponent = defender;
					Stance = strike.Blocked;
					clip = boundAnimations.Clips[Stance].ForSide( MirroredAction );
					// SwapAnimation preserves elapsed time. A synced group uses its first clip as the clock.
					AttackPhase = Math.Min( 1f, AttackPhase * strike.Poses[0].Clip.Duration / clip.Duration );
					if ( strike.SwapStance != strike.SwapBlocked ) MirroredStance = !MirroredStance;
					blocked = true;
				}
			}
			if ( !impactPassed && (blocked ? clip.Blocked : clip.Impact) is { } impact && AttackPhase >= impact.Time / clip.Duration )
			{
				impactPassed = true;
				if ( blocked )
				{
					var pos = Renderer.TryGetBoneTransform( $"bone_{impact.Bone}", out var bone ) ? bone.Position : EyePosition;
					if ( blockingOpponent.IsValid() && !blockingOpponent.Downed )
					{
						stunnedBy = blockingOpponent;
						blockStun = 0.5f;
						var species = blockingOpponent.boundAnimations.Species;
						bool smaller = boundAnimations.Species == "dog" && species is not ("dog" or "wolf") || species == "rat" && boundAnimations.Species != "rat";
						blockingOpponent.ReceiveBlockImpact( this, strike, smaller ? 4f : 1f );
					}
					if ( !strike.FleshUnblockable ) PlayBluntImpact( "hit_block", pos );
					else if ( PrimaryItem.IsValid() && blockingOpponent.IsValid() && blockingOpponent.PrimaryItem.IsValid() &&
						PrimaryItem.TryContact( blockingOpponent.PrimaryItem, out var contact, out var a, out var b ) )
						PlayWeaponClash( contact, a, b );
					blockingOpponent = null;
				}
				else TryHit( clip, impact.Bone );
			}
			if ( AttackPhase < 1f && !CanEndAttack( AnimationCanCancel ) ) return;
			strike = null;
			EndGroupAttack();
			Stance = Threat;
			return;
		}

		// UpdateGroundAttackControls only runs on the ground. The rabbit's airborne
		// leg-cannon controls are player-only; an AI finishes its crossing first.
		if ( !Animation.IsGrounded )
		{
			Stance = Locomotion;
			return;
		}

		if ( SeekWeapon() ) return;
		if ( enemy is not null && ClosestKnownThreat() is { } closest && closest != enemy )
		{
			Remember( closest, engage: false );
			visible = CanSeeCharacter( closest, closest.GetComponent<RabbitGuard>() );
		}
		if ( !CanAttack( enemy ) || (!visible && sinceEnemySeen > 12f) )
		{
			if ( enemy is not null && (!enemy.IsValid() || !IsLiveTarget( enemy ) || enemy.GetComponent<RabbitGuard>() is { Unconscious: true }) )
			{
				FinishCombat( enemy.IsValid() ? TargetPosition( enemy ) : lastSeen );
			}
			enemy = null;
			if ( FollowHeardVoice() ) return;
			if ( InvestigateCasualty() ) return;
			if ( !investigatePoint.HasValue && SearchArea() ) return;
			float arrival = urgentInvestigation ? MathF.Sqrt( 2f ) * 40f : 40f;
			if ( investigatePoint.HasValue && startled > 0 )
			{
				Movement.Stop();
				Stance = Watch;
				return;
			}
			if ( investigatePoint is { } point && WorldPosition.DistanceSquared( point ) >= arrival * arrival )
			{
				if ( Movement.MoveTarget != point ) Movement.MoveTo( point, arrival, (urgentInvestigation ? 0.7f : 0.2f) * GroundRunSpeed );
				Stance = Locomotion;
				return;
			}
			investigatePoint = null;
			urgentInvestigation = false;
			if ( FollowPatrol() ) return;
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
		if ( Animation.IsGrounded && TryCounterThrow() ) return;
		var destination = visible ? TargetPosition( enemy ) : lastSeen;
		var delta = visible ? CharacterCenter( enemy ) - CharacterCenter( GameObject ) : destination - WorldPosition;
		float distance = delta.Length;
		bool investigating = !visible && sinceEnemySeen > 2f;
		if ( investigating && distance < 40f )
		{
			enemy = null;
			BeginAreaSearch();
			SearchArea();
			return;
		}

		if ( visible ) UpdateTactic( distance );
		if ( Stance == Roll ) return;
		bool attacking = airResponse == AirResponse.Counter || (airResponse != AirResponse.Evade && tactic is CombatTactic.Rush or CombatTactic.Wait);
		bool canStartStrike = Animation.IsGrounded && !(boundAnimations.Species == "wolf" && blockStun > 0.5f);
		if ( canStartStrike && visible && attacking && strike is null && knifeCooldown <= 0 && distance < AttackRange && HasSight( enemy ) )
		{
			bool close = distance < CloseAttackRange;
			int attackClass = airResponse == AirResponse.Counter ? 1 : Random.Shared.Next( 3 );
			bool mirrored = Random.Shared.Next( 2 ) == 0;
			var label = boundAnimations.WeaponLabel;
			if ( label == "sword" && attackClass == 0 ) attackClass = 1;
			var choice = attackClass switch
			{
				1 or 2 when (label is "sword" or "rapier") && mirrored == LeftHanded => SwordSlashRight,
				1 when !PrimaryItem.IsValid() && enemy.GetComponent<RabbitGuard>() is { Downed: true } => SoccerKick,
				1 when PrimaryItem.IsValid() || !IsCrouching( enemy ) => close ? Jab : Roundhouse,
				2 => Sweep,
				_ => close ? Knee : Kick
			};
			strike = boundAnimations.Attacks[choice];
			if ( strike.AsLayer )
			{
				BeginKnifeAttack();
				return;
			}
			Movement.Stop();
			Stance = choice;
			if ( strike.Direction != "front" )
				MirroredStance = mirrored != (strike.Direction == "left");
			MirroredAction = MirroredStance;
			flippedAttack = strike.Direction == "front" ? !MirroredStance : MirroredStance;
			if ( strike.SwapStance ) MirroredStance = !MirroredStance;
			duckAmount = strike.Height == "low" ? 1f : 0f;
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
			if ( !investigating )
			{
				var intent = CombatApproach( ChaseDestination( destination ), out var point );
				if ( !intent.IsNearlyZero() )
					Movement.MoveTo( point, 0f, intent.Length * GroundRunSpeed,
						visible && Animation.MoveVelocity.WithZ( 0 ).Length < 5f * 40f ? enemy : null );
				else Movement.Stop();
			}
			else Movement.MoveTo( destination, 0.45f * 40, GroundRunSpeed );
		}
		Stance = !Movement.MoveTarget.HasValue || visible && Animation.MoveVelocity.WithZ( 0 ).Length < 5f * 40f ? Threat : Locomotion;
	}

	// GetNavMeshMovement slows only the component along the line to the target.
	Vector3 CombatApproach( Vector3 target, out Vector3 point )
	{
		var toward = (target - WorldPosition).WithZ( 0f ).Normal;
		var offset = target - toward * preferredRange + Vector3.Up.Cross( toward ) * strafeOffset;
		point = preferredRange > 0f ? Movement.ProjectPoint( offset ) ?? WorldPosition : target;
		var delta = (point - WorldPosition).WithZ( 0f );
		var direct = toward * delta.Dot( toward );
		float slowRadius = Math.Max( 0.2f * 40f, 40f - preferredRange );
		var intent = (delta - direct + direct.Normal * Math.Max( 0f, direct.Length - slowRadius )) / 40f;
		return intent.LengthSquared > 1f ? intent.Normal : intent;
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

	static bool TargetIsAirborne( GameObject target ) => target.GetComponent<BaseNpc>() is { IsAirborne: true }
		|| target.GetComponent<PlayerController>() is { IsOnGround: false };

	static bool AvoidsStrike( GameObject target, string height ) => target.GetComponent<RabbitGuard>() is { IsDodging: true } || height switch
	{
		"high" => IsCrouching( target ),
		"low" => TargetIsAirborne( target ),
		_ => false
	};

	void TryHit( RabbitClip clip, int bone )
	{
		var position = Renderer.TryGetBoneTransform( $"bone_{bone}", out var contact ) ? contact.Position : EyePosition;
		// HandleAnimationEvent allows an extra 0.1 m at contact for an unarmed strike.
		var origin = CharacterCenter( GameObject );
		var offset = enemy.IsValid() ? CharacterCenter( enemy ) - origin : Vector3.Zero;
		bool reached = CanAttack( enemy ) && offset.LengthSquared < ContactRange * ContactRange && HasSight( enemy );
		var rabbit = reached ? enemy.GetComponent<RabbitGuard>() : null;
		if ( rabbit is not null )
		{
			rabbit.NoticeAttacker( GameObject );
			if ( rabbit.Downed || rabbit.RollProgress < 1f || IsGetUp( rabbit.Stance ) ) rabbit.hitsOnGround++;
		}
		if ( !reached || AvoidsStrike( enemy, strike.Height ) )
		{
			PlayCombatSound( "Data/Sounds/whoosh/hit_whoosh.xml", position );
			return;
		}
		bool parried = rabbit is not null && rabbit.TryPassiveBlock( this, strike );
		var direction = offset.Normal;
		if ( strike.SharpMax > 0f )
		{
			ApplySharpHit( clip, position, direction, rabbit, parried );
			return;
		}
		if ( parried )
		{
			ReportSound( enemy, position, HeardSound.Combat, 10f );
			PlayBluntImpact( "hit_normal", position );
			return;
		}
		var npc = enemy.GetComponent<BaseNpc>();
		ApplyStrikeImpulse( clip, position, direction );
		if ( enemy.IsValid() ) ReportSound( enemy, position, HeardSound.Combat, 10f );
		var group = "hit_normal";
		if ( npc is not null && (!npc.IsValid() || npc.Health < 1f) ) group = "hit_hard";
		else if ( npc is RabbitGuard { Downed: true } ) group = "hit_medium";
		PlayBluntImpact( group, position );
		if ( rabbit.IsValid() && strike is not null ) rabbit.TryDisarm( strike.WorldImpact( direction, flippedAttack ) );
	}

	void ApplyStrikeImpulse( RabbitClip clip, Vector3 position, Vector3 direction )
	{
		var victim = enemy.GetComponentInParent<IDamageable>();
		if ( victim is null ) return;
		var origin = CharacterCenter( GameObject );
		float force = strike.Force * AttackKnockback;
		if ( enemy.GetComponent<RabbitGuard>() is { MaxHealth: > 0 } defender )
			force *= 1f - Math.Clamp( defender.TemporaryHealth / defender.MaxHealth, 0f, 1f ) * 0.5f;
		var damage = new StrikeDamage
		{
			// Overgrowth applies the authored force for one 120 Hz physics step (40 units/metre).
			Impulse = strike.WorldImpact( direction, flippedAttack ) * (force * 40f / 120f),
			Damage = Random.Shared.Float( clip.DamageMin, clip.DamageMax ) * 100f * AttackDamage,
			Origin = origin,
			Position = position,
			Attacker = GameObject,
			Weapon = PrimaryItem.IsValid() ? PrimaryItem.GameObject : GameObject
		};
		damage.Tags.Add( "melee" );
		damage.Tags.Add( "nevergib" );
		victim.OnDamage( damage );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayBluntImpact( string group, Vector3 position )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		mount.PlayGroup( $"Data/Sounds/hit/{group}.xml", position );
		mount.EmitImpactDust( "Data/Particles/impactfast.xml", position );
		mount.EmitImpactDust( "Data/Particles/impactslow.xml", position );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayWeaponClash( Vector3 point, string a, string b )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		var group = (a, b) switch
		{
			("metal", "metal") => "weapon_metal_hit_metal_strong",
			("wood", "wood") => "weapon_staff_hit_staff_strong",
			_ => "weapon_staff_hit_metal_strong"
		};
		mount.PlayGroup( $"Data/Sounds/weapon_foley/impact/{group}.xml", point );
		if ( a == "metal" && b == "metal" )
		{
			mount.MetalSparks( point );
			return;
		}
		mount.EmitClashParticles( "woodspeck", point, Random.Shared.Next( a == "wood" && b == "wood" ? 5 : 10 ) );
		mount.EmitImpactDust( "Data/Particles/impactfast.xml", point );
		mount.EmitImpactDust( "Data/Particles/impactslow.xml", point );
	}

	void Speak( string cue )
	{
		if ( cue is "attack" or "engage" ) ReportSound( GameObject, WorldPosition, HeardSound.Voice, 10f );
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
	void PlayCombatSound( string group, Vector3 position, float gain = 1f )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount )
			mount.PlayGroup( group, position, gain );
	}
}
