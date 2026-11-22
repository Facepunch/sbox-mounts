using GoldSrc.HalfLife;
using Sandbox.Npcs;

namespace GoldSrc.OpposingForce;

[Hide]
public abstract class AlliedGrunt : HumanGrunt, Component.IPressable
{
	[Sync( SyncFlags.FromHost )] protected GameObject Leader { get; private set; }

	internal override bool FollowLeader()
	{
		if ( !IsLiveTarget( Leader ) ) return false;
		if ( WorldPosition.DistanceSquared( Leader.WorldPosition ) > 128 * 128 )
			MoveToward( Leader.WorldPosition, 128 );
		else Movement?.Stop();
		return true;
	}

	bool CanFollow( GameObject player ) => player.IsValid() && GetDisposition( player ) != Disposition.Hostile;

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e ) => Leader == e.Source.GameObject
		? new IPressable.Tooltip( "#npc.action.stop_following", "person_off", null )
		: new IPressable.Tooltip( "#npc.action.follow", "follow_the_signs", null );

	bool IPressable.CanPress( IPressable.Event e ) => CanFollow( e.Source.GameObject );

	bool IPressable.Press( IPressable.Event e )
	{
		ToggleFollow( e.Source.GameObject );
		return true;
	}

	[Rpc.Host]
	void ToggleFollow( GameObject source )
	{
		var player = source?.GetComponentInParent<PlayerController>();
		if ( !player.IsValid() || !CanFollow( player.GameObject ) || WorldPosition.Distance( player.WorldPosition ) > 160 ||
			(Rpc.Caller is not null && Rpc.Caller != player.Network.Owner) ) return;
		Leader = Leader == player.GameObject ? null : player.GameObject;
		Movement?.Stop();
		EmitSound( Leader.IsValid() ? "!FG_OK" : "!FG_WAIT", WorldPosition );
	}
}
