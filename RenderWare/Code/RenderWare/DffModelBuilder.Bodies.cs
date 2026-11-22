using System;

namespace Renderware;

partial class DffModelBuilder
{
	/// <summary>
	/// A capsule body per bone along its geometry's bone local bounds, joined to its parent with a ball joint.
	/// </summary>
	void GenerateBodies( ModelBuilder builder )
	{
		var bodyIndex = new Dictionary<RigBone, int>();

		foreach ( var bone in rig )
		{
			if ( bone.Parent is null || bone.Bounds is not { } bounds )
				continue;

			var mins = bounds.Mins * Units.Scale;
			var maxs = bounds.Maxs * Units.Scale;
			float radius = MathF.Max( 1, MathF.Max( maxs.y - mins.y, maxs.z - mins.z ) * 0.5f );
			var center = (mins + maxs) * 0.5f;

			var body = builder.AddBody( boneName: bone.Name );
			body.AddCapsule( new Capsule( center.WithX( mins.x + radius ), center.WithX( MathF.Max( mins.x + radius, maxs.x - radius ) ), radius ) );
			bodyIndex[bone] = bodyIndex.Count;
		}

		foreach ( var (bone, childBody) in bodyIndex )
		{
			var parent = bone.Parent;
			while ( parent is not null && !bodyIndex.ContainsKey( parent ) )
				parent = parent.Parent;

			if ( parent is null )
				continue;

			var transform = new Transform( bone.Bind.Position * Units.Scale, bone.Bind.Rotation );
			var parentTransform = new Transform( parent.Bind.Position * Units.Scale, parent.Bind.Rotation );

			// todo: work out joint types and limits properly
			builder.AddBallJoint( bodyIndex[parent], childBody, parentTransform.ToLocal( transform ), Transform.Zero )
				.WithTwistLimit( -20, 20 )
				.WithSwingLimit( 90 );
		}
	}
}
