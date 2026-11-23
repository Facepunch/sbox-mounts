using Sandbox.Animation;

namespace Overgrowth;

// Bone-relative transforms for the primary weapon are part of the sampled pose.
// Give each reference bone its own channels so graph transitions can change hands.
internal sealed class WeaponPoseTrack( int bone, int frames )
{
	static readonly string[] Channels = ["weight", "x", "y", "z", "qx", "qy", "qz", "qw"];
	internal static string[] Names( int bone ) => Channels.Select( channel => $"item_{bone}_{channel}" ).ToArray();
	readonly float[][] values = Channels.Select( _ => new float[frames] ).ToArray();
	Rotation previous = Rotation.Identity;

	internal void Set( int frame, Transform pose )
	{
		var q = pose.Rotation.Normal;
		float dot = previous.x * q.x + previous.y * q.y + previous.z * q.z + previous.w * q.w;
		float sign = dot < 0f ? -1f : 1f;
		previous = new Rotation( q.x * sign, q.y * sign, q.z * sign, q.w * sign );
		values[0][frame] = 1f;
		values[1][frame] = pose.Position.x;
		values[2][frame] = pose.Position.y;
		values[3][frame] = pose.Position.z;
		values[4][frame] = previous.x;
		values[5][frame] = previous.y;
		values[6][frame] = previous.z;
		values[7][frame] = previous.w;
	}

	internal void Write( ClipBuilder clip )
	{
		var names = Names( bone );
		for ( int i = 0; i < names.Length; ++i )
			clip.AddFloatChannelTrack( $"item_bone_{bone}", names[i], values[i] );
	}
}
