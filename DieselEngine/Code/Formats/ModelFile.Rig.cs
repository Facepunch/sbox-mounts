namespace Diesel;

using Matrix = System.Numerics.Matrix4x4;
using Vec = System.Numerics.Vector3;

sealed partial class ModelFile
{
	public IEnumerable<(uint Id, ulong Name, uint Parent)> RigNodes => nodes.Select( n => (n.Key, n.Value.Name, n.Value.Parent) );
	public sealed record Skin( uint[] Bones, Matrix[] InverseBind, ushort[] Indices, float[] Weights );
	Skin ReadSkin( Section model, int offset, ushort[] indices, float[] weights )
	{
		CheckSection( model, offset, 4 );
		uint id = data.UInt( offset );
		if ( id == 0 ) return null;
		var section = Get( id, 0x65cc1825 );
		int p = section.Start, mappings = data.Int( p ); p += 4;
		uint[] mapping = null;
		if ( mappings < 1 || mappings > 256 ) throw new InvalidDataException( "Invalid skin mappings." );
		for ( int i = 0; i < mappings; i++ )
		{
			int count = data.Int( p ); p += 4;
			CheckSection( section, p, (long)count * 4 );
			if ( i == 0 ) mapping = Enumerable.Range( 0, count ).Select( j => data.UInt( p + j * 4 ) ).ToArray();
			p += count * 4;
		}
		int bones = data.Int( p + 4 ); p += 8;
		CheckSection( section, p, (long)bones * 68 + 64 );
		var boneIds = new uint[mapping.Length]; var inverse = new Matrix[mapping.Length];
		for ( int i = 0; i < mapping.Length; i++ )
		{
			if ( mapping[i] >= bones ) throw new InvalidDataException( "Invalid skin bone mapping." );
			boneIds[i] = data.UInt( p + (int)mapping[i] * 4 );
			if ( !nodes.ContainsKey( boneIds[i] ) ) throw new InvalidDataException( "Missing skin bone." );
			inverse[i] = ReadMatrix( p + bones * 4 + (int)mapping[i] * 64 );
		}
		for ( int i = 0; i < indices.Length; i++ )
			if ( !float.IsFinite( weights[i] ) || weights[i] < 0 || weights[i] > 1.001f || (weights[i] > 0 && indices[i] >= boneIds.Length) )
				throw new InvalidDataException( "Invalid skin influence." );
		for ( int i = 0; i < weights.Length; i += 4 )
			if ( MathF.Abs( weights[i] + weights[i + 1] + weights[i + 2] + weights[i + 3] - 1 ) > 0.001f )
				throw new InvalidDataException( "Skin weights are not normalized." );
		return new( boneIds, inverse, indices, weights );
	}
	Matrix ReadMatrix( int p ) => new(
		data.Float(p),data.Float(p+4),data.Float(p+8),data.Float(p+12),
		data.Float(p+16),data.Float(p+20),data.Float(p+24),data.Float(p+28),
		data.Float(p+32),data.Float(p+36),data.Float(p+40),data.Float(p+44),
		data.Float(p+48),data.Float(p+52),data.Float(p+56),data.Float(p+60) );

	public Dictionary<uint, Matrix> Pose( AnimationFile clip, float time, Dictionary<ulong, AnimationFile.Track> embedded = null, bool preserveBoneLengths = false )
	{
		var pose = new Dictionary<uint, Matrix>();
		var active = new HashSet<uint>();
		Matrix Evaluate( uint id )
		{
			if ( pose.TryGetValue( id, out var cached ) ) return cached;
			if ( !active.Add( id ) || !nodes.TryGetValue( id, out var node ) ) throw new InvalidDataException( "Invalid rig hierarchy." );
			var local = clip?.Tracks.TryGetValue( node.Name, out var track ) == true ? track.Sample( time, node.Matrix ) : node.Matrix;
			// Shared character clips can contain another body's proportions (notably male walk/
			// surrender on female rigs). Retarget local offsets before composing the hierarchy.
			// Keep root/hip travel; all child joints retain the target skeleton's segment lengths.
			if ( preserveBoneLengths && node.Parent != 0 && node.Name != IdString.Hash( "Hips" ) && node.Name != IdString.Hash( "root_point" ) )
			{
				var offset = local.Translation;
				local.Translation = offset.LengthSquared() > 0.000001f
					? Vec.Normalize( offset ) * node.Matrix.Translation.Length() : node.Matrix.Translation;
			}
			if ( embedded?.TryGetValue( node.Name, out var controller ) == true ) local = controller.Sample( time, local );
			var world = node.Parent == 0 ? local : local * Evaluate( node.Parent );
			active.Remove( id );
			return pose[id] = world;
		}
		foreach ( uint id in nodes.Keys ) Evaluate( id );
		return pose;
	}
	public Dictionary<ulong, AnimationFile.Track> Controllers( HashSet<ulong> enabled )
		=> ControllerTracks( enabled, includeDescendants: true );

