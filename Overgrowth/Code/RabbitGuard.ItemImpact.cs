using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	sealed class ItemWound : BloodDamage { public Vector3 Impulse { get; init; } }
	sealed class ItemImpact : BluntDamage { public Vector3 Impulse { get; init; } }

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
			startled = 1f;
			NoticeAttacker( attacker );
			if ( species == "wolf" ) return;
			if ( embedded ) KnockoutShield = Math.Max( 0, KnockoutShield - 1 );
		}
		if ( species == "dog" && label is not ("spear" or "big_sword") )
		{
			var group = boundAnimations.Reactions["Data/Attacks/reaction_medfront.xml"];
			string animation = group.AtSeverity( embedded ? 1f : Math.Min( 1f, strength / 20f ) );
			MirroredAction = MirroredStance;
			BeginReaction( attacker, WorldPosition - velocity, animation, boundAnimations.Clips[animation] );
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
		if ( !Downed ) GoLimp();
		blockHealth = 0f;
		limpStun = 0.9f;
		ApplyHitImpulse( ragdoll, point, impulse );
	}
}
