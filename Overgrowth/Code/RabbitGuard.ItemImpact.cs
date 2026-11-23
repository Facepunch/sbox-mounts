using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[ConVar( "overgrowth_blood", Help = "Overgrowth blood effects. Set to 0 to disable." )]
	public static int BloodLevel
	{
		get;
		set { field = value; CutMarks.RefreshAppearance(); }
	} = 2;
	[ConVar( "overgrowth_blood_color", Help = "Tint for Overgrowth blood particles and decals." )]
	public static Color BloodColor
	{
		get;
		set { field = value; CutMarks.RefreshAppearance(); }
	} = new( 0.4f, 0f, 0f );

	sealed class ItemWound : BloodDamage { public Vector3 Impulse { get; init; } }
	sealed class ItemImpact : BluntDamage { public Vector3 Impulse { get; init; } }
	sealed class ExtractionDamage : BloodDamage { public Vector3 Impulse { get; init; } }

	void ExtractWeapon( LevelItem item, bool lift )
	{
		var renderer = item.StuckIn;
		var wounded = renderer.GetComponentInParent<RabbitGuard>();
		var physics = renderer.GetComponent<ModelPhysics>();
		var point = item.PhysicsPosition;
		item.Drop();
		PlayExtractionEffect( point, wounded.IsValid() && wounded.Health > 0f );
		var impulse = lift ? Vector3.Up * (1000f * 40f / 120f) : Vector3.Zero;
		if ( physics is { Active: true } )
		{
			physics.GetComponent<RagdollDamping>()?.Refresh();
			physics.GetComponent<RagdollInjury>()?.AdvanceInjuryAge( 6f );
		}
		if ( wounded.IsValid() && wounded.Health > 0f )
		{
			bool protectedByShield = wounded.boundAnimations?.Species is ("wolf" or "dog") && wounded.KnockoutShield > 1;
			wounded.KnockoutShield = protectedByShield ? wounded.KnockoutShield - 1 : 0;
			var damage = new ExtractionDamage { Damage = protectedByShield ? 10f : 10000f, Position = point,
				Attacker = wounded != this ? GameObject : null, Weapon = item.GameObject, Impulse = impulse };
			damage.Tags.Add( "slash" );
			damage.Tags.Add( "nevergib" );
			((IDamageable)wounded).OnDamage( damage );
			if ( !wounded.IsValid() || wounded.Health <= 0f ) return;
		}
		if ( lift && physics is { Active: true } ) ApplyHitImpulse( physics, point, impulse );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayExtractionEffect( Vector3 point, bool alive )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		mount.PlayGroup( "Data/Sounds/weapon_foley/pickup/knife_remove.xml", point, 0.5f );
		if ( BloodLevel == 0 ) return;
		mount.EmitBillboard( "Data/Particles/bloodcloud.xml", point, Vector3.Up * 40f, BloodColor );
		if ( alive ) mount.EmitBillboard( "Data/Particles/bloodsplat.xml", point, Vector3.Up * 40f, BloodColor );
	}

	internal void HitByItem( LevelItem item, GameObject attacker, Vector3 point, Vector3 velocity, float mass, string label, bool cut, bool embedded )
	{
		var force = (velocity - Animation.MoveVelocity) * mass;
		var impulse = force * (200f / 120f);
		float strength = force.Length / 40f;
		string species = boundAnimations?.Species;
		bool wasAwake = !Unconscious;
		blockVelocity += force.WithZ( 0f ) * 0.1f;
		if ( cut )
		{
			var sound = mass < 1f ? (embedded ? "weapon_foley/impact/weapon_knife_hit" : "weapon_foley/impact/weapon_knife_hit_neck") : mass < 3f ? "hit/hit_medium" : "hit/hit_hard";
			if ( embedded && label == "spear" ) sound = "hit/spear_hit_flesh";
			PlayCombatSound( $"Data/Sounds/{sound}.xml", point );
		}
		if ( wasAwake && species is ("dog" or "wolf") )
		{
			Speak( "hit" );
			ReportBodySound( HeardSound.Voice, 10f );
			startled = 1f;
			NoticeAttacker( attacker );
			if ( species == "wolf" ) return;
			if ( embedded ) KnockoutShield = Math.Max( 0, KnockoutShield - 1 );
		}
		if ( species == "dog" && label is not ("spear" or "big_sword") )
		{
			if ( Downed ) ApplyHitImpulse( ragdoll, point, impulse );
			if ( !wasAwake ) return;
			var group = boundAnimations.Reactions["Data/Attacks/reaction_medfront.xml"];
			string animation = group.AtSeverity( embedded ? 1f : Math.Min( 1f, strength / 20f ) );
			MirroredAction = MirroredStance;
			BeginReaction( attacker, WorldPosition - velocity, animation, boundAnimations.Clips[animation] );
			Speak( "hit" );
			return;
		}
		bool aggro = CanAttack( enemy );
		void Damage( DamageInfo damage, float amount )
		{
			damage.Damage = amount * 100f;
			damage.Position = point;
			damage.Origin = attacker.IsValid() ? attacker.WorldPosition : point;
			damage.Attacker = attacker;
			damage.Weapon = item.GameObject;
			damage.Tags.Add( "nevergib" );
			damage.Tags.Add( damage is BloodDamage ? "slash" : "blunt" );
			((IDamageable)this).OnDamage( damage );
		}
		if ( embedded )
		{
			if ( aggro && label == "knife" ) KnockoutShield = Math.Max( 0, KnockoutShield - 2 );
			Damage( new ItemWound { Impulse = impulse }, aggro && label == "knife" ? Random.Shared.Float( 0.6f, 1.4f ) : 2f );
		}
		else
		{
			Damage( new ItemImpact { Impulse = impulse }, strength / 30f );
			if ( cut && IsValid && Health > 0f ) Damage( new ItemWound { Impulse = impulse }, strength / 50f );
		}
		if ( !IsValid || Health < 1f ) return;
		if ( wasAwake ) ReportBodySound( HeardSound.Voice, 10f );
		if ( !Downed ) GoLimp();
		blockHealth = 0f;
		limpStun = 0.9f;
		ApplyHitImpulse( ragdoll, point, impulse );
		if ( wasAwake && Unconscious && boundAnimations?.FallPose is { Writhe: not null } pose )
			ragdoll.GetOrAddComponent<RagdollInjury>().StartInjury( ragdoll, pose, Random.Shared.Float( 0f, 12f ), true );
		else if ( wasAwake && !Unconscious ) Speak( "hit" );
	}
}
