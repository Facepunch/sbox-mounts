using System.Runtime.CompilerServices;

namespace GoldSrc.HalfLife;

public partial class HumanGrunt
{
	sealed class RadioChannel
	{
		internal TimeUntil NextLine;
		internal string Reply;
	}

	static readonly ConditionalWeakTable<Scene, RadioChannel> Channels = new();
	RadioChannel Channel => Channels.GetValue( Scene, _ => new() );
	readonly float _radioPitch = Game.Random.Int( 0, 1 ) == 0 ? 1 : Game.Random.Int( 109, 116 ) / 100f;

	[Property, Group( "Speech" )] public bool QuietOutsideCombat { get; set; }

	bool Radio( string sentence )
	{
		var channel = Channel;
		if ( channel.NextLine > 0 ) return false;
		EmitSound( sentence, WorldPosition, 0.35f, _radioPitch );
		channel.NextLine = Game.Random.Float( 1.5f, 2 );
		return true;
	}

	internal override void OnEnemySpotted( GameObject target )
	{
		if ( SquadLeader.IsValid() ) return;
		if ( target.GetComponentInParent<PlayerController>().IsValid() ) Radio( "!HG_ALERT" );
		else if ( target.GetComponentInParent<Npc>() is { Faction: Sandbox.Npcs.Factions.Monster } ) Radio( "!HG_MONST" );
	}

	internal override void DeathSound() => EmitSound( Sounds.Death, WorldPosition, attenuation: GoldSrcSoundEvents.Idle );

	internal override void IdleSound()
	{
		var channel = Channel;
		if ( QuietOutsideCombat || channel.NextLine > 0 ) return;
		if ( channel.Reply is { } answer )
		{
			if ( Radio( answer ) ) channel.Reply = null;
			return;
		}
		if ( Game.Random.Int( 0, 1 ) == 0 ) return;
		switch ( Game.Random.Int( 0, 2 ) )
		{
			case 0: Radio( "!HG_CHECK" ); channel.Reply = "!HG_CLEAR"; break;
			case 1: Radio( "!HG_QUEST" ); channel.Reply = "!HG_ANSWER"; break;
			default: Radio( "!HG_IDLE" ); break;
		}
	}
}
