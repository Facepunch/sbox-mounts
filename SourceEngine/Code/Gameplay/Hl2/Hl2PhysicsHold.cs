using System;
using Sandbox.Physics;

namespace Source1;

/// <summary>Moves a rigidbody towards a target transform through a control joint. Run it where the body is simulated.</summary>
sealed class Hl2PhysicsHold
{
	const float MaxLinearAcceleration = 700000.0f;

	Sandbox.Physics.ControlJoint _joint;
	PhysicsBody _body;
	Vector3 _massCenterOffset;

	public bool IsHolding => _joint is not null;

	/// <summary>Starts holding <paramref name="body"/> by <paramref name="grabPoint"/>, in world space. Does nothing while holding.</summary>
	public void Hold( Rigidbody body, Vector3 grabPoint, float damping = 4.0f )
	{
		if ( IsHolding ) return;

		var physicsBody = body.PhysicsBody;
		_body ??= new PhysicsBody( physicsBody.World ) { BodyType = PhysicsBodyType.Keyframed, AutoSleep = false };

		var bodyTransform = body.WorldTransform.WithScale( 1.0f );
		_massCenterOffset = physicsBody.LocalMassCenter - bodyTransform.PointToLocal( grabPoint );

		var point1 = new PhysicsPoint( _body );
		var point2 = new PhysicsPoint( physicsBody, physicsBody.LocalMassCenter );
		var maxForce = physicsBody.Mass * MaxLinearAcceleration;

		var inertia = physicsBody.Inertia;
		var maxTorque = MaxLinearAcceleration * MathF.Sqrt( physicsBody.Mass * MathF.Max( inertia.x, MathF.Max( inertia.y, inertia.z ) ) );

		_joint = PhysicsJoint.CreateControl( point1, point2 );
		_joint.LinearSpring = new PhysicsSpring( 32, damping, maxForce );
		_joint.AngularSpring = new PhysicsSpring( 64, damping, maxTorque );
	}

	public void Move( Transform target )
	{
		if ( _body is null ) return;
		_body.Transform = target.WithPosition( target.PointToWorld( _massCenterOffset ) );
	}

	public void Release()
	{
		_joint?.Remove();
		_joint = null;

		_body?.Remove();
		_body = null;
	}

	/// <summary>The yaw of a view rotation, so a held object doesn't tumble as the view pitches.</summary>
	public static Rotation Stabilize( Rotation rotation )
	{
		var forward = rotation.Forward.WithZ( 0 );
		var up = rotation.Up.WithZ( 0 );

		var heading = forward.LengthSquared >= up.LengthSquared ? forward : (rotation.Forward.z > 0 ? -up : up);
		return Rotation.FromYaw( MathX.RadianToDegree( MathF.Atan2( heading.y, heading.x ) ) );
	}
}