	/// <summary>Object animation groups list their controlled nodes explicitly; weapon helpers can include descendants.</summary>
	public Dictionary<ulong, AnimationFile.Track> ControllerTracks( HashSet<ulong> enabled, bool includeDescendants )
	{
		var result = new Dictionary<ulong, AnimationFile.Track>();
		foreach ( var (id, node) in nodes )
		{
			if ( !includeDescendants && !enabled.Contains( node.Name ) ) continue;
			uint ancestor = id;
			int depth = 0;
			while ( ancestor != 0 && !enabled.Contains( nodes[ancestor].Name ) )
			{
				if ( ++depth > nodes.Count || !nodes.TryGetValue( ancestor, out var parent ) ) throw new InvalidDataException( "Invalid controller hierarchy." );
				ancestor = parent.Parent;
			}
			if ( ancestor == 0 ) continue;
			var section = sections[id];
			int count = data.Int( section.Start + 8 );
			float[] pt = [], rt = []; Vec[] positions = []; System.Numerics.Quaternion[] rotations = [];
			for ( int i = 0; i < count; i++ )
			{
				uint controller = data.UInt( section.Start + 12 + i * 12 );
				if ( controller == 0 ) continue;
				if ( !sections.TryGetValue( controller, out var c ) ) throw new InvalidDataException( "Missing animation controller." );
				int keys = data.Int( c.Start + 20 );
				int stride = c.Type == 0x648a206c ? 20 : c.Type == 0x26a5128c ? 16 : throw new InvalidDataException( "Unsupported weapon controller." );
				CheckSection( c, c.Start + 24, (long)keys * stride );
				var times = new float[keys];
				for ( int k = 0; k < keys; k++ )
				{
					times[k] = data.Float( c.Start + 24 + k * stride );
					// Some revolver controllers include negative-time preroll keys.
					if ( !float.IsFinite( times[k] ) || (k > 0 && times[k] <= times[k - 1]) )
						throw new InvalidDataException( $"Invalid controller {controller} timestamp {k}: {times[k]} (previous {(k > 0 ? times[k - 1] : 0)})." );
				}
				if ( stride == 16 )
				{
					pt = times; positions = new Vec[keys];
					for ( int k = 0; k < keys; k++ )
					{
						int p = c.Start + 28 + k * stride;
						positions[k] = new( data.Float( p ), data.Float( p + 4 ), data.Float( p + 8 ) );
					}
				}
				else
				{
					rt = times; rotations = new System.Numerics.Quaternion[keys];
					for ( int k = 0; k < keys; k++ )
					{
						int p = c.Start + 28 + k * stride;
						rotations[k] = System.Numerics.Quaternion.Normalize( new( data.Float( p ), data.Float( p + 4 ), data.Float( p + 8 ), data.Float( p + 12 ) ) );
					}
				}
			}
			if ( pt.Length + rt.Length > 0 ) result.Add( node.Name, new( pt, positions, rt, rotations ) );
		}
		return result;
	}
	public Matrix NodePose( Dictionary<uint, Matrix> pose, string name ) => NodePose( pose, IdString.Hash( name ) );
	public Matrix NodePose( Dictionary<uint, Matrix> pose, ulong name )
	{
		var node = nodes.FirstOrDefault( n => n.Value.Name == name );
		if ( node.Key == 0 ) throw new InvalidDataException( $"Missing rig node {name:x}." );
		return pose[node.Key];
	}
	public static Transform Convert( Matrix matrix )
	{
		var p = matrix.Translation;
		var f = Vec.TransformNormal( Vec.UnitY, matrix );
		var u = Vec.TransformNormal( Vec.UnitZ, matrix );
		return new( Coordinates.Position( new Vector3( p.X, p.Y, p.Z ) ),
			Rotation.LookAt( new Vector3( f.Y, -f.X, f.Z ), new Vector3( u.Y, -u.X, u.Z ) ) );
	}
	public void Deform( Geometry geometry, Dictionary<uint, Matrix> pose, Matrix root, Vector3[] positions, Vector3[] normals )
	{
		var rigid = NodePose( pose, geometry.Name ) * root;
		var skin = geometry.Skin;
		var matrices = skin?.Bones.Select( (bone, i) => skin.InverseBind[i] * pose[bone] * root ).ToArray();
		for ( int v = 0; v < positions.Length; v++ )
		{
			var src = geometry.Positions[v]; var normal = geometry.Normals[v];
			var p = new Vec( src.x, src.y, src.z ); var n = new Vec( normal.x, normal.y, normal.z );
			Vec target = Vec.Zero, targetNormal = Vec.Zero;
			if ( skin is null ) { target = Vec.Transform( p, rigid ); targetNormal = Vec.TransformNormal( n, rigid ); }
			else
				for ( int j = 0; j < 4; j++ )
				{
					float w = skin.Weights[v * 4 + j]; if ( w <= 0 ) continue;
					var m = matrices[skin.Indices[v * 4 + j]];
					target += Vec.Transform( p, m ) * w;
					targetNormal += Vec.TransformNormal( n, m ) * w;
				}
			positions[v] = Coordinates.Position( new Vector3( target.X, target.Y, target.Z ) );
			normals[v] = new Vector3( targetNormal.Y, -targetNormal.X, targetNormal.Z ).Normal;
		}
	}
}
