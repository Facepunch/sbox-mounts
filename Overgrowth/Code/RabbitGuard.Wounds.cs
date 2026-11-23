using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Property] public bool MetalArmor { get; set; }
	[Property] public bool CannotBeDisarmed { get; set; }
	[Sync] float BloodLost { get; set; }
	internal const string HalfWounded = "half_wounded_idle", Wounded = "wounded_idle";

	string WoundedPose( string pose )
	{
		if ( pose is not (Idle or Ready or Threat) || BloodLost <= 0f || boundAnimations.Species == "wolf" ) return pose;
		bool severe = BloodLost > MaxHealth * 0.5f && (PrimaryItem.IsValid() || Animation.MoveVelocity.LengthSquared < 0.5f * 40f * 40f);
		return severe ? Wounded : HalfWounded;
	}
	int initialKnockoutShield;
	class BloodDamage : DamageInfo;

	readonly List<float> painStarts = new();

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void FlinchFromCut() => painStarts.Add( Time.Now );

	void UpdatePainFlinch()
	{
		if ( painStarts.Count == 0 ) return;
		const float fadeSpeed = 8f;
		float duration = boundAnimations.Clips["pain"].Duration;
		foreach ( var group in boundAnimations.PainMask )
		{
			float remaining = 1f;
			foreach ( float start in painStarts )
			{
				float age = Time.Now - start;
				float opacity = Math.Clamp( Math.Min( (age + 1f / 120f) * fadeSpeed, 1f - (age - duration) * fadeSpeed ), 0f, 1f );
				remaining *= 1f - opacity * group.Weight;
			}
			Renderer.Animation.Parameters.Set( group.Name, 1f - remaining );
		}
		while ( painStarts.Count > 0 && Time.Now - painStarts[0] >= duration + 1f / fadeSpeed ) painStarts.RemoveAt( 0 );
	}

	// BaseNpc has already subtracted the accepted hit. Apply TakeBloodDamage's
	// separate blood, permanent and temporary health changes after damage listeners.
	void ApplyBloodLoss( float amount )
	{
		float permanent = Health + amount;
		float temporary = permanent - recoverableDamage;
		BloodLost += amount;
		float blood = MaxHealth - BloodLost;
		Health = permanent > blood ? permanent - 0.5f * amount : permanent;
		recoverableDamage = Math.Max( 0f, Health - temporary );
		if ( blood <= 0f && !Unconscious ) Health = 0f;
	}

	void ApplySharpHit( RabbitClip clip, Vector3 position, Vector3 direction, RabbitGuard defender, bool parried )
	{
		var label = boundAnimations.WeaponLabel;
		bool knife = label == "knife";
		float sharp = Random.Shared.Float( strike.SharpMin, strike.SharpMax );
		var target = strikeTarget;
		var victim = target.GetComponentInParent<IDamageable>();
		if ( victim is null ) return;
		var weapon = PrimaryItem.IsValid() ? PrimaryItem.GameObject : GameObject;
		void Cut( float amount )
		{
			if ( !target.IsValid() || amount <= 0f ) return;
			var damage = new BloodDamage { Damage = amount * 100f, Origin = CharacterCenter( GameObject ), Position = position, Attacker = GameObject, Weapon = weapon };
			damage.Tags.Add( "slash" );
			damage.Tags.Add( "nevergib" );
			victim.OnDamage( damage );
		}

		string species = defender?.boundAnimations?.Species;
		if ( !parried )
		{
			if ( defender is { MetalArmor: true } )
			{
				if ( !knife ) PlayCombatSound( "Data/Sounds/weapon_foley/cut/flesh_hit.xml", position );
				Cut( knife ? sharp * AttackDamage * 0.5f : 0.3f );
				if ( Random.Shared.Next( 2 ) == 0 ) PlayArmorImpact( position, !knife );
			}
			else
			{
				if ( species == "dog" || species == "cat" && defender.PrimaryItem.IsValid() || boundAnimations.Species == "cat" )
				{
					sharp *= 0.5f;
					if ( label != "big_sword" ) sharp = Math.Min( 0.5f, sharp );
				}
				Cut( sharp * AttackDamage );
			}
		}

		if ( defender.IsValid() )
		{
			if ( !parried )
			{
				if ( defender.Health > 0f && (defender.Unconscious || defender.blockHealth <= 0f) )
				{
					ApplyStrikeImpulse( clip, position, direction );
					if ( defender.IsValid() && sharp < 0.5f ) defender.limpStun = 0f;
				}
				else defender.KnockoutShield = Math.Max( 0, defender.KnockoutShield - 1 );
			}
			if ( !defender.IsValid() ) return;
			if ( label is "sword" or "rapier" or "big_sword" ) defender.KnockoutShield = Math.Max( 0, defender.KnockoutShield - 1 );
			if ( species == "wolf" && defender.KnockoutShield == 0 ) defender.BloodLost = defender.MaxHealth;
		}
		if ( parried && defender.IsValid() && (defender.PrimaryItem.IsValid() || defender.MetalArmor) )
		{
			defender.ClashWith( PrimaryItem );
			if ( !defender.PrimaryItem.IsValid() || Random.Shared.Next( 2 ) == 0 )
			{
				PlayCombatSound( "Data/Sounds/weapon_foley/cut/flesh_hit.xml", position );
				Cut( 0.3f );
			}
			if ( defender.IsValid() && defender.MetalArmor && !defender.PrimaryItem.IsValid() ) PlayArmorImpact( position, true );
		}
		else
		{
			PlayCombatSound( "Data/Sounds/weapon_foley/cut/flesh_hit.xml", position );
			if ( !knife ) Cut( species == "wolf" ? 0.01f : 0.3f );
			else if ( species == "wolf" && defender.IsValid() )
			{
				int shield = defender.initialKnockoutShield;
				if ( shield > 0 ) defender.BloodLost = Math.Max( defender.BloodLost, defender.MaxHealth * (1f - (defender.KnockoutShield + 0.5f) / shield) );
				Cut( 0.1f );
				if ( defender.IsValid() ) defender.KnockoutShield = Math.Min( defender.KnockoutShield, Math.Max( 0, (int)(shield * (1f - defender.BloodLost / defender.MaxHealth)) ) );
			}
		}
		if ( target.IsValid() ) ReportSound( target, position, HeardSound.Combat, 2f );
		if ( defender.IsValid() ) defender.TryDisarm( strike.WorldImpact( direction, flippedAttack ), PrimaryItem );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayArmorImpact( Vector3 position, bool strong )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		mount.PlayGroup( $"Data/Sounds/weapon_foley/impact/weapon_metal_hit_metal{(strong ? "_strong" : "")}.xml", position );
		mount.MetalSparks( position );
	}

	void TryDisarm( Vector3 direction, LevelItem strikingWeapon = null )
	{
		var item = LeftHandItem.IsValid() ? LeftHandItem : RightHandItem;
		if ( !item.IsValid() || CannotBeDisarmed || Random.Shared.Float() >= 0.2f ) return;
		ClashWith( strikingWeapon );
		DropHandItem();
		var body = item.GetComponent<Rigidbody>();
		body.Velocity = direction * 200f;
		body.AngularVelocity = new Vector3( Random.Shared.Float( -20f, 20f ), Random.Shared.Float( -20f, 20f ), Random.Shared.Float( -20f, 20f ) );
	}
}
