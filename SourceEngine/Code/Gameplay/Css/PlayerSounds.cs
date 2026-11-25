using System;
using Sandbox.Npcs;

namespace Source1.Css;

/// <summary>
/// The sounds of a Counter-Strike player, for an NPC wearing a player model: hit and death sounds. Its radio calls are <see cref="NpcVoice"/> lines.
/// The game plays these from code; the sound scripts come from the mount, so lines it lacks stay silent.
/// </summary>
[Title( "CS:S Player Sounds" )]
[Category( "Counter-Strike: Source" )]
[Alias( "Source1.CssPlayerSounds", "CssPlayerSounds" )]
public sealed class PlayerSounds : Component, INpcPart
{
	// CCSPlayer::OnTakeDamage and CCSPlayer::DeathSound.
	const string HitSound = "Player.DamageKevlar";
	const string HeadHitSound = "Player.DamageHelmet";
	const string DeathSound = "Player.Death";
	const string HeadshotDeathSound = "Player.DeathHeadShot";

	void INpcPart.OnNpcHurt( Component.IDamageable.DamageEvent damage )
	{
		if ( damage.Victim.GetComponent<Sandbox.Npcs.BaseNpc>() is not { Health: >= 1 } )
			return;

		Play( damage.DamageInfo.Tags.Contains( DamageTags.Headshot ) ? HeadHitSound : HitSound, WorldPosition );
	}

	void INpcPart.OnNpcDeath( Component.IDamageable.DeathEvent death )
	{
		Play( death.DamageInfo.Tags.Contains( DamageTags.Headshot ) ? HeadshotDeathSound : DeathSound, WorldPosition );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Play( string name, Vector3 position )
	{
		if ( Sounds.Sound( name, MountContent.Of( GameObject ) ) is { } sound )
			Sound.Play( sound, position );
	}
}
