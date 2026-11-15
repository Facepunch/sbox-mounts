using System;

namespace Source1;

/// <summary>Recovers a collapsed Source hand from the shared mounted hand mesh.</summary>
static class SourceHandPose
{
	// Keyed by resource name so the caches never root models past mount or scene teardown.
	static readonly Dictionary<(string, string, string), Transform?> CachedPoses = new();
	static readonly Dictionary<string, Dictionary<string, SourceModelBoneSurfaceData.Sample[]>> CachedSurfaces = new();
	readonly record struct Match( Vertex Reference, Vertex[] Candidates );

	/// <summary>A model's mounted v_hand vertices, read from the model once per hands configuration.</summary>
	public sealed class HandMesh( Model model )
	{
		public Model Model { get; } = model;
		Vertex[] _vertices;
		Dictionary<(int, int), Vertex[]> _byUv;
		public Vertex[] Vertices => _vertices ??= ReadHandVertices( Model ).ToArray();
		public Dictionary<(int, int), Vertex[]> ByUv => _byUv ??= Vertices.GroupBy( UvKey ).ToDictionary( g => g.Key, g => g.DistinctBy( v => v.Position ).ToArray() );
	}

	/// <summary>Fits the replacement palm to mounted knuckles without assuming identical wrist pivots.</summary>
	public static Transform? FitPalm( Vector3[] from, Vector3[] to )
	{
		if ( from.Length < 4 || from.Length != to.Length ) return null;
		var a = from.Aggregate( Vector3.Zero, (sum, p) => sum + p ) / from.Length;
		var b = to.Aggregate( Vector3.Zero, (sum, p) => sum + p ) / to.Length;
		var covariance = new float[3, 3];
		for ( var i = 0; i < from.Length; i++ )
		{
			var x = from[i] - a;
			var y = to[i] - b;
			float[] av = [x.x, x.y, x.z], bv = [y.x, y.y, y.z];
			for ( var j = 0; j < 3; j++ )
				for ( var k = 0; k < 3; k++ ) covariance[j, k] += av[j] * bv[k];
		}
		var s = covariance;
		var trace = s[0, 0] + s[1, 1] + s[2, 2];
		// Horn's quaternion fit. Shift the symmetric matrix so power iteration
		// selects its largest algebraic eigenvalue, not the largest magnitude.
		var n = new float[4, 4]
		{
			{ trace, s[1, 2] - s[2, 1], s[2, 0] - s[0, 2], s[0, 1] - s[1, 0] },
			{ s[1, 2] - s[2, 1], s[0, 0] - s[1, 1] - s[2, 2], s[0, 1] + s[1, 0], s[2, 0] + s[0, 2] },
			{ s[2, 0] - s[0, 2], s[0, 1] + s[1, 0], -s[0, 0] + s[1, 1] - s[2, 2], s[1, 2] + s[2, 1] },
			{ s[0, 1] - s[1, 0], s[2, 0] + s[0, 2], s[1, 2] + s[2, 1], -s[0, 0] - s[1, 1] + s[2, 2] }
		};
		var shift = 0f;
		for ( var i = 0; i < 4; i++ )
		{
			var row = 0f;
			for ( var j = 0; j < 4; j++ ) row += Math.Abs( n[i, j] );
			shift = Math.Max( shift, row );
		}
		if ( shift < 0.0001f ) return null;
		for ( var i = 0; i < 4; i++ ) n[i, i] += shift;
		float[] q = [1f, 0.17f, 0.23f, 0.31f];
		var next = new float[4];
		for ( var iteration = 0; iteration < 80; iteration++ )
		{
			Array.Clear( next );
			for ( var i = 0; i < 4; i++ )
				for ( var j = 0; j < 4; j++ ) next[i] += n[i, j] * q[j];
			var length = MathF.Sqrt( next.Sum( v => v * v ) );
			if ( length < 0.0001f ) return null;
			for ( var i = 0; i < 4; i++ ) q[i] = next[i] / length;
		}
		var rotation = new Rotation( q[1], q[2], q[3], q[0] );
		var numerator = 0f;
		var denominator = 0f;
		for ( var i = 0; i < from.Length; i++ )
		{
			numerator += (rotation * (from[i] - a)).Dot( to[i] - b );
			denominator += (from[i] - a).LengthSquared;
		}
		var scale = numerator / denominator;
		if ( !float.IsFinite( scale ) || scale < 0.5f || scale > 2f ) return null;
		return new Transform( b - rotation * a * scale, rotation, scale );
	}

