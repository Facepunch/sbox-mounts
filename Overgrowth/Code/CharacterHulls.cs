namespace Overgrowth;

internal static class CharacterHulls
{
	internal static void Add( HitboxSetBuilder set, BaseFileSystem fs, string path, Vector3 center, float scale,
		SkeletonData skeleton, Dictionary<int, RagdollBuilder.BoneCenter> bodyCenters )
	{
		var mesh = ObjLoader.Load( fs, path );
		var vertices = mesh.RawPositions.Select( v => (new Vector3( v.z, v.x, v.y ) * 40f - center) * scale ).ToArray();
		var parents = Enumerable.Range( 0, vertices.Length ).ToArray();
		int Root( int index )
		{
			while ( parents[index] != index )
			{
				parents[index] = parents[parents[index]];
				index = parents[index];
			}
			return index;
		}

		var indices = mesh.FaceVertPosIndices;
		for ( int i = 0; i < indices.Count; i += 3 )
		{
			var root = Root( indices[i] );
			parents[Root( indices[i + 1] )] = root;
			parents[Root( indices[i + 2] )] = root;
		}
		var groups = Enumerable.Range( 0, vertices.Length ).GroupBy( Root )
			.Select( group => group.Select( index => vertices[index] ).ToArray() ).ToArray();
		var centers = groups.Select( points => points.Aggregate( Vector3.Zero, (sum, p) => sum + p ) / points.Length ).ToArray();

		// Each eligible physics bone nominates its nearest piece. When two bones
		// nominate the same piece, the closer bone owns it.
		var owners = new Dictionary<int, (int Bone, float Distance)>();
		foreach ( var (bone, centerInfo) in bodyCenters )
		{
			if ( centerInfo.Fixed ) continue;
			var position = centerInfo.Position;
			int nearest = 0;
			for ( int i = 1; i < centers.Length; i++ )
				if ( position.DistanceSquared( centers[i] ) < position.DistanceSquared( centers[nearest] ) ) nearest = i;
			float distance = position.DistanceSquared( centers[nearest] );
			if ( !owners.TryGetValue( nearest, out var owner ) || distance < owner.Distance )
				owners[nearest] = (bone, distance);
		}

		foreach ( var (piece, owner) in owners )
		{
			var bone = $"bone_{owner.Bone}";
			var points = groups[piece];
			if ( points.Length < 4 ) continue;
			var bindPose = skeleton.BindPose[owner.Bone];
			for ( int i = 0; i < points.Length; i++ )
				points[i] = bindPose.PointToLocal( points[i] );

			set.AddHull( bone, bone, points ).WithSurface( "flesh" );
		}
	}
}
