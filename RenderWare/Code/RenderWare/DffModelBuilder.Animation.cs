using System;

namespace Renderware;

partial class DffModelBuilder
{
	const float AnimationFrameRate = 30; // the games' own key rate

	/// <summary>
	/// Animations to bake for the rig (rigged models only), see <see cref="AddAnimations"/>.
	/// </summary>
	public IReadOnlyList<AnimationClip> Animations { get; set; }

	/// <summary>
	/// Each clip resampled at 30 fps into parent local bone transforms. A track finds its frame by animation node id
	/// (Vice City / San Andreas) or by name (III); frames without one keep their file pose, rotation only tracks their
	/// file position. Poses are worked out on the frame hierarchy and taken in game space - the clump frame being where
	/// the game puts the ped's matrix - so they come out standing, facing +Y, whatever space the rig's bind pose is in.
	/// Clips that end where they start loop, with the root's walk forward taken out so they play in place.
	/// </summary>
	void AddAnimations( ModelBuilder builder )
	{
		if ( Animations is not { Count: > 0 } )
			return;

		var frames = dff.FrameList.Frames;
		var frameIndex = new Dictionary<Frame, int>();
		for ( int i = 0; i < frames.Length; i++ )
			frameIndex[frames[i]] = i;

		var byName = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		for ( int i = 0; i < frames.Length; i++ )
		{
			if ( frames[i].Name is { } n )
				byName.TryAdd( n.Trim(), i );
		}

		bool byNodeId = dff.FrameList.HierarchyRoot is not null;

		int FindFrame( AnimationTrack track )
		{
			if ( byNodeId && track.BoneId >= 0 && dff.FrameList.TryGetBone( track.BoneId, out var bone ) )
				return frameIndex[bone];
			return byName.TryGetValue( track.Name, out var i ) ? i : -1;
		}

		var parents = frames.Select( f => dff.FrameList.GetParent( f ) is { } p ? frameIndex[p] : -1 ).ToArray();
		var restLocal = frames.Select( ( f, i ) => parents[i] < 0 ? Transform.Zero : f.LocalTransform ).ToArray();

		foreach ( var clip in Animations )
		{
			var tracks = new AnimationTrack[frames.Length];
			foreach ( var track in clip.Tracks )
			{
				if ( track.Keys.Length > 0 && FindFrame( track ) is >= 0 and var f )
					tracks[f] ??= track;
			}

			if ( tracks.All( t => t is null ) )
				continue;

			int frameCount = Math.Max( 1, (int)MathF.Round( clip.Duration * AnimationFrameRate ) + 1 );

			// the topmost track that moves: the one the game takes the ped's movement from
			int mover = Enumerable.Range( 0, frames.Length )
				.Where( i => tracks[i]?.HasTranslation == true )
				.OrderBy( i => Depth( parents, i ) )
				.DefaultIfEmpty( -1 )
				.First();

			var poses = new Transform[frameCount][];
			for ( int n = 0; n < frameCount; n++ )
			{
				float time = n / AnimationFrameRate;
				var local = new Transform[frames.Length];
				for ( int i = 0; i < frames.Length; i++ )
					local[i] = tracks[i] is { } track ? Sample( track, time, restLocal[i].Position ) : restLocal[i];
				poses[n] = local;
			}

			bool looping = frameCount > 1 && Enumerable.Range( 0, frames.Length )
				.Where( i => tracks[i] is not null )
				.All( i => poses[0][i].Rotation.Distance( poses[^1][i].Rotation ) < 6 ); // degrees

			if ( looping && mover >= 0 )
				RemoveDrift( poses, mover );

			var animation = builder.AddAnimation( clip.Name, AnimationFrameRate ).WithLooping( looping );
			var world = new Transform[frames.Length];
			var boneFrames = new Transform[rig.Count];

			foreach ( var local in poses )
			{
				for ( int i = 0; i < frames.Length; i++ )
					world[i] = WorldOf( i );

				Transform WorldOf( int i ) => parents[i] < 0 ? local[i] : WorldOf( parents[i] ).ToWorld( local[i] );

				for ( int b = 0; b < rig.Count; b++ )
				{
					var bone = rig[b];
					var boneWorld = bone.Frame is not null ? world[frameIndex[bone.Frame]] : bone.Bind;
					var parentWorld = bone.Parent?.Frame is { } pf ? world[frameIndex[pf]] : bone.Parent?.Bind;
					var t = parentWorld is { } pw ? pw.ToLocal( boneWorld ) : boneWorld;
					boneFrames[b] = new Transform( t.Position * Units.Scale, t.Rotation );
				}

				animation.AddFrame( boneFrames.AsSpan() );
			}
		}
	}

	static int Depth( int[] parents, int i )
	{
		int depth = 0;
		for ( ; parents[i] >= 0; i = parents[i] ) depth++;
		return depth;
	}

	/// <summary>
	/// A track's local transform at <paramref name="time"/>, between the keys either side (held past the ends).
	/// </summary>
	static Transform Sample( AnimationTrack track, float time, Vector3 restPosition )
	{
		var keys = track.Keys;
		int next = 0;
		while ( next < keys.Length && keys[next].Time < time )
			next++;

		AnimationKey a, b;
		float t;
		if ( next == 0 ) { a = b = keys[0]; t = 0; }
		else if ( next >= keys.Length ) { a = b = keys[^1]; t = 0; }
		else
		{
			a = keys[next - 1];
			b = keys[next];
			float span = b.Time - a.Time;
			t = span > 0 ? (time - a.Time) / span : 0;
		}

		var rotation = Rotation.Slerp( a.Rotation, b.Rotation, t );
		var position = a.Translation is { } ta && b.Translation is { } tb ? Vector3.Lerp( ta, tb, t ) : restPosition;
		return new Transform( position, rotation );
	}

	/// <summary>
	/// Takes the root's ground plane travel out of a looping clip, spread evenly over it, so a walk plays on the spot.
	/// The mover's parent is the clump frame - game space - so x / y is the ground.
	/// </summary>
	static void RemoveDrift( Transform[][] poses, int mover )
	{
		var drift = poses[^1][mover].Position - poses[0][mover].Position;
		drift = drift.WithZ( 0 );

		for ( int n = 0; n < poses.Length; n++ )
		{
			float t = poses.Length > 1 ? n / (float)(poses.Length - 1) : 0;
			var pose = poses[n][mover];
			poses[n][mover] = pose.WithPosition( pose.Position - drift * t );
		}
	}
}
