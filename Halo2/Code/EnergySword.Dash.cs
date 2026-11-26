using System;

namespace Halo2;

public sealed partial class EnergySword
{
	[Property] public float TargetAngle { get; set; }
	[Property] public float TargetRange { get; set; }

	sealed class Dash
	{
		public PlayerController Player { get; }
		public Vector3 EntryVelocity { get; }
		public float Speed { get; private set; }
		public bool Finished { get; private set; }

		readonly float step, minimumSpeed, targetLead;
		readonly int maxTicks;
		float? braking;
		int tick;

		public Dash( PlayerController player, float distance, float targetForwardSpeed )
		{
			Player = player;
			EntryVelocity = player.Body.Velocity;
			step = DashStep( distance );
			maxTicks = (int)MathF.Round( 7 + TravelTicks( distance, step ) );
			minimumSpeed = Math.Clamp( 1.5f * targetForwardSpeed, 0.75f * 120, 3.5f * 120 );
			targetLead = Math.Clamp( targetForwardSpeed, 0, 2.5f * 120 ) / 5;
		}

		public void Advance( float distance )
		{
			float remaining = distance + targetLead;
			float brakingDistance = (Speed / 30 - step / 3) * 1.5f;
			if ( braking.HasValue || brakingDistance > remaining || maxTicks - tick <= 4 )
			{
				// The first braking tick fixes the decrement for the rest of the dash.
				braking ??= MathF.Max( minimumSpeed, Speed - minimumSpeed ) / 4;
				Finished = MathF.Round( remaining / step - 0.5f ) <= 0 || Speed <= 0.001f * 120 * 30;
				Speed = MathF.Max( 0, Speed - braking.Value );
			}
			else Speed = MathF.Min( Speed + step * 10, step * 30 );
			if ( ++tick >= maxTicks + 6 ) Finished = true;
		}
	}
	Dash activeDash;

	public override bool CanSecondaryAttack() => activeDash is null && base.CanSecondaryAttack();

	bool CanInterruptLunge => (activeDash is not null || MeleeActive)
		&& MeleeAction is "melee_dash" or "melee_dash_airborne" or "melee_lunge" or "melee_lunge_airborne";

	public override bool CanReload() => IsActive && CanInterruptLunge;

