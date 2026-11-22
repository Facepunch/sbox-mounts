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
	TimeUntil recovery;

	void UpdateCombat()
	{
		if ( strike is not null )
		{
			if ( enemy.IsValid() ) AttackHeight = GetAttackHeight( enemy );
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
				if ( CanAttack( enemy ) && WorldPosition.Distance( enemy.WorldPosition ) < 64f && HasSight( enemy ) &&
					enemy.GetComponent<RabbitGuard>() is { } defender && defender.TryActiveBlock( this, strike, predictability ) )
				{
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
			if ( AttackPhase < 1f ) return;
			strike = null;
			Stance = Threat;
			// Leave the attack state before an identical move can play again.
			recovery = 0.2f;
			return;
		}

		if ( !CanAttack( enemy ) || (!visible && sinceEnemySeen > 12f) )
		{
			if ( Movement.MoveTarget.HasValue ) Movement.Stop();
			enemy = null;
			Stance = AlertStage > 0 ? Watch : Idle;
			return;
		}

		if ( startled > 0 )
		{
			Movement.Stop();
			Stance = Watch;
			return;
		}
		var destination = visible ? enemy.WorldPosition : lastSeen;
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
			if ( !delta.WithZ( 0 ).IsNearlyZero() ) WorldRotation = Rotation.LookAt( delta.WithZ( 0 ) );
			Stance = Threat;
			return;
		}

		if ( visible && attacking && distance < 1.5f * 40 && HasSight( enemy ) )
		{
			bool moving = Movement.IsMoving && enemy.GetComponent<PlayerController>() is not { IsDucking: true };
			Movement.Stop();
			var facing = delta.WithZ( 0 );
			if ( !facing.IsNearlyZero() ) WorldRotation = Rotation.LookAt( facing );
			if ( recovery > 0 ) { Stance = Threat; return; }

			bool close = distance < 40;
			// aschar.as GetAttackPath: moving_close / moving, or stationary_close / stationary.
			Stance = moving ? (close ? Jab : Roundhouse) : (close ? Knee : Kick);
			strike = boundAnimations.Attacks[Stance];
			Speak( "attack" );
			AttackPhase = 0;
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

	void TryHit( RabbitClip clip, int bone )
	{
		var position = Renderer.TryGetBoneTransform( $"bone_{bone}", out var contact ) ? contact.Position : EyePosition;
		// HandleAnimationEvent allows an extra 0.1 m at contact for an unarmed strike.
		if ( !CanAttack( enemy ) || WorldPosition.Distance( enemy.WorldPosition ) >= 1.6f * 40 || !HasSight( enemy ) )
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
		var damage = new DamageInfo
		{
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
