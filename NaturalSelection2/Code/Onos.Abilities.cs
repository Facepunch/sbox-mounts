using Sandbox.Npcs;
using System;

namespace NS2;

public sealed partial class Onos
{
	[Property] public bool ChargeUnlocked { get; set; } = true;
	[Property] public bool BoneShieldUnlocked { get; set; } = true;
	[Property] public bool StompUnlocked { get; set; } = true;
	[Sync( SyncFlags.FromHost )] public float ShieldHealth { get; private set; } = 1000;

	[Sync( SyncFlags.FromHost )] bool Shielding { get; set; }
	bool charging, stomping, stompHit;
	bool straightPath;
	TimeSince sinceCharge, sinceChargeEnd = 10, sinceDamage = 10, sinceShieldEnd = 10, sinceBlocked = 10, sinceStomp = 10;
	TimeUntil rechargeShield, nextHeal, clearStampedes;
	float stompDuration;
	Dictionary<GameObject, TimeUntil> stampedeCooldowns;

	void ChooseAbility( GameObject enemy, float distance )
	{
		bool melee = distance < 4 * Scale;
		bool wantShield = !melee && sinceDamage < 1 && BoneShieldUnlocked && !stomping;
		SetShield( wantShield );
		if ( !melee && !Shielding && !attacking && !stomping && StompUnlocked && sinceStomp > 6 && stompDuration > 0 &&
			energy > 30 && distance <= 12 * Scale && Animation.IsGrounded &&
			(enemy.GetComponent<PlayerController>().IsValid() || enemy.GetComponent<BaseNpc>().IsValid()) &&
			Vector3.Dot( ViewRotation.Forward.WithZ( 0 ).Normal, (enemy.WorldPosition - WorldPosition).Normal ) >= 0.9f )
		{
			sinceStomp = 0;
			stomping = true;
			stompHit = false;
			energy -= 30;
		}

		UpdateCharge();
	}

	TimeUntil checkCorners;

	void UpdateCharge()
	{
		if ( !Movement.MoveTarget.HasValue )
		{
			EndCharge();
			return;
		}
		if ( checkCorners <= 0 )
		{
			var forward = WorldRotation.Forward.WithZ( 0 ).Normal;
			checkCorners = 0.2f;
			var path = GetComponent<NavMeshAgent>().GetPath();
			float similarity = 1;
			foreach ( var corner in path.Points )
			{
				var offset = corner.Position - WorldPosition;
				if ( offset.WithZ( 0 ).LengthSquared < 1 ) continue;
				similarity *= Vector3.Dot( offset.WithZ( 0 ).Normal, forward );
				if ( offset.Length >= 4 * Scale ) break;
			}
			straightPath = similarity > 0.9f;
		}
		bool wantsCharge = ChargeUnlocked && !Shielding && !stomping && straightPath && energy > (charging ? 55 : 85) &&
			Vector3.Dot( Animation.MoveVelocity.WithZ( 0 ).Normal, WorldRotation.Forward.WithZ( 0 ).Normal ) > 0.9f;
		if ( !wantsCharge ) EndCharge();
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
		if ( ShieldHealth <= 0 ) SetShield( false );
	}

	void UpdateAbilities( Vector3 velocity )
	{
		energy = Math.Clamp( energy + Time.Delta * ((Shielding ? 5 : 10) - (charging ? 20 : 0)), 0, 100 );
		if ( charging && (energy <= 0 || sinceCharge > 1 && velocity.Length < 4.5f * Scale) ) EndCharge();
		var speed = (RunSpeed + (charging ? Math.Min( 1, sinceCharge / 0.5f ) * (12.5f * Scale - RunSpeed) : 0)) * (Shielding ? 0.682f : 1);
		Movement.RunSpeed = Movement.WalkSpeed = speed;
		GetComponent<NavMeshAgent>().Acceleration = 6.5f * speed;
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
		if ( stomping && sinceStomp > stompDuration + 0.15f ) stomping = false;
		if ( velocity.Length > 8.5f * Scale ) Stampede( velocity );
		else if ( clearStampedes <= 0 ) stampedeCooldowns?.Clear();
	}

	void Stampede( Vector3 velocity )
	{
		var forward = WorldRotation.Forward.WithZ( 0 ).Normal;
		var center = WorldPosition + Vector3.Up * Scale + forward * (1.2f * Scale);
		foreach ( var enemy in Senses.VisibleHostiles )
		{
			if ( stampedeCooldowns?.TryGetValue( enemy, out var until ) == true && until > 0 ) continue;
			var collider = enemy.GetComponentInChildren<Collider>();
			if ( !collider.IsValid() || enemy.GetComponent<IDamageable>() is not { } victim ) continue;
			var point = collider.FindClosestPoint( center );
			var offset = WorldTransform.Rotation.Inverse * (point - center);
			if ( Math.Abs( offset.x ) > 1.2f * Scale || Math.Abs( offset.y ) > Scale || Math.Abs( offset.z ) > 1.2f * Scale ) continue;
			(stampedeCooldowns ??= [])[enemy] = 0.7f;
			clearStampedes = 0.7f;
			var body = enemy.GetComponent<Rigidbody>();
			if ( body.IsValid() && body.MotionEnabled )
			{
				var push = (CalculateAimPoint( enemy ) - WorldPosition).Normal;
				body.Velocity = velocity * 1.2f + (push - velocity.Normal).WithZ( 0 ) * (10 * Scale) + Vector3.Up * (6.5f * Scale * (1 - body.PhysicsBody.Mass / 1000));
			}
			DealDamage( victim, 12, point, "charge" );
			PlayCue( "charge_hit_marine" );
		}
	}
}
