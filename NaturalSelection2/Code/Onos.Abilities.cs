using Sandbox.Npcs;
using System;
using System.Runtime.CompilerServices;

namespace NS2;

public sealed partial class Onos
{
	[Property] public bool ChargeUnlocked { get; set; } = true;
	[Property] public bool BoneShieldUnlocked { get; set; } = true;
	[Property] public bool StompUnlocked { get; set; } = true;
	[Sync( SyncFlags.FromHost )] public float ShieldHealth { get; private set; } = 1000;

	[Sync( SyncFlags.FromHost )] bool Shielding { get; set; }
	bool charging, wantsStomp, stomping, stompHit;
	TimeSince sinceCharge, sinceChargeEnd = 10, sinceDamage = 10, sinceShieldEnd = 10, sinceBlocked = 10, sinceStomp, sinceStompRequest = 10;
	TimeUntil rechargeShield, nextHeal;
	float stompDuration;
	sealed class StampedeCooldown { public TimeUntil Remaining; }
	static readonly ConditionalWeakTable<GameObject, StampedeCooldown> StampedeCooldowns = new();

	void ChooseAbility( GameObject enemy, float distance )
	{
		bool wantShield = !attacking && sinceDamage < 1 && BoneShieldUnlocked && !wantsStomp;
		SetShield( wantShield );
		if ( !attacking && !wantShield && StompUnlocked && (wantsStomp || sinceStompRequest > 6) &&
			energy > 30 && distance <= 12 * Scale &&
			(enemy.GetComponent<PlayerController>().IsValid() || enemy.GetComponent<BaseNpc>().IsValid()) &&
			Vector3.Dot( ViewRotation.Forward.WithZ( 0 ).Normal, (enemy.WorldPosition - WorldPosition).Normal ) >= 0.9f )
		{
			// The bot holds secondary through the stomp. Its cooldown starts on the last input.
			wantsStomp = true;
			sinceStompRequest = 0;
			if ( !stomping && Animation.IsGrounded && stompDuration > 0 )
			{
				sinceStomp = 0;
				stomping = true;
				stompHit = false;
				energy -= 30;
			}
		}
		UpdateCharge();
	}

	void UpdateCharge()
	{
		if ( !Movement.MoveTarget.HasValue || !ChargeUnlocked || Shielding || stomping || energy <= (charging ? 55 : 85) )
		{
			EndCharge();
			return;
		}
		var agent = GetComponent<NavMeshAgent>();
		var corners = agent.GetPath().Points;
		var forward = ViewRotation.Forward.WithZ( 0 ).Normal;
		float alignment = Vector3.Dot( agent.WishVelocity.WithZ( 0 ).Normal, forward );
		var previous = WorldPosition;
		float distance = 0;
		// The first point is the navigation agent's sampled position.
		for ( int i = 1; i < corners.Count; i++ )
		{
			var corner = corners[i].Position;
			alignment *= Vector3.Dot( (corner - WorldPosition).WithZ( 0 ).Normal, forward );
			distance += previous.Distance( corner );
			if ( distance >= 4 * Scale ) break;
			previous = corner;
		}

		if ( alignment <= 0.9f ) EndCharge();
		else if ( !charging && Animation.IsGrounded && sinceChargeEnd > 0.5f )
		{
			charging = true;
			sinceCharge = 0;
			PlayCue( "wound_serious" );
		}
	}

	void EndCharge()
	{
		if ( !charging ) return;
		charging = false;
		sinceChargeEnd = 0;
	}

	void SetShield( bool wanted )
	{
		if ( wanted == Shielding ) return;
		if ( wanted )
		{
			if ( sinceShieldEnd <= 0.75f || ShieldHealth <= 100 || !Animation.IsGrounded || charging ) return;
			Shielding = true;
			PlayCue( "land" );
		}
		else
		{
			Shielding = false;
			sinceShieldEnd = 0;
			rechargeShield = ShieldHealth <= 0 ? 6 : sinceBlocked <= 3 ? 3 : 0;
		}
	}

	void BlockDamage( IDamageable.DamageEvent e )
	{
		if ( !Shielding ) return;
		var hit = e.DamageInfo;
		var tags = hit.Tags;
		if ( !(tags.Has( "bullet" ) || tags.Has( "blast" ) || tags.Has( "explosion" ) || tags.Has( "buckshot" ) ||
			tags.Has( "melee" ) || tags.Has( "burn" ) || tags.Has( "heavy" ) ) ) return;
		if ( Vector3.Dot( ViewRotation.Forward.WithZ( 0 ).Normal, (hit.Position - WorldPosition).Normal ) < 0.34f ) return;
		float factor = tags.Has( "heavy" ) ? 2 : 1;
		float blocked = Math.Min( e.AcceptedDamage * factor, ShieldHealth );
		ShieldHealth -= blocked;
		e.AcceptedDamage -= blocked / factor;
		sinceBlocked = 0;
		rechargeShield = 3;
		if ( ShieldHealth <= 0 )
		{
			SetShield( false );
			PlaySoundEvent( "alien/common/armor_break" );
		}
	}

