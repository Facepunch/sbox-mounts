using Sandbox.Npcs;
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

	Vector3? guardLookTarget;
	float lastGuardLook;
	TimeUntil guardInputExpires = -1;
	bool guardSprint;
	[Sync( SyncFlags.FromHost ), Change( nameof( OnCrouchingChanged ) )] bool crouching { get; set; }
	float crouchProgress;
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] float crouchAmount { get; set; }

	bool guarding;
	float? leaderCrouchedAt;

	void GuardPlayer( PlayerController player )
	{
		guarding = true;
		Animation.SetLookTarget( Leader );
		bool duck = player.IsDucking;
		if ( duck ) leaderCrouchedAt ??= Time.Now;
		else leaderCrouchedAt = null;

		var velocity = player.Velocity / Scale;
		float speed = velocity.WithZ( 0 ).Length;
		float distance = WalkDistance( Leader.WorldPosition ) / Scale;
		float desired = duck || (!guardSprint && guardInputExpires >= 0) ? 2 : 5;
		bool boost = speed == 0 && leaderCrouchedAt + 3 < Time.Now && distance > desired;
		if ( boost ) desired = 2.075f;
		float minimum = duck && speed == 0 ? 3.85f * 0.75f : 3.85f;

		if ( distance > desired || boost )
		{
			// NS2 uses a damped spring to decide when the guard should close the gap.
			float stretch = -2 * (distance - desired) * desired / distance;
			float damping = 0.02f * (Animation.MoveVelocity.Length / Scale - velocity.Length);
			float force = (WorldPosition.Normal * stretch - Vector3.One * damping).LengthSquared;
			if ( (force > (duck ? 7.5f : 15) && force < 500 && distance > minimum - 0.15f) || (boost && distance > desired) )
				Movement.MoveTo( Leader.WorldPosition, Scale );
			else if ( force < 1.1f || speed < 1 )
				Movement.Stop();
			return;
		}

		if ( distance is > 1.105f and < 1.55f && !duck && player.IsOnGround )
		{
			var eye = player.EyeTransform;
			var forward = eye.Rotation.Forward;
			bool playerSeesUs = Vector3.GetAngle( forward, WorldPosition - eye.Position ) < 42;
			bool weSeePlayer = Vector3.GetAngle( aim.Forward, Leader.WorldPosition - EyePosition ) < 75;
			var goal = playerSeesUs
				? WorldPosition + (weSeePlayer ? aim.Forward * -28 : forward * 12) * Scale
				: Leader.WorldPosition - forward * (18 * Scale);
			if ( Movement.ProjectPoint( goal ) is { } point ) Movement.MoveTo( point, Scale );
			else Movement.Stop();
			return;
		}

		if ( distance <= minimum - 0.25f ) return;
		Movement.Stop();
		if ( Time.Now > lastGuardLook + 3 + Random.Shared.NextSingle() * 3.5f )
		{
			lastGuardLook = Time.Now;
			guardLookTarget = EyePosition + Rotation.FromYaw( Random.Shared.NextSingle() * 360 ).Forward * (30 * Scale);
		}
		viewTarget = guardLookTarget;
	}

	void UpdateGuardInput()
	{
		if ( NpcConVars.Enabled && guarding && CanFollowLeader &&
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

		if ( guarding ) Movement.Stop();
		guarding = false;
		leaderCrouchedAt = null;
		Leader = Leader == player.GameObject ? null : player.GameObject;
		guardLookTarget = null;
		lastGuardLook = 0;
	}
}
