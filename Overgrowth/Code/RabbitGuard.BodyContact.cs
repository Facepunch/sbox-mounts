using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Property, Range( 0f, 10f )] public float FallDamageMultiplier { get; set; } = 1f;
	TimeSince sinceRagdollImpact;
	sealed class FallImpactDamage : BluntDamage;

	internal void OnRagdollContact( Collision collision, bool head )
	{
		if ( !Networking.IsHost || !Downed || collision.Other.IsTrigger ) return;
		var other = collision.Other.GameObject;
		if ( !other.IsValid() ) return;
		var fighter = other.GetComponentInParent<RabbitGuard>();
		if ( fighter == this ) return;
		float impulse = BodyImpactImpulse( collision ) / 40f;
		var point = collision.Contact.Point;
		if ( !IsValid || Health <= 0f ) return;
		if ( impulse <= 0.1f && !awaitingThrowImpact ) return;
		var surface = TerrainLoader.ContactSurface( collision.Other.Collider as ModelCollider, point, collision.Other.Surface )?.ResourceName;
		float hardness = (Sandbox.Mounting.Directory.Get( "overgrowth" ) as GameMount)?.MaterialHardness( surface ) ?? 1f;
		if ( impulse * hardness * (head ? 5f : 1f) > 5f )
		{
			float multiplier = Math.Max( 0.001f, FallDamageMultiplier );
			KnockoutShield = Math.Max( 0, KnockoutShield - (int)((float)sinceRagdollImpact * 2f * multiplier) );
			sinceRagdollImpact = 0f;
			bool awake = !Unconscious;
			((IDamageable)this).OnDamage( new FallImpactDamage { Damage = impulse * 0.5f * multiplier, Position = point } );
			if ( !IsValid || Health <= 0f ) return;
			if ( awake && Unconscious ) PlayCombatSound( "Data/Sounds/hit/hit_medium_juicy.xml", point );
		}
		if ( bodyDragger.IsValid() || (impulse <= 1f && !awaitingThrowImpact) ) return;
		var center = CharacterCenter( GameObject );
		if ( WaterDepthAt( center - Vector3.Up * LegRadius ) >= 0.2f ) return;
		if ( awaitingThrowImpact && fighter is null )
		{
			ApplyThrowImpact( point );
			if ( !IsValid || Health <= 0f ) return;
		}

		// Collided shares these cooldowns with upright landings, but thresholds are in impulse units.
		if ( impulse > 4f && bodyFall <= 0f )
		{
			bodyFall = 0.6f;
			PlayMaterialEvent( "bodyfall", point, surface, 1f );
		}
		else if ( impulse > 3f && heavyFall <= 0f )
		{
			heavyFall = 0.2f;
			PlayMaterialEvent( "bodyfall_heavy", point, surface, 0.8f );
			if ( bodyFall <= 0f )
			{
				bodyFall = 0.6f;
				PlayMaterialEvent( "bodyfall", point, surface, 1f );
			}
		}
		else if ( impulse > 2f && mediumFall <= 0f )
		{
			mediumFall = 0.2f;
			PlayMaterialEvent( "bodyfall_medium", point, surface, 0.6f );
		}
		else if ( impulse > 1f && lightFall <= 0f )
		{
			lightFall = 0.2f;
			PlayMaterialEvent( "bodyfall_light", point, surface, 0.4f );
		}
		else return;
		ReportSound( GameObject, center, HeardSound.LoudFoley, 5f );
	}

	// Box3D's begin-touch notification can contain just the last solver substep's impulse.
	// Recover the normal impulse required to arrest the initial approach at this contact.
	internal static float BodyImpactImpulse( Collision collision )
	{
		var contact = collision.Contact;
		float inverseMass = InverseContactMass( collision.Self.Body ) + InverseContactMass( collision.Other.Body );
		return inverseMass > 0f ? Math.Max( contact.Impulse, contact.NormalSpeed / inverseMass ) : contact.Impulse;

		float InverseContactMass( PhysicsBody body )
		{
			if ( !body.IsValid() || !body.MotionEnabled || body.Mass <= 0f ) return 0f;
			var arm = Vector3.Cross( contact.Point - body.MassCenter, contact.Normal );
			var axis = (body.Rotation * body.InertiaRotation).Inverse * arm;
			var inertia = body.Inertia;
			return 1f / body.Mass + Angular( axis.x, inertia.x ) + Angular( axis.y, inertia.y ) + Angular( axis.z, inertia.z );
		}
		static float Angular( float axis, float inertia ) => inertia > 0f ? axis * axis / inertia : 0f;
	}
}

internal sealed class RabbitBodyContact : Component, Component.ICollisionListener
{
	internal RabbitGuard Fighter { get; set; }
	internal bool Head { get; set; }

	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( Fighter.IsValid() ) Fighter.OnRagdollContact( collision, Head );
	}
}
