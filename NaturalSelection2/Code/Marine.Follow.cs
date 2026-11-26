using Sandbox.Npcs;
using Sandbox.Npcs.Schedules;
using System;

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

	TimeUntil guardInputExpires = -1;
	bool guardSprint;
	[Sync( SyncFlags.FromHost ), Change( nameof( OnCrouchingChanged ) )] bool crouching { get; set; }
	float crouchProgress;
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] float crouchAmount { get; set; }

	void UpdateGuardInput()
	{
		if ( NpcConVars.Enabled && ActiveSchedule is FollowSchedule && CanFollowLeader &&
			Leader.GetComponent<PlayerController>() is { } player )
		{
			// WishVelocity is the movement input replicated by the player's controller.
			bool running = player.IsOnGround && player.WishVelocity.WithZ( 0 ).LengthSquared > player.WalkSpeed * player.WalkSpeed;
			if ( player.IsDucking || running )
			{
				guardSprint = !player.IsDucking;
				guardInputExpires = 1;
			}
		}

		bool desired = !guardSprint && guardInputExpires >= 0;
		crouchProgress = Math.Clamp( crouchProgress + (crouching ? 4 : -4) * Time.Delta, 0, 1 );
		crouchAmount = crouchProgress is 0 or 1 ? crouchProgress : MathF.Sin( MathF.PI * 0.5f * crouchProgress );
		if ( desired == crouching ) return;

		var capsule = GetComponent<CapsuleCollider>();
		var standing = Vector3.Up * (StandingHeight - capsule.Radius);
		var crouched = Vector3.Up * (StandingHeight * 0.5f - capsule.Radius);
		if ( !desired && Scene.Trace.Sphere( capsule.Radius, WorldTransform.PointToWorld( crouched ), WorldTransform.PointToWorld( standing ) )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).WithCollisionRules( GameObject.Tags ).Run().Hit ) return;

		crouching = desired;
	}

	void OnCrouchingChanged( bool previous, bool current )
	{
		if ( GetComponent<CapsuleCollider>() is { } capsule )
			capsule.End = Vector3.Up * ((current ? StandingHeight * 0.5f : StandingHeight) - capsule.Radius);
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
