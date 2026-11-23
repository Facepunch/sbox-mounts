using Sandbox.Animation;

namespace Overgrowth;

// Bone-relative transforms for the primary weapon are part of the sampled pose.
// Give each reference bone its own channels so graph transitions can change hands.
internal sealed class WeaponPoseTrack( int bone, int frames )
{
	static readonly string[] Channels = ["weight", "x", "y", "z", "qx", "qy", "qz", "qw"];
	internal static string[] Names( int bone ) => Channels.Select( channel => $"item_{bone}_{channel}" ).ToArray();
	readonly float[][] values = Channels.Select( _ => new float[frames] ).ToArray();

	internal void Set( int frame, Transform pose )
	{
		var q = pose.Rotation;
		values[0][frame] = 1f;
		values[1][frame] = pose.Position.x;
		values[2][frame] = pose.Position.y;
		values[3][frame] = pose.Position.z;
		values[4][frame] = q.x;
		values[5][frame] = q.y;
		values[6][frame] = q.z;
		values[7][frame] = q.w;
	}

	internal void Write( ClipBuilder clip )
	{
		// Float-channel interpolation needs adjacent samples in the same quaternion hemisphere.
		var previous = Rotation.Identity;
		for ( int i = 0; i < frames; i++ )
		{
			var q = new Rotation( values[4][i], values[5][i], values[6][i], values[7][i] );
			if ( previous.x * q.x + previous.y * q.y + previous.z * q.z + previous.w * q.w < 0f )
			{
				for ( int channel = 4; channel < 8; channel++ ) values[channel][i] = -values[channel][i];
				q = new Rotation( -q.x, -q.y, -q.z, -q.w );
			}
			previous = q;
		}
		var names = Names( bone );
		for ( int i = 0; i < names.Length; ++i )
			clip.AddFloatChannelTrack( $"item_bone_{bone}", names[i], values[i] );
	}
}
