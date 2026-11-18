namespace Renderware;

partial class DffModelBuilder
{
	/// <summary>
	/// A capsule body per bone from its geometry's bounds, joined to its parent with a ball joint.
	/// </summary>
	void GenerateBodies( ModelBuilder builder )
	{
		var bodyIndex = new Dictionary<string, int>();

		foreach ( var (frame, bone) in bones )
		{
			if ( bone.Name is null || bone.ParentName is null )
				continue;

			var extents = frame.GetLocalBounds().Size;
			float length = extents.Dot( Vector3.Forward );
			float radius = extents.Dot( Vector3.Left ) / 2.0f;

			var body = builder.AddBody( boneName: bone.Name );
			body.AddCapsule( new Capsule( Vector3.Zero, Vector3.Forward * (length * 0.5f), radius ) );
			bodyIndex[bone.Name] = bodyIndex.Count;
		}

		foreach ( var bone in bones.Values )
		{
			if ( bone.Name is null || bone.ParentName is null )
				continue;

			if ( !bodyIndex.TryGetValue( bone.ParentName, out var parentBody ) || !bodyIndex.TryGetValue( bone.Name, out var childBody ) )
				continue;

			var parent = bones.Values.First( b => b.Name == bone.ParentName );

			var transform = new Transform( bone.Position, Rotation.LookAt( bone.Rotation.Up, bone.Rotation.Forward ) );
			var parentTransform = new Transform( parent.Position, parent.Rotation );

			// todo: work out joint types and limits properly
			builder.AddBallJoint( parentBody, childBody, parentTransform.ToLocal( transform ), transform.ToLocal( transform ) )
				.WithTwistLimit( -20, 20 )
				.WithSwingLimit( 90 );
		}
	}
}
