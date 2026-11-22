namespace Overgrowth;

// Animation::Retarget mirrors skin transforms in the reference rabbit rig's bind space.
internal sealed class RabbitMirror
{
	readonly Transform[] bind;
	readonly Transform[] inverseBind;
	readonly int[] symmetry;
	readonly int[] order;

	internal RabbitMirror( SkeletonData source, float scale )
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
		symmetry = Enumerable.Range( 0, bind.Length ).ToArray();
		for ( int i = 0; i < bind.Length; i++ )
		{
			var (a, b) = source.Bones[i];
			for ( int j = i; j < bind.Length; j++ )
			{
				var (c, d) = source.Bones[j];
				// SkeletonAsset compares endpoints in the original file axes, in metres.
				if ( source.Points[a].DistanceSquared( source.Points[c] * new Vector3( -1f, 1f, 1f ) ) >= 0.0001f ||
					source.Points[b].DistanceSquared( source.Points[d] * new Vector3( -1f, 1f, 1f ) ) >= 0.0001f ) continue;
				symmetry[i] = j;
				symmetry[j] = i;
			}
		}
	}

	internal static Transform Reflect( Transform tx )
	{
		tx.Position = tx.Position.WithY( -tx.Position.y );
		var q = tx.Rotation;
		tx.Rotation = new Rotation( q.x, -q.y, q.z, -q.w );
		return tx;
	}

	internal void Apply( Transform[] frame, Transform[] skin, List<int> parents )
	{
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