	public static Transform? RecoverJoint( HandMesh mesh, HandMesh referenceMesh, string bone )
	{
		var model = mesh.Model;
		var reference = referenceMesh.Model;
		if ( CachedPoses.TryGetValue( (model.Name, reference.Name, bone), out var cached ) ) return cached;
		var joint = reference.Bones.GetBone( bone );
		if ( joint is null ) return null;
		if ( !CachedSurfaces.TryGetValue( reference.Name, out var surfaces ) )
		{
			surfaces = (reference.GetData<SourceModelBoneSurfaceData[]>() ?? []).ToDictionary( surface => surface.Bone, surface => surface.Samples );
			CachedSurfaces[reference.Name] = surfaces;
		}
		if ( surfaces.TryGetValue( bone, out var samples ) && samples.Length >= 6
			&& Fit( mesh, referenceMesh, joint.LocalTransform.Position, 1, samples ) is { } weighted )
		{
			CachedPoses[(model.Name, reference.Name, bone)] = weighted;
			return weighted;
		}
		var child = reference.Bones.AllBones.FirstOrDefault( b => b.Parent == joint );
		var length = child is not null ? child.LocalTransform.Position.Distance( joint.LocalTransform.Position )
			: joint.Parent is not null ? joint.LocalTransform.Position.Distance( joint.Parent.LocalTransform.Position ) : 0;
		// Fit the rigid middle of a finger segment, away from the skin blended
		// across its hinge. A neighborhood centered on the joint mixes two poses.
		var center = joint.LocalTransform.Position + joint.LocalTransform.Rotation.Forward * length * 0.5f;
		var uvs = mesh.ByUv;
		var distances = referenceMesh.Vertices.Where( v => uvs.ContainsKey( UvKey( v ) ) )
			.DistinctBy( v => v.Position ).Select( v => v.Position.Distance( center ) ).Order().Take( 12 ).ToArray();
		if ( distances.Length < 12 ) return null;
		return Recover( mesh, referenceMesh, bone, center, distances[^1] + 0.001f );
	}

	public static Transform? RecoverHand( HandMesh mesh, HandMesh referenceMesh, bool left )
	{
		var reference = referenceMesh.Model;
		var side = left ? "L" : "R";
		var hand = reference.Bones.GetBone( $"ValveBiped.Bip01_{side}_Hand" );
		var index = reference.Bones.GetBone( $"ValveBiped.Bip01_{side}_Finger1" );
		if ( hand is null || index is null ) return null;
		return Recover( mesh, referenceMesh, hand.Name, hand.LocalTransform.Position, hand.LocalTransform.Position.Distance( index.LocalTransform.Position ) * 0.75f );
	}

	public static Transform? RecoverForearm( HandMesh mesh, HandMesh referenceMesh, bool left )
	{
		var reference = referenceMesh.Model;
		var side = left ? "L" : "R";
		var arm = reference.Bones.GetBone( $"ValveBiped.Bip01_{side}_Forearm" );
		var hand = reference.Bones.GetBone( $"ValveBiped.Bip01_{side}_Hand" );
		if ( arm is null || hand is null ) return null;
		return Recover( mesh, referenceMesh, arm.Name, Vector3.Lerp( arm.LocalTransform.Position, hand.LocalTransform.Position, 0.5f ),
			arm.LocalTransform.Position.Distance( hand.LocalTransform.Position ) * 0.3f );
	}

	static Transform? Recover( HandMesh mesh, HandMesh referenceMesh, string bone, Vector3 center, float radius )
	{
		var key = (mesh.Model.Name, referenceMesh.Model.Name, bone);
		if ( CachedPoses.TryGetValue( key, out var cached ) ) return cached;
		var result = Fit( mesh, referenceMesh, center, radius );
		CachedPoses[key] = result;
		return result;
	}

