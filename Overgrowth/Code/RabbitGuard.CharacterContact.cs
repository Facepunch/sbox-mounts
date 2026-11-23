using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	const float ContactDistance = 0.7f * 40f;
	TimeUntil contactReady = 0.2f;
	[Sync( SyncFlags.Interpolate )] Vector3 ContactVelocity { get; set; }
	readonly HashSet<RabbitGuard> contactCandidates = new();

	bool CanTouchCharacters => !Downed && !Unconscious && !InThrow && Health > 0 && boundAnimations is not null && contactReady <= 0f;

	internal void UpdateCharacterContacts()
	{
		if ( !Networking.IsHost || !CanTouchCharacters ) return;
		ContactVelocity = Animation.IsGrounded ? ContactVelocity * MathF.Pow( 0.9f, Time.Delta * 120f ) : default;
		var center = CharacterCenter( GameObject );
		Vector3? massCenter = null;
		contactCandidates.Clear();
		foreach ( var hit in Scene.FindInPhysics( new Sphere( center, ContactDistance ) ) )
		{
			var other = hit.GetComponentInParent<RabbitGuard>();
			if ( !other.IsValid() || !other.CanTouchCharacters || other == holdPartner || Id.CompareTo( other.Id ) >= 0 ||
				StaticCharacter && other.StaticCharacter || !contactCandidates.Add( other ) ) continue;
			massCenter ??= ContactCenter();
			if ( massCenter is not { } here || other.ContactCenter() is not { } there ) continue;
			var delta = there - here;
			float distance = delta.Length;
			if ( distance >= ContactDistance || distance <= 0f ) continue;
			var offset = delta * (0.5f * (ContactDistance - distance) / distance);
			var direction = (other.CharacterCenter( other.GameObject ) - center).Normal;
			ReceiveCharacterContact( other, -offset, direction );
			other.ReceiveCharacterContact( this, offset, -direction );
			if ( !StaticCharacter ) massCenter -= offset;
		}
	}

	Vector3? ContactCenter()
	{
		if ( CharacterLoader.UprightCenterOfMass( this ) is not { } center ) return null;
		// Physics may separate the roots more than once between animation evaluations.
		if ( motionSamples > 0 ) center = Renderer.WorldTransform.PointToWorld( currentMotionRoot.PointToLocal( center ) );
		return center.WithZ( CharacterCenter( GameObject ).z );
	}

	void ReceiveCharacterContact( RabbitGuard other, Vector3 offset, Vector3 toward )
	{
		if ( StaticCharacter ) return;
		Hear( other.GameObject, CharacterCenter( GameObject ) + toward * (5f * 40f), HeardSound.Footstep, true );
		WorldPosition += offset;
		if ( GetComponent<NavMeshAgent>() is { Active: true } agent && !IsAirborne ) agent.SetAgentPosition( WorldPosition );
		ContactVelocity += offset * (120f * 0.15f);
	}
}
