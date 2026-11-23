using System;

namespace Overgrowth;

// Animation::Retarget mirrors skin transforms in the reference rabbit rig's bind space.
internal sealed class RabbitMirror
{
	readonly Transform[] bind;
	readonly Transform[] inverseBind;
	readonly int[] symmetry;
	readonly int[] order;
	readonly Transform[] bindChange;
	readonly bool symmetricHierarchy;

	internal RabbitMirror( SkeletonData source, float scale, int[] animationParents )
	{
		bind = source.BindPose.Select( tx => tx.WithPosition( tx.Position * scale ) ).ToArray();
		inverseBind = bind.Select( tx => tx.ToLocal( Transform.Zero ) ).ToArray();
		int Depth( int i )
		{
			int depth = 0;
			while ( (i = source.HierParents[i]) >= 0 ) depth++;
			return depth;
		}
		order = Enumerable.Range( 0, bind.Length ).OrderBy( Depth ).ToArray();
		symmetry = MatchBones( source );
		bindChange = Enumerable.Range( 0, bind.Length ).Select( i => Reflect( inverseBind[i] ).ToWorld( bind[symmetry[i]] ) ).ToArray();
		symmetricHierarchy = Enumerable.Range( 0, bind.Length ).All( i =>
			animationParents[symmetry[i]] == (animationParents[i] < 0 ? -1 : symmetry[animationParents[i]]) );
	}

	internal static int[] MatchBones( SkeletonData source )
	{
		var symmetry = Enumerable.Range( 0, source.Bones.Count ).ToArray();
		for ( int i = 0; i < symmetry.Length; i++ )
		{
			var (a, b) = source.Bones[i];
			for ( int j = i; j < symmetry.Length; j++ )
			{
				var (c, d) = source.Bones[j];
				// SkeletonAsset compares endpoints in the original file axes, in metres.
				if ( source.Points[a].DistanceSquared( source.Points[c] * new Vector3( -1f, 1f, 1f ) ) >= 0.0001f ||
					source.Points[b].DistanceSquared( source.Points[d] * new Vector3( -1f, 1f, 1f ) ) >= 0.0001f ) continue;
				symmetry[i] = j;
				symmetry[j] = i;
			}
		}
		return symmetry;
	}

	internal static string SwapSide( string label ) => label.Contains( "left" )
		? label.Replace( "left", "right" ) : label.Replace( "right", "left" );

	internal static Transform Reflect( Transform tx )
	{
		tx.Position = tx.Position.WithY( -tx.Position.y );
		var q = tx.Rotation;
		tx.Rotation = new Rotation( q.x, -q.y, q.z, -q.w );
		return tx;
	}

	internal int Opposite( int bone ) => symmetry[bone];
	internal Transform BindChange( int bone ) => bindChange[bone];

	internal Transform FootTarget( int anchor, Transform pose ) =>
		Reflect( pose.ToWorld( bind[anchor] ) ).ToWorld( inverseBind[symmetry[anchor]] );

	internal void Apply( Span<Transform> frame, Transform[] skin, int[] parents )
	{
		// On a symmetric hierarchy, the mirrored parent world transform cancels when returning to local space.
		if ( symmetricHierarchy )
		{
			for ( int i = 0; i < frame.Length; i++ )
			{
				var local = Reflect( frame[i] ).ToWorld( bindChange[i] );
				skin[symmetry[i]] = parents[i] < 0 ? local : bindChange[parents[i]].ToLocal( local );
			}
			skin.AsSpan().CopyTo( frame );
			return;
		}
		foreach ( int i in order )
		{
			if ( parents[i] >= 0 ) frame[i] = frame[parents[i]].ToWorld( frame[i] );
			skin[i] = Reflect( frame[i].ToWorld( inverseBind[i] ) );
		}
		for ( int i = 0; i < frame.Length; i++ )
			frame[i] = skin[symmetry[i]].ToWorld( bind[i] );
		for ( int j = order.Length - 1; j >= 0; j-- )
		{
			int i = order[j];
			if ( parents[i] >= 0 ) frame[i] = frame[parents[i]].ToLocal( frame[i] );
		}
	}
}