	static Transform? Fit( HandMesh mesh, HandMesh referenceMesh, Vector3 center, float radius, SourceModelBoneSurfaceData.Sample[] samples = null )
	{
		var target = mesh.ByUv;
		var vertices = samples is null ? referenceMesh.Vertices
			: samples.Select( point => new Vertex { Position = point.Position, Normal = point.Normal, TexCoord0 = point.TexCoord } );
		var matches = vertices
			.Where( v => (samples is not null || v.Position.Distance( center ) < radius) && target.ContainsKey( UvKey( v ) ) )
			.DistinctBy( v => v.Position )
			.Select( v => new Match( v, target[UvKey( v )] ) ).ToArray();
		if ( matches.Length < (samples is null ? 12 : 6) ) return null;

		// UVs are shared by both hands. A proper rigid rotation can fit the right
		// wrist, but cannot mirror the left wrist into it. This also rejects a
		// model that only contains a left hand, instead of inventing a right one.
		var errors = new float[matches.Length];
		var bestError = float.MaxValue;
		Transform? best = null;
		for ( var trial = 0; trial < 500; trial++ )
		{
			var a = matches[Sample( trial, 0, matches.Length )];
			var b = matches[Sample( trial, 1, matches.Length )];
			var c = matches[Sample( trial, 2, matches.Length )];
			var minimumArea = radius * radius * 0.005f;
			if ( (b.Reference.Position - a.Reference.Position).Cross( c.Reference.Position - a.Reference.Position ).Length < minimumArea ) continue;
			var from = Frame( a.Reference.Position, b.Reference.Position, c.Reference.Position );
			var ab = a.Reference.Position.Distance( b.Reference.Position );
			var ac = a.Reference.Position.Distance( c.Reference.Position );
			var bc = b.Reference.Position.Distance( c.Reference.Position );
			var tolerance = samples is null ? radius * 0.25f : 0.08f;
			foreach ( var x in a.Candidates )
			foreach ( var y in b.Candidates )
			foreach ( var z in c.Candidates )
			{
				// A rigid correspondence preserves triangle lengths. Reject unrelated
				// UV islands before scoring the complete neighborhood.
				if ( Math.Abs( x.Position.Distance( y.Position ) - ab ) > tolerance
					|| Math.Abs( x.Position.Distance( z.Position ) - ac ) > tolerance
					|| Math.Abs( y.Position.Distance( z.Position ) - bc ) > tolerance ) continue;
				if ( (y.Position - x.Position).Cross( z.Position - x.Position ).Length < minimumArea ) continue;
				var rotation = Frame( x.Position, y.Position, z.Position ) * from.Inverse;
				var transform = new Transform( x.Position - rotation * a.Reference.Position, rotation );
				for ( var i = 0; i < matches.Length; i++ )
				{
					var position = transform.PointToWorld( matches[i].Reference.Position );
					var normal = rotation * matches[i].Reference.Normal;
					var error = float.MaxValue;
					foreach ( var candidate in matches[i].Candidates )
					{
						var normalError = 1 - normal.Dot( candidate.Normal ).Clamp( -1, 1 );
						error = Math.Min( error, position.DistanceSquared( candidate.Position ) + radius * radius * 0.25f * normalError * normalError );
					}
					errors[i] = error;
				}
				Array.Sort( errors );
				// Fingers and forearm skin can deform independently of the wrist.
				var count = (int)Math.Ceiling( errors.Length * 0.8f );
				var mean = 0f;
				for ( var i = 0; i < count; i++ ) mean += errors[i] / count;
				if ( mean >= bestError ) continue;
				bestError = mean;
				best = transform;
			}
		}
		return bestError < (samples is null ? radius * radius / 16f : 0.15f * 0.15f) ? best : null;
	}

	static int Sample( int trial, int point, int count )
	{
		unchecked
		{
			var value = (uint)(trial + 1) * 747796405u + (uint)(point + 1) * 2891336453u;
			value = (value ^ (value >> 16)) * 0x7feb352du;
			value = (value ^ (value >> 15)) * 0x846ca68bu;
			return (int)((value ^ (value >> 16)) % (uint)count);
		}
	}
	static (int, int) UvKey( Vertex vertex ) => ((int)MathF.Round( vertex.TexCoord0.x * 10000 ), (int)MathF.Round( vertex.TexCoord0.y * 10000 ));
	static Rotation Frame( Vector3 a, Vector3 b, Vector3 c ) => Rotation.LookAt( b - a, (b - a).Cross( c - a ) );

	static IEnumerable<Vertex> ReadHandVertices( Model model )
	{
		var vertices = model.GetVertices();
		if ( vertices is null || vertices.Length != model.MeshInfo.TotalVertices ) yield break;
		var start = 0;
		foreach ( var mesh in model.MeshInfo.Meshes )
		{
			// Mounted MDL geometry creates one mesh per material and body part.
			if ( (mesh.LodMask & 1) != 0 && mesh.DrawCalls.Length == 1 && mesh.DrawCalls[0].Material.Name.Contains( "/v_hand/" ) )
				for ( var i = 0; i < mesh.Vertices; i++ ) yield return vertices[start + i];
			start += mesh.Vertices;
		}
	}
}
