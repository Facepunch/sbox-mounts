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
	class BloodDamage : DamageInfo
	{
		internal float HealthBeforeHit { get; set; }
	}
	sealed class MeleeCut : BloodDamage
	{
		internal float SharpDamage { get; init; }
		internal bool AwakeAtImpact { get; init; }
		internal bool AppliesImpact { get; init; }
		internal (Vector3 Impulse, Vector3 Line) Contact { get; set; }
	}

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
	void ApplyBloodLoss( float amount, BloodDamage damage )
	{
		// BaseNpc clamps Health at zero when a cut exceeds the remaining permanent health.
		float permanent = Health > 0f ? PermanentHealth + amount : damage.HealthBeforeHit;
		float temporary = permanent - recoverableDamage;
		BloodLost += amount;
		float blood = MaxHealth - BloodLost;
		if ( permanent > blood ) permanent -= 0.5f * amount;
		SetLivingHealth( permanent );
		recoverableDamage = Math.Max( 0f, permanent - temporary );
		if ( damage is MeleeCut cut ) cut.Contact = (cut.Contact.Impulse * StrikeForceScale, cut.Contact.Line);
		if ( blood <= 0f && !Unconscious ) Health = 0f;
	}

	void ApplySharpHit( RabbitClip clip, Vector3 position, Vector3 direction, RabbitGuard defender, bool parried, float sharp )
	{
		var label = PrimaryItem?.Label;
		bool knife = label == "knife";
		var target = strikeTarget;
		var victim = target.GetComponentInParent<IDamageable>();
		if ( victim is null ) return;
		var weapon = PrimaryItem.IsValid() ? PrimaryItem.GameObject : GameObject;
		var contact = !parried && defender.IsValid() ? StrikeContact( direction ) : default;
		bool awakeAtImpact = defender.IsValid() && !defender.Unconscious;
		bool appliesImpact = !parried;
		void Cut( float amount, bool allowHeavy = false )
		{
			if ( !target.IsValid() || amount <= 0f ) return;
			var damage = new MeleeCut
			{
				Damage = amount * 100f, SharpDamage = sharp, AwakeAtImpact = awakeAtImpact,
				AppliesImpact = appliesImpact, Contact = contact, Origin = CharacterCenter( GameObject ),
				Position = position, Attacker = GameObject, Weapon = weapon
			};
			damage.Tags.Add( "slash" );
			damage.Tags.Add( "nevergib" );
			if ( strike.LightStab is { } light && defender.IsValid() && PrimaryItem.IsValid() )
			{
				var axis = PrimaryItem.StabDirection();
				if ( !axis.IsNearlyZero() )
				{
					defender.Stab( PrimaryItem.PhysicsPosition - axis * 200f, axis, light );
					PrimaryItem.MarkStab( CharacterCenter( GameObject ) );
				}
			}
			var faces = CutSurface( defender, position );
			if ( faces is { Count: > 0 } ) defender.Renderer.GetOrAddComponent<CutMarks>().Draw( faces );
			bool awake = defender.IsValid() && !defender.Unconscious;
			bool heavy = !allowHeavy || strike.HeavyCut;
			victim.OnDamage( damage );
			if ( faces is { Count: > 0 } )
			{
				bool knockedOut = awake && (!defender.IsValid() || defender.Health <= 0f || defender.Unconscious);
				EmitCutParticles( faces, knockedOut || !heavy );
			}
		}

		string species = defender?.boundAnimations?.Species;
		if ( !parried )
		{
			if ( defender is { MetalArmor: true } )
			{
				if ( knife )
				{
					if ( Random.Shared.Next( 2 ) == 0 ) PlayArmorImpact( position, false );
					Cut( sharp * AttackDamage * 0.5f, true );
				}
				else
				{
					PlayCombatSound( "Data/Sounds/weapon_foley/cut/flesh_hit.xml", position );
					bool bledOut = defender.BloodLost > defender.MaxHealth;
					Cut( 0.3f );
					if ( (bledOut || defender.IsValid() && defender.BloodLost < defender.MaxHealth) && Random.Shared.Next( 2 ) == 0 )
						PlayArmorImpact( position, true );
				}
			}
			else
			{
				if ( species == "dog" || species == "cat" && defender.PrimaryItem.IsValid() || boundAnimations.Species == "cat" )
				{
					sharp *= 0.5f;
					if ( label != "big_sword" ) sharp = Math.Min( 0.5f, sharp );
				}
				Cut( sharp * AttackDamage, true );
			}
		}

		// Later flesh cuts occur after the impact and must not impart the same impulse twice.
		contact = default;
		appliesImpact = false;
		if ( defender.IsValid() )
		{
			if ( !parried )
			{
				if ( defender.Health > 0f && (defender.Unconscious || defender.blockHealth <= 0f) )
				{
					ApplyStrikeImpulse( clip, position, direction, sharp );
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

	List<CharacterSurface.Cut> CutSurface( RabbitGuard victim, Vector3 point )
	{
		if ( strike.CutPlane is not { } cut || !victim.IsValid() ||
			CharacterLoader.FromModel( victim.Renderer.Model ) is not { } character ) return null;
		if ( flippedAttack ) cut.y *= -1f;
		var forward = WorldRotation.Forward;
		var right = new Vector3( forward.y, -forward.x, forward.z );
		float angle = GetAttackHeight( victim.GameObject ) * 0.5f;
		var tilt = Rotation.FromAxis( right, angle.RadianToDegree() );
		var normal = tilt * (forward * cut.x + right * cut.y + Vector3.Up * cut.z);
		if ( PrimaryItem.IsValid() && CharacterLoader.AverageChainFrame( victim.Renderer, "head" ) is { } head )
		{
			var along = normal.Cross( (tilt * Vector3.Up).Cross( normal ).Normal ).Normal;
			PrimaryItem.MarkCut( CharacterCenter( victim.GameObject ), head.Position, along * 0.8f + normal * 0.2f );
		}
		return character.Surface.CutFaces( victim.Renderer, normal, point, tilt * forward );
	}

	void EmitCutParticles( List<CharacterSurface.Cut> faces, bool light )
	{
		var clouds = new List<Vector3>();
		var splats = new List<Vector3>();
		foreach ( var face in faces )
		{
			if ( Random.Shared.Next( 10 ) == 0 ) clouds.Add( face.Center );
			if ( light && Random.Shared.Next( 40 ) == 0 ) splats.Add( face.Center );
		}
		if ( clouds.Count + splats.Count > 0 ) PlayCutParticles( clouds.ToArray(), splats.ToArray() );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayCutParticles( Vector3[] clouds, Vector3[] splats )
	{
		if ( BloodLevel == 0 || Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		foreach ( var point in clouds ) mount.EmitBillboard( "Data/Particles/bloodcloud.xml", point, ParticleScatter() * 40f, BloodColor );
		foreach ( var point in splats ) mount.EmitBillboard( "Data/Particles/bloodsplat.xml", point, ParticleScatter() * 80f, BloodColor );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayArmorImpact( Vector3 position, bool strong )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		mount.PlayGroup( $"Data/Sounds/weapon_foley/impact/weapon_metal_hit_metal{(strong ? "_strong" : "")}.xml", position );
		mount.MetalSparks( position, SecondaryPoseFrames );
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
