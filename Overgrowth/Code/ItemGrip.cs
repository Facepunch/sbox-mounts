using System;

namespace Overgrowth;

internal static class ItemGrip
{
	// LoadAttachPose samples the first animation key in model space. Centering
	// and yaw are common to both matrices and cancel in the hand-relative pose.
	internal static Transform Read( BaseFileSystem fs, string path, int bone, bool mirrored, bool blade, bool hip, float scale )
	{
		using var reader = new BinaryReader( fs.OpenRead( path ) );
		var (version, _, _, _, _) = RabbitClip.ReadHeader( reader, path );
		if ( reader.ReadInt32() < 1 || version < 7 ) throw new InvalidDataException( $"Missing item pose in {path}" );
		reader.ReadInt32();
		int weights = reader.ReadInt32();
		reader.BaseStream.Position += weights * 4L;
		int bones = reader.ReadInt32();
		if ( (uint)bone >= bones ) throw new InvalidDataException( $"Missing attachment bone {bone} in {path}" );
		var frames = reader.BaseStream.Position;
		var matrix = new float[16];
		Transform Bone( int index )
		{
			reader.BaseStream.Position = frames + 64L * index;
			return RabbitClip.ReadMatrix( reader, matrix );
		}
		var hand = Bone( bone );
		reader.BaseStream.Position = frames + 64L * bones;
		if ( reader.ReadInt32() < 1 ) throw new InvalidDataException( $"Missing item matrix in {path}" );
		var item = RabbitClip.ReadMatrix( reader, matrix );
		if ( version >= 8 )
		{
			int relative = reader.ReadInt32();
			if ( reader.ReadSingle() > 0f ) item = Bone( relative ).ToLocal( item );
		}

		if ( mirrored )
		{
			var original = item.Rotation;
			item = RabbitMirror.Reflect( item );
			var edge = hip ? Vector3.Down : new Vector3( 0f, -0.25f, -0.165f );
			var mirroredEdge = edge.WithY( -edge.y );
			if ( blade && Vector3.Dot( item.Rotation * original.Inverse * edge, mirroredEdge ) < 0f )
			{
				var x = item.Rotation * Vector3.Left;
				var y = item.Rotation * Vector3.Up;
				var z = item.Rotation * Vector3.Forward;
				var axis = MathF.Abs( y.x ) > MathF.Max( MathF.Abs( x.x ), MathF.Abs( z.x ) ) ? y
					: MathF.Abs( z.x ) > MathF.Abs( x.x ) ? z : x;
				item.Rotation = Rotation.FromAxis( axis, 180f ) * item.Rotation;
			}
		}

		var local = hand.ToLocal( item );
		local.Position *= scale;
		local.Rotation *= Rotation.FromAxis( Vector3.Left, 90f );
		return local;
	}
}
