using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	const float ContactDistance = 0.7f * 40f;
	TimeUntil contactReady = 0.2f;
	double lastContactDecay = double.NegativeInfinity;
	[Sync( SyncFlags.Interpolate )] Vector3 ContactVelocity { get; set; }
	readonly HashSet<RabbitGuard> contactCandidates = new();
	readonly HashSet<ModelPhysics> nearbyRagdolls = new();

	bool IgnoresCharacterContact => InThrow || FadingGrab || FinishingCaptureReaction || (Held || Holding) && !CaptureReactionEnded;
	bool CanTouchCharacters => !Downed && !Unconscious && !IgnoresCharacterContact && Health > 0 && boundAnimations is not null && contactReady <= 0f;

	void UpdateCharacterContacts()
	{
		if ( !Networking.IsHost || Downed || boundAnimations is null ) return;
		bool uprightContact = CanTouchCharacters;
		bool ragdollContact = !StaticCharacter && IsOnGround;
		if ( !uprightContact && !ragdollContact ) return;
		DecayContactVelocity();
		var center = CharacterCenter( GameObject );
		Vector3? massCenter = null;
		contactCandidates.Clear();
		nearbyRagdolls.Clear();
		foreach ( var hit in Scene.FindInPhysics( new Sphere( center, 40f + LegRadius ) ) )
		{
			var other = hit.GetComponentInParent<RabbitGuard>();
			if ( ragdollContact && hit.GetComponentInParent<ModelPhysics>() is { Active: true } physics &&
				(other is { Downed: true } || physics.GetComponent<CharacterRemains>().IsValid()) && nearbyRagdolls.Add( physics ) )
			{
				ReceiveRagdollContact( physics, center );
				if ( Downed ) return;
			}
			if ( !uprightContact || !other.IsValid() || !other.CanTouchCharacters || other == holdPartner || Id.CompareTo( other.Id ) >= 0 ||
				StaticCharacter && other.StaticCharacter || !contactCandidates.Add( other ) ) continue;
			// Overgrowth dispatches body contact only when the two character query spheres touch.
			var between = other.CharacterCenter( other.GameObject ) - center;
			if ( between.LengthSquared > 4f * CharacterQueryRadius * CharacterQueryRadius ) continue;
			massCenter ??= ContactCenter();
			if ( massCenter is not { } here || other.ContactCenter() is not { } there ) continue;
			var delta = there - here;
			float distance = delta.Length;
			if ( distance >= ContactDistance || distance <= 0f ) continue;
			var offset = delta * (0.5f * (ContactDistance - distance) / distance);
			var direction = between.Normal;
			ReceiveCharacterContact( other, -offset, direction );
			other.ReceiveCharacterContact( this, offset, -direction );
			if ( !StaticCharacter ) massCenter -= offset;
		}
	}

	void ReceiveRagdollContact( ModelPhysics physics, Vector3 center )
	{
		if ( CharacterLoader.RagdollCenter( physics ) is not { } position ||
			position.DistanceSquared( center ) > (40f + LegRadius) * (40f + LegRadius) ||
			(position - center).WithZ( 0f ).LengthSquared >= 16f * 16f ||
			CharacterLoader.RagdollVelocity( physics ) is not { } velocity || velocity.LengthSquared <= 80f * 80f ) return;

		var relative = (velocity - BodyVelocity - combatVelocity).WithZ( 0f );
		bool wolf = boundAnimations.Species == "wolf";
		if ( !wolf && velocity.LengthSquared > 120f * 120f && relative.LengthSquared > 160f * 160f )
		{
			EnterRagdoll( passive: true );
			return;
		}

		float frames = Time.Delta * 120f;
		ApplyHitImpulse( physics, center, relative * (-500f / 120f * frames), Vector3.Up );
		if ( !wolf ) combatVelocity += relative * frames;
	}

	Vector3? ContactCenter()
	{
		if ( CharacterLoader.UprightCenterOfMass( this ) is not { } center ) return null;
		// Physics may separate the roots more than once between animation evaluations.
		if ( motionSamples > 0 ) center = Renderer.WorldTransform.PointToWorld( currentMotionRoot.PointToLocal( center ) );
		return center.WithZ( CharacterCenter( GameObject ).z );
	}

	// Both partners receive their push together, even if one has yet to run its fixed update.
	void DecayContactVelocity()
	{
		if ( lastContactDecay == Time.NowDouble ) return;
		lastContactDecay = Time.NowDouble;
		ContactVelocity = Animation.IsGrounded ? ContactVelocity * MathF.Pow( 0.9f, Time.Delta * 120f ) : default;
	}

	void ReceiveCharacterContact( RabbitGuard other, Vector3 offset, Vector3 toward )
	{
		if ( StaticCharacter ) return;
		Hear( other.GameObject, CharacterCenter( GameObject ) + toward * (5f * 40f), HeardSound.Footstep, true );
		TranslateRoot( offset );
		DecayContactVelocity();
		ContactVelocity += offset * (120f * 0.15f);
	}
}
