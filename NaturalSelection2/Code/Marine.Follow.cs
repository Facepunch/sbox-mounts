using Sandbox.Npcs;
using Sandbox.Npcs.Schedules;

namespace NS2;

public sealed partial class Marine : Component.IPressable
{
	[Sync( SyncFlags.FromHost )] GameObject Leader { get; set; }
	bool CanFollowLeader => Friendly && Leader.IsValid() && Leader.Active;

	[Property, Title( "#entity.property.npc_friendly" ), Description( "#entity.description.npc_friendly" ), ClientEditable]
	public bool Friendly
	{
		get => Faction == Factions.Ally;
		set
		{
			if ( Friendly == value ) return;
			Faction = value ? Factions.Ally : Factions.Enemy;
			if ( !value ) Leader = null;
		}
	}

	protected override BaseSchedule SelectSchedule()
	{
		var follow = GetSchedule<FollowSchedule>();
		follow.Target = Leader;
		return follow;
	}

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e ) => !Friendly ? null :
		Leader == e.Source.GameObject.Root ? new( "#npc.action.stop_following", "person_off", null ) :
		new( "#npc.action.follow", "follow_the_signs", null );

	bool IPressable.CanPress( IPressable.Event e ) => Friendly;

	bool IPressable.Press( IPressable.Event e )
	{
		ToggleFollow( e.Source.GameObject );
		return true;
	}

	[Rpc.Host]
	void ToggleFollow( GameObject source )
	{
		var player = source?.GetComponentInParent<PlayerController>();
		if ( !Friendly || !player.IsValid() || player.WorldPosition.Distance( WorldPosition ) > 160 ||
			(Rpc.Caller is not null && player.Network.Owner != Rpc.Caller) ) return;

		EndCurrentSchedule();
		Leader = Leader == player.GameObject ? null : player.GameObject;
	}
}