	void UpdateAbilities( Vector3 velocity )
	{
		energy = Math.Clamp( energy + Time.Delta * ((Shielding ? 5 : 10) - (charging ? 20 : 0)), 0, 100 );
		if ( charging && (energy <= 0 || sinceCharge > 1 && velocity.Length < 4.5f * Scale) ) EndCharge();
		var speed = (RunSpeed + (charging ? Math.Min( 1, sinceCharge / 0.5f ) * (12.5f * Scale - RunSpeed) : 0)) * (Shielding ? 0.682f : 1);
		Movement.RunSpeed = Movement.WalkSpeed = speed;
		if ( rechargeShield <= 0 && ShieldHealth < 1000 ) ShieldHealth = Math.Min( 1000, ShieldHealth + 80 * Time.Delta );
		if ( nextHeal <= 0 )
		{
			nextHeal = 2;
			if ( sinceDamage > 3 && !Shielding )
			{
				float heal = Math.Clamp( MaxHealth * 0.02f, 1, 20 );
				float missing = Math.Min( MaxHealth - Health, heal );
				Health += missing;
				Armor = Math.Min( MaxArmor, Armor + (heal - missing) * 0.5f );
			}
		}
		if ( stomping && sinceStomp > stompDuration + 0.15f ) wantsStomp = stomping = false;
		if ( velocity.Length > 8.5f * Scale ) Stampede( velocity );

	}

	void Stampede( Vector3 velocity )
	{
		var facing = Rotation.LookAt( ViewRotation.Forward.WithZ( 0 ).Normal );
		var center = WorldPosition + Vector3.Up * Scale + facing.Forward * (1.2f * Scale);
		// The source query includes the half-diagonal of the impact box plus 1.5 metres.
		foreach ( var obj in Scene.FindInPhysics( new Sphere( center, (MathF.Sqrt( 3.88f ) + 1.5f) * Scale ) ) )
		{
			var body = obj.GetComponentInParent<Rigidbody>();
			bool prop = IsLooseProp( body );
			var target = obj.GetComponentInParent<IDamageable>() as Component;
			if ( !prop && (!target.IsValid() || target.GameObject.Tags.Has( "structure" ) || GetDisposition( target.GameObject ) != Disposition.Hostile) ) continue;
			var enemy = prop ? body.GameObject : target.GameObject;
			if ( StampedeCooldowns.TryGetValue( enemy, out var cooldown ) && cooldown.Remaining > 0 ) continue;
			var collider = obj.GetComponent<Collider>() ?? enemy.GetComponentInChildren<Collider>();
			if ( !collider.IsValid() ) continue;
			var point = collider.FindClosestPoint( center );
			var offset = facing.Inverse * (point - center);
			if ( Math.Abs( offset.x ) > 1.2f * Scale || Math.Abs( offset.y ) > Scale || Math.Abs( offset.z ) > 1.2f * Scale ) continue;
			if ( prop && !CanReachProp( WorldPosition + Vector3.Up * Scale, point, body ) ) continue;
			(cooldown ?? StampedeCooldowns.GetValue( enemy, _ => new() )).Remaining = 0.7f;

			if ( prop )
			{
				PushProp( body, point, velocity );
				continue;
			}
			body = enemy.GetComponent<Rigidbody>();
			if ( body.IsValid() && body.MotionEnabled )
			{
				var push = (CalculateAimPoint( enemy ) - WorldPosition).Normal;
				var impact = velocity * 1.2f + (push - velocity.Normal).WithZ( 0 ) * (10 * Scale) + Vector3.Up * (6.5f * Scale * (1 - body.PhysicsBody.Mass / 1000));
				var player = enemy.GetComponent<PlayerController>();
				if ( player.IsValid() ) KnockBackPlayer( player, impact );
				else body.Velocity = impact;
			}
			DealDamage( (IDamageable)target, 12, point, "charge" );
			PlayCue( "charge_hit_marine" );
		}
	}

	internal void PushHitProp( SceneTraceResult hit, Vector3 velocity )
	{
		if ( hit.GameObject?.GetComponentInParent<Rigidbody>() is { } body && IsLooseProp( body ) )
			PushProp( body, hit.HitPosition, velocity );
	}

	static bool IsLooseProp( Rigidbody body ) => body.IsValid() && body.MotionEnabled && body.PhysicsBody.IsValid() &&
		!body.GameObject.Tags.Has( "structure" ) && !body.GetComponentInParent<BaseNpc>().IsValid() &&
		!body.GetComponentInParent<PlayerController>().IsValid();

	bool CanReachProp( Vector3 origin, Vector3 point, Rigidbody body ) => !Scene.Trace.Ray( origin, point )
		.IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( body.GameObject ).WithoutTags( "trigger" ).Run().Hit;

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PushProp( Rigidbody body, Vector3 point, Vector3 velocity )
	{
		if ( !body.IsValid() || body.IsProxy || !body.MotionEnabled || !body.PhysicsBody.IsValid() ) return;
		float closingSpeed = Vector3.Dot( velocity - body.Velocity, velocity.Normal );
		if ( closingSpeed <= 0 ) return;
		float mass = body.PhysicsBody.Mass;
		const float onosMass = 453;
		body.ApplyImpulseAt( point, velocity.Normal * (closingSpeed * mass * onosMass / (mass + onosMass)) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void KnockBackPlayer( PlayerController player, Vector3 impact )
	{
		if ( !player.IsValid() || player.IsProxy || !player.Body.IsValid() ) return;
		player.PreventGrounding( 0.2f );
		player.Body.Velocity = impact;
	}
}
