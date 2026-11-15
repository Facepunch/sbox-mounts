using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>
/// The sounds of a Counter-Strike player, for an NPC wearing a player model: hit and death sounds. Its radio calls are <see cref="SourceNpcVoice"/> lines.
/// The game plays these from code; the sound scripts come from the mount, so lines it lacks stay silent.
/// </summary>
[Title( "CS:S Player Sounds" )]
[Category( "Counter-Strike: Source" )]
public sealed class CssPlayerSounds : Component, ISourceNpcPart
{
	// CCSPlayer::OnTakeDamage and CCSPlayer::DeathSound.
	const string HitSound = "Player.DamageKevlar";
	const string HeadHitSound = "Player.DamageHelmet";
	const string DeathSound = "Player.Death";
	const string HeadshotDeathSound = "Player.DeathHeadShot";

	void ISourceNpcPart.OnNpcHurt( Component.IDamageable.DamageEvent damage )
	{
		if ( damage.Victim.GetComponent<Sandbox.Npcs.BaseNpc>() is not { Health: >= 1 } )
			return;

		Play( damage.DamageInfo.Tags.Contains( SourceDamageTags.Headshot ) ? HeadHitSound : HitSound, WorldPosition );
	}

	void ISourceNpcPart.OnNpcDeath( Component.IDamageable.DeathEvent death )
	{
		Play( death.DamageInfo.Tags.Contains( SourceDamageTags.Headshot ) ? HeadshotDeathSound : DeathSound, WorldPosition );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Play( string name, Vector3 position )
	{
		if ( SourceSounds.Sound( name, SourceMount.Of( GameObject ) ) is { } sound )
			Sound.Play( sound, position );
	}
}
