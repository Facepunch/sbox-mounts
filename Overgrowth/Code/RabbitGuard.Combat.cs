using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	RabbitClip strike;
	TimeSince attackTime;
	bool impactPassed;
	TimeUntil repath;
	TimeUntil recovery;

	void UpdateCombat()
	{
		if ( strike is not null )
		{
			if ( !impactPassed && strike.Impact is { } impact && attackTime >= impact.Time )
			{
				impactPassed = true;
				TryHit( impact.Bone );
			}
			if ( attackTime < strike.Duration ) return;
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

		var destination = visible ? enemy.WorldPosition : lastSeen;
		var delta = destination - WorldPosition;
		float distance = delta.Length;
		if ( !visible && distance < 40 )
		{
			if ( Movement.MoveTarget.HasValue ) Movement.Stop();
			Stance = sinceEnemySeen < 2f ? Threat : Watch;
			return;
		}

		if ( visible && distance < 1.5f * 40 && HasSight( enemy ) )
		{
			bool moving = Movement.IsMoving && enemy.GetComponent<PlayerController>() is not { IsDucking: true };
			Movement.Stop();
			var facing = delta.WithZ( 0 );
			if ( !facing.IsNearlyZero() ) WorldRotation = Rotation.LookAt( facing );
			if ( recovery > 0 ) { Stance = Threat; return; }

			bool close = distance < 40;
			// aschar.as GetAttackPath: moving_close / moving, or stationary_close / stationary.
			Stance = moving ? (close ? Jab : Roundhouse) : (close ? Knee : Kick);
			strike = boundAnimations.Clips[Stance];
			Speak( "attack" );
			attackTime = 0;
			impactPassed = false;
			return;
		}

		if ( repath <= 0 )
		{
			repath = 0.2f;
			Movement.MoveTo( destination, 0.45f * 40, visible || sinceEnemySeen < 2f ? 8f * 40 : 2.2f * 40 );
		}
		Stance = Locomotion;
	}

	void TryHit( int bone )
	{
		var position = Renderer.TryGetBoneTransform( $"bone_{bone}", out var contact ) ? contact.Position : EyePosition;
		// HandleAnimationEvent allows an extra 0.1 m at contact for an unarmed strike.
		if ( !CanAttack( enemy ) || WorldPosition.Distance( enemy.WorldPosition ) >= 1.6f * 40 || !HasSight( enemy ) )
		{
			PlayCombatSound( "Data/Sounds/whoosh/hit_whoosh.xml", position );
			return;
		}
		var victim = enemy.GetComponentInParent<IDamageable>();
		if ( victim is null ) return;
		var damage = new DamageInfo
		{
			Damage = Random.Shared.Float( strike.DamageMin, strike.DamageMax ) * 100,
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