	public override void Reload()
	{
		if ( Networking.IsHost )
		{
			if ( CanReload() ) InterruptLunge();
		}
		else RequestInterrupt();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestInterrupt() => Reload();

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void InterruptLunge() => InterruptMelee();

	protected override string PrimaryMeleeAction()
	{
		if ( LungeDamage is null || Holder is not PlayerController player ) return null;
		var ray = player.EyeTransform.ForwardRay;
		var target = FindAttackTarget( player, ray, new( TargetAngle, TargetRange ), out var point );
		if ( !target.IsValid() ) return null;
		float distance = point.Distance( ray.Position );
		if ( distance <= MeleeRange ) return LungeAction( player );
		var unit = target.GetComponentInParent<Sandbox.Npcs.BaseNpc>()?.GameObject ?? target;
		var velocity = Sandbox.Npcs.BaseNpc.GetVelocity( unit );
		var approach = DashApproach( point - ray.Position, ray.Forward, velocity );
		StartDash( target, target.WorldTransform.PointToLocal( point ), approach, Vector3.Dot( ray.Forward, velocity ) );
		return player.IsOnGround ? "melee_dash" : "melee_dash_airborne";
	}

	static string LungeAction( PlayerController player ) => player.IsOnGround ? "melee_lunge" : "melee_lunge_airborne";

	static float DashStep( float distance ) => MathF.Min( 12f * 120 / 30, distance * 0.375f );

	static float TravelTicks( float distance, float step ) => 6 + MathF.Max( distance - 3 * step, 0 ) / step;

	static Vector3 DashApproach( Vector3 displacement, Vector3 aim, Vector3 targetVelocity )
	{
		float distance = displacement.Length;
		float lead = Math.Clamp( Vector3.Dot( aim, targetVelocity ) * 1.5f / (2.5f * 120), 0, 1 );
		return displacement + targetVelocity * (TravelTicks( distance, DashStep( distance ) ) / 30 * lead);
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void StartDash( GameObject target, Vector3 localPoint, Vector3 approach, float targetForwardSpeed )
	{
		if ( Holder is not PlayerController player || (!Networking.IsHost && player.IsProxy) ) return;
		var dash = new Dash( player, approach.Length, targetForwardSpeed );
		activeDash = dash;
		_ = RunDash( dash, target, localPoint, approach.Normal );
	}

	async System.Threading.Tasks.Task RunDash( Dash dash, GameObject target, Vector3 localPoint, Vector3 direction )
	{
		var player = dash.Player;
		var clock = TaskSource.Create( player.GameObject.EnabledToken );
		double nextTick = Time.NowDouble;
		try
		{
			// Movement belongs to the player and continues after the weapon is put away.
			while ( true )
			{
				await clock.FixedUpdate();
				if ( !ReferenceEquals( activeDash, dash ) ) return;
				var eye = player.EyeTransform;
				if ( !target.IsValid() ) break;
				float distance = target.WorldTransform.PointToWorld( localPoint ).Distance( eye.Position );
				if ( distance <= MeleeRange ) TryLunge( player );
				while ( Time.NowDouble >= nextTick && !dash.Finished )
				{
					nextTick += 1.0 / 30;
					// The native action retains its launch direction until the player's aim leaves the cone.
					if ( Vector3.Dot( eye.Forward, direction ) < 0.8660254f ) direction = eye.Forward;
					dash.Advance( distance );
				}
				if ( dash.Finished && Time.NowDouble >= nextTick ) break;
				var travel = direction * (dash.Speed * player.Scene.FixedDelta);
				if ( player.IsOnGround ) travel = travel.WithZ( MathF.Max( 0, travel.z ) );
				if ( DashBlocked( player, travel ) ) break;
				if ( !player.IsProxy ) player.Body.Velocity = direction * dash.Speed;
			}
		}
		finally
		{
			if ( ReferenceEquals( activeDash, dash ) ) StopMovement();
		}
		if ( !IsValid || !Networking.IsHost || Holder != player ) return;
		if ( player.IsProxy ) EndOwnerDash();
		TryLunge( player );
	}

	static bool DashBlocked( PlayerController player, Vector3 travel )
	{
		var body = player.BodyCollider;
		if ( !body.IsValid() ) return false;
		var transform = body.WorldTransform;
		var capsule = new Capsule( transform.PointToWorld( body.Start ), transform.PointToWorld( body.End ),
			body.Radius * MathF.Abs( transform.UniformScale ) );
		var contact = player.Scene.Trace.Capsule( capsule, Vector3.Zero, travel )
			.IgnoreGameObjectHierarchy( player.GameObject ).WithCollisionRules( player.Tags ).Run();
		return contact.Hit && Vector3.Dot( contact.Normal, travel ) < 0;
	}

	void TryLunge( PlayerController player )
	{
		if ( IsValid && Networking.IsHost && IsActive && Holder == player && MeleeAction is ("melee_dash" or "melee_dash_airborne") )
			PerformMeleeAction( LungeAction( player ), FirstPersonAnimations()?.Actions );
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	void EndOwnerDash() => StopMovement();

	void StopMovement()
	{
		if ( activeDash is not {} dash ) return;
		activeDash = null;
		var player = dash.Player;
		if ( player.IsValid() && !player.IsProxy && player.Body.IsValid() )
		{
			// Exit velocity is bounded against the velocity on entry to melee physics.
			var velocity = player.Body.Velocity.ClampLength( dash.EntryVelocity.Length + 0.8f * 120 );
			player.Body.Velocity = velocity.WithZ( MathF.Min( velocity.z, dash.EntryVelocity.z + 0.3f * 120 ) );
		}
	}

	protected override void PlayPutAway()
	{
		if ( CanInterruptLunge ) InterruptMelee();
		base.PlayPutAway();
	}

	protected override void OnHolstered()
	{
		if ( CanInterruptLunge ) InterruptMelee();
		base.OnHolstered();
	}
}
