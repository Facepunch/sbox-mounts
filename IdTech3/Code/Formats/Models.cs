using System.Text;

namespace IdTech3.Formats;

public struct ModelVertex
{
	public Vector3 Position;
	public Vector3 Normal;
	public Vector2 TexCoord;
	/// <summary>Up to 4 bone influences (index, weight). Unused for vertex-animated models.</summary>
	public int B0, B1, B2, B3;
	public float W0, W1, W2, W3;
}

public sealed class ModelSurface
{
	public string Name;
	/// <summary>Shader / material name (no extension), may be empty.</summary>
	public string Shader;
	public List<ModelVertex> Vertices = new();
	public List<int> Indices = new();
}

public struct ModelTag
{
	public string Name;
	public Vector3 Origin;
	public Axis Axis;
}

public sealed class ModelBone
{
	public string Name;
	public int Parent = -1;
	/// <summary>Model-space bind pose.</summary>
	public Vector3 Position;
	/// <summary>Model-space rotation quaternion (x, y, z, w).</summary>
	public float Qx, Qy, Qz, Qw = 1;
}

public sealed class ModelData
{
	public string Name;
	public List<ModelSurface> Surfaces = new();
	public List<ModelTag> Tags = new();
	public List<ModelBone> Bones = new();
	public int FrameCount = 1;
	public List<string> FrameNames = new();

	public ModelTag? FindTag( string name )
	{
		foreach ( var t in Tags ) if ( t.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) ) return t;
		return null;
	}

	/// <summary>Apply a rigid transform to all geometry and tags (used when attaching player parts).</summary>
	public void Transform( Vector3 origin, Axis axis )
	{
		foreach ( var s in Surfaces )
			for ( int i = 0; i < s.Vertices.Count; i++ )
			{
				var v = s.Vertices[i];
				v.Position = origin + axis.Transform( v.Position );
				v.Normal = axis.Transform( v.Normal );
				s.Vertices[i] = v;
			}
		for ( int i = 0; i < Tags.Count; i++ )
		{
			var t = Tags[i];
			t.Origin = origin + axis.Transform( t.Origin );
			t.Axis = axis.Multiply( t.Axis );
			Tags[i] = t;
		}
	}
}

public static class ModelReader
{
	public static ModelData Load( byte[] data, string name, int frame = 0 )
	{
		if ( data is null || data.Length < 8 ) return null;
		var magic = Encoding.ASCII.GetString( data, 0, 4 );
		return magic switch
		{
			"IDP3" => Md3.Load( data, name, frame ),
			"IDPC" => Mdc.Load( data, name, frame ),
			"MDSW" => Mds.Load( data, name, frame ),
			_ => null,
		};
	}

	internal static Vector3 DecodeLatLong( int packed )
	{
		float lat = ((packed >> 8) & 0xff) * (MathF.PI * 2 / 256f);
		float lng = (packed & 0xff) * (MathF.PI * 2 / 256f);
		return new Vector3( MathF.Cos( lat ) * MathF.Sin( lng ), MathF.Sin( lat ) * MathF.Sin( lng ), MathF.Cos( lng ) );
	}
}

/// <summary>Quake 3 MD3 (IDP3 v15) vertex-animated model.</summary>
public static class Md3
{
	public static ModelData Load( byte[] data, string name, int frame = 0 )
	{
		var b = new Bin( data );
		int numFrames = b.I32( 76 ), numTags = b.I32( 80 ), numSurfaces = b.I32( 84 );
		int ofsFrames = b.I32( 92 ), ofsTags = b.I32( 96 ), ofsSurfaces = b.I32( 100 );
		if ( numFrames <= 0 ) return null;
		frame = Math.Clamp( frame, 0, numFrames - 1 );

		var model = new ModelData { Name = name, FrameCount = numFrames };
		for ( int i = 0; i < numFrames; i++ ) model.FrameNames.Add( b.Str( ofsFrames + i * 56 + 40, 16 ) );

		for ( int i = 0; i < numTags; i++ )
		{
			int o = ofsTags + (frame * numTags + i) * 112;
			model.Tags.Add( new ModelTag
			{
				Name = b.Str( o, 64 ),
				Origin = b.V3( o + 64 ),
				Axis = new Axis { Forward = b.V3( o + 76 ), Left = b.V3( o + 88 ), Up = b.V3( o + 100 ) },
			} );
		}

		int so = ofsSurfaces;
		for ( int s = 0; s < numSurfaces && b.Has( so, 108 ); s++ )
		{
			int sFrames = b.I32( so + 72 ), numShaders = b.I32( so + 76 ), numVerts = b.I32( so + 80 ), numTris = b.I32( so + 84 );
			int ofsTris = b.I32( so + 88 ), ofsShaders = b.I32( so + 92 ), ofsSt = b.I32( so + 96 ), ofsXyz = b.I32( so + 100 ), ofsEnd = b.I32( so + 104 );
			int f = Math.Clamp( frame, 0, Math.Max( 0, sFrames - 1 ) );

			var surf = new ModelSurface
			{
				Name = b.Str( so + 4, 64 ),
				Shader = numShaders > 0 ? PathUtil.Normalize( PathUtil.StripExtensionIfImage( b.Str( so + ofsShaders, 64 ) ) ) : string.Empty,
			};

			for ( int v = 0; v < numVerts; v++ )
			{
				int xo = so + ofsXyz + (f * numVerts + v) * 8;
				surf.Vertices.Add( new ModelVertex
				{
					Position = new Vector3( b.I16( xo ), b.I16( xo + 2 ), b.I16( xo + 4 ) ) * (1f / 64f),
					Normal = ModelReader.DecodeLatLong( b.U16( xo + 6 ) ),
					TexCoord = b.V2( so + ofsSt + v * 8 ),
					W0 = 1,
				} );
			}
			for ( int t = 0; t < numTris; t++ )
			{
				int to = so + ofsTris + t * 12;
				int i0 = b.I32( to ), i1 = b.I32( to + 4 ), i2 = b.I32( to + 8 );
				if ( (uint)i0 >= numVerts || (uint)i1 >= numVerts || (uint)i2 >= numVerts ) continue;
				surf.Indices.Add( i0 ); surf.Indices.Add( i1 ); surf.Indices.Add( i2 );
			}
			model.Surfaces.Add( surf );

			if ( ofsEnd <= 0 ) break;
			so += ofsEnd;
		}
		return model;
	}
}

/// <summary>RTCW compressed MD3 (IDPC v2).</summary>
public static class Mdc
{
	public static ModelData Load( byte[] data, string name, int frame = 0 )
	{
		var b = new Bin( data );
		int numFrames = b.I32( 76 ), numTags = b.I32( 80 ), numSurfaces = b.I32( 84 );
		int ofsFrames = b.I32( 92 ), ofsTagNames = b.I32( 96 ), ofsTags = b.I32( 100 ), ofsSurfaces = b.I32( 104 );
		if ( numFrames <= 0 ) return null;
		frame = Math.Clamp( frame, 0, numFrames - 1 );

		var model = new ModelData { Name = name, FrameCount = numFrames };
		for ( int i = 0; i < numFrames; i++ ) model.FrameNames.Add( b.Str( ofsFrames + i * 56 + 40, 16 ) );

		const float tagAngleScale = 360f / 32700f;
		for ( int i = 0; i < numTags; i++ )
		{
			int o = ofsTags + (frame * numTags + i) * 12;
			var origin = new Vector3( b.I16( o ), b.I16( o + 2 ), b.I16( o + 4 ) ) * (1f / 64f);
			var ang = new Vector3( b.I16( o + 6 ), b.I16( o + 8 ), b.I16( o + 10 ) ) * tagAngleScale;
			model.Tags.Add( new ModelTag { Name = b.Str( ofsTagNames + i * 64, 64 ), Origin = origin, Axis = Axis.FromAngles( ang.x, ang.y, ang.z ) } );
		}

		int so = ofsSurfaces;
		for ( int s = 0; s < numSurfaces && b.Has( so, 124 ); s++ )
		{
			int numComp = b.I32( so + 72 ), numBase = b.I32( so + 76 ), numShaders = b.I32( so + 80 );
			int numVerts = b.I32( so + 84 ), numTris = b.I32( so + 88 );
			int ofsTris = b.I32( so + 92 ), ofsShaders = b.I32( so + 96 ), ofsSt = b.I32( so + 100 );
			int ofsXyz = b.I32( so + 104 ), ofsComp = b.I32( so + 108 );
			int ofsBaseFrames = b.I32( so + 112 ), ofsCompFrames = b.I32( so + 116 ), ofsEnd = b.I32( so + 120 );

			int baseFrame = Math.Clamp( (int)b.I16( so + ofsBaseFrames + frame * 2 ), 0, Math.Max( 0, numBase - 1 ) );
			int compFrame = numComp > 0 ? b.I16( so + ofsCompFrames + frame * 2 ) : -1;

			var surf = new ModelSurface
			{
				Name = b.Str( so + 4, 64 ),
				Shader = numShaders > 0 ? PathUtil.Normalize( PathUtil.StripExtensionIfImage( b.Str( so + ofsShaders, 64 ) ) ) : string.Empty,
			};

			for ( int v = 0; v < numVerts; v++ )
			{
				int xo = so + ofsXyz + (baseFrame * numVerts + v) * 8;
				var pos = new Vector3( b.I16( xo ), b.I16( xo + 2 ), b.I16( xo + 4 ) ) * (1f / 64f);
				if ( compFrame >= 0 && compFrame < numComp )
				{
					uint c = b.U32( so + ofsComp + (compFrame * numVerts + v) * 4 );
					pos += new Vector3( ((c & 255) - 127f) * 0.05f, (((c >> 8) & 255) - 127f) * 0.05f, (((c >> 16) & 255) - 127f) * 0.05f );
				}
				surf.Vertices.Add( new ModelVertex
				{
					Position = pos,
					Normal = ModelReader.DecodeLatLong( b.U16( xo + 6 ) ),
					TexCoord = b.V2( so + ofsSt + v * 8 ),
					W0 = 1,
				} );
			}
			for ( int t = 0; t < numTris; t++ )
			{
				int to = so + ofsTris + t * 12;
				int i0 = b.I32( to ), i1 = b.I32( to + 4 ), i2 = b.I32( to + 8 );
				if ( (uint)i0 >= numVerts || (uint)i1 >= numVerts || (uint)i2 >= numVerts ) continue;
				surf.Indices.Add( i0 ); surf.Indices.Add( i1 ); surf.Indices.Add( i2 );
			}
			model.Surfaces.Add( surf );

			if ( ofsEnd <= 0 ) break;
			so += ofsEnd;
		}
		return model;
	}
}

/// <summary>RTCW skeletal model (MDSW v4). Posed at a single frame; bones are exported in that pose.</summary>
public static class Mds
{
	struct BoneXform { public Axis Axis; public Vector3 Translation; }

	public static ModelData Load( byte[] data, string name, int frame = 0 )
	{
		var b = new Bin( data );
		int numFrames = b.I32( 80 ), numBones = b.I32( 84 ), ofsFrames = b.I32( 88 ), ofsBones = b.I32( 92 );
		int numSurfaces = b.I32( 100 ), ofsSurfaces = b.I32( 104 ), numTags = b.I32( 108 ), ofsTags = b.I32( 112 );
		if ( numFrames <= 0 || numBones <= 0 ) return null;
		frame = Math.Clamp( frame, 0, numFrames - 1 );

		int frameSize = 52 + numBones * 12;
		int fo = ofsFrames + frame * frameSize;
		var boundsMin = b.V3( fo );
		var boundsMax = b.V3( fo + 12 );
		var parentOffset = b.V3( fo + 40 );

		var parents = new int[numBones];
		var parentDist = new float[numBones];
		var names = new string[numBones];
		for ( int i = 0; i < numBones; i++ )
		{
			int o = ofsBones + i * 80;
			names[i] = b.Str( o, 64 );
			parents[i] = b.I32( o + 64 );
			parentDist[i] = b.F32( o + 72 );
		}

		const float s2a = 360f / 65536f;
		var bones = new BoneXform[numBones];
		var done = new bool[numBones];

		void Calc( int i, int depth )
		{
			if ( done[i] || depth > 256 ) return;
			int p = parents[i];
			if ( p >= 0 && p < numBones ) Calc( p, depth + 1 );

			int co = fo + 52 + i * 12;
			var ang = new Vector3( b.I16( co ) * s2a, b.I16( co + 2 ) * s2a, b.I16( co + 4 ) * s2a );
			var axis = Axis.FromAngles(ang.x, ang.y, ang.z);
			Vector3 translation;
			if ( p >= 0 && p < numBones )
			{
				float pitch = b.I16( co + 8 ) * s2a, yaw = b.I16( co + 10 ) * s2a;
				var dir = Axis.FromAngles( pitch, yaw, 0 ).Forward;
				translation = bones[p].Translation + dir * parentDist[i];
			}
			else translation = parentOffset;

			bones[i] = new BoneXform { Axis = axis, Translation = translation };
			done[i] = true;
		}
		for ( int i = 0; i < numBones; i++ ) Calc( i, 0 );

		// The weight offsets can be interpreted with the bone matrix or its transpose depending on
		// how the exporter wrote them; pick the interpretation that matches the stored frame bounds.
		var normal = Build( b, name, ofsSurfaces, numSurfaces, bones, false );
		var transposed = Build( b, name, ofsSurfaces, numSurfaces, bones, true );
		var model = BoundsError( transposed, boundsMin, boundsMax ) < BoundsError( normal, boundsMin, boundsMax ) ? transposed : normal;
		model.FrameCount = numFrames;

		// bones in the chosen frame
		for ( int i = 0; i < numBones; i++ )
		{
			var q = QuatFromAxis( bones[i].Axis );
			model.Bones.Add( new ModelBone { Name = names[i], Parent = parents[i], Position = bones[i].Translation, Qx = q.x, Qy = q.y, Qz = q.z, Qw = q.w } );
		}

		for ( int i = 0; i < numTags; i++ )
		{
			int o = ofsTags + i * 72;
			int bone = b.I32( o + 68 );
			if ( bone < 0 || bone >= numBones ) continue;
			model.Tags.Add( new ModelTag { Name = b.Str( o, 64 ), Origin = bones[bone].Translation, Axis = bones[bone].Axis } );
		}
		return model;
	}

	static float BoundsError( ModelData m, Vector3 min, Vector3 max )
	{
		var mn = new Vector3( float.MaxValue, float.MaxValue, float.MaxValue );
		var mx = -mn;
		foreach ( var s in m.Surfaces ) foreach ( var v in s.Vertices ) { mn = Vector3.Min( mn, v.Position ); mx = Vector3.Max( mx, v.Position ); }
		if ( mn.x > mx.x ) return float.MaxValue;
		return (mn - min).Length + (mx - max).Length;
	}

	static ModelData Build( Bin b, string name, int ofsSurfaces, int numSurfaces, BoneXform[] bones, bool transpose )
	{
		var model = new ModelData { Name = name };
		int so = ofsSurfaces;
		for ( int s = 0; s < numSurfaces && b.Has( so, 176 ); s++ )
		{
			var surf = new ModelSurface
			{
				Name = b.Str( so + 4, 64 ),
				Shader = PathUtil.Normalize( PathUtil.StripExtensionIfImage( b.Str( so + 68, 64 ) ) ),
			};
			int numVerts = b.I32( so + 144 ), ofsVerts = b.I32( so + 148 ), numTris = b.I32( so + 152 ), ofsTris = b.I32( so + 156 );
			int ofsEnd = b.I32( so + 172 );

			int vo = so + ofsVerts;
			for ( int v = 0; v < numVerts && b.Has( vo, 32 ); v++ )
			{
				var n = b.V3( vo );
				var uv = b.V2( vo + 12 );
				int numWeights = b.I32( vo + 20 );
				var pos = Vector3.Zero;
				var nrm = Vector3.Zero;
				var vert = new ModelVertex { TexCoord = uv };
				var infl = new List<(int, float)>();
				for ( int w = 0; w < numWeights; w++ )
				{
					int wo = vo + 32 + w * 20;
					int bone = b.I32( wo );
					float weight = b.F32( wo + 4 );
					var ofs = b.V3( wo + 8 );
					if ( bone < 0 || bone >= bones.Length ) continue;
					var bx = bones[bone];
					pos += (Apply( bx.Axis, ofs, transpose ) + bx.Translation) * weight;
					nrm += Apply( bx.Axis, n, transpose ) * weight;
					infl.Add( (bone, weight) );
				}
				vert.Position = pos;
				vert.Normal = nrm.Normal;
				infl = infl.OrderByDescending( x => x.Item2 ).Take( 4 ).ToList();
				float total = infl.Sum( x => x.Item2 );
				if ( total <= 0 ) total = 1;
				if ( infl.Count > 0 ) { vert.B0 = infl[0].Item1; vert.W0 = infl[0].Item2 / total; }
				if ( infl.Count > 1 ) { vert.B1 = infl[1].Item1; vert.W1 = infl[1].Item2 / total; }
				if ( infl.Count > 2 ) { vert.B2 = infl[2].Item1; vert.W2 = infl[2].Item2 / total; }
				if ( infl.Count > 3 ) { vert.B3 = infl[3].Item1; vert.W3 = infl[3].Item2 / total; }
				surf.Vertices.Add( vert );
				vo += 32 + numWeights * 20;
			}

			for ( int t = 0; t < numTris; t++ )
			{
				int to = so + ofsTris + t * 12;
				int i0 = b.I32( to ), i1 = b.I32( to + 4 ), i2 = b.I32( to + 8 );
				if ( (uint)i0 >= surf.Vertices.Count || (uint)i1 >= surf.Vertices.Count || (uint)i2 >= surf.Vertices.Count ) continue;
				surf.Indices.Add( i0 ); surf.Indices.Add( i1 ); surf.Indices.Add( i2 );
			}
			model.Surfaces.Add( surf );
			if ( ofsEnd <= 0 ) break;
			so += ofsEnd;
		}
		return model;
	}

	static Vector3 Apply( Axis a, Vector3 v, bool transpose ) => transpose
		? new Vector3( v.Dot( a.Forward ), v.Dot( a.Left ), v.Dot( a.Up ) )
		: a.Transform( v );

	internal static (float x, float y, float z, float w) QuatFromAxis( Axis a )
	{
		// columns of the rotation matrix are forward, left, up
		float m00 = a.Forward.x, m01 = a.Left.x, m02 = a.Up.x;
		float m10 = a.Forward.y, m11 = a.Left.y, m12 = a.Up.y;
		float m20 = a.Forward.z, m21 = a.Left.z, m22 = a.Up.z;
		float tr = m00 + m11 + m22;
		float x, y, z, w;
		if ( tr > 0 )
		{
			float s = MathF.Sqrt( tr + 1 ) * 2;
			w = 0.25f * s; x = (m21 - m12) / s; y = (m02 - m20) / s; z = (m10 - m01) / s;
		}
		else if ( m00 > m11 && m00 > m22 )
		{
			float s = MathF.Sqrt( 1 + m00 - m11 - m22 ) * 2;
			w = (m21 - m12) / s; x = 0.25f * s; y = (m01 + m10) / s; z = (m02 + m20) / s;
		}
		else if ( m11 > m22 )
		{
			float s = MathF.Sqrt( 1 + m11 - m00 - m22 ) * 2;
			w = (m02 - m20) / s; x = (m01 + m10) / s; y = 0.25f * s; z = (m12 + m21) / s;
		}
		else
		{
			float s = MathF.Sqrt( 1 + m22 - m00 - m11 ) * 2;
			w = (m10 - m01) / s; x = (m02 + m20) / s; y = (m12 + m21) / s; z = 0.25f * s;
		}
		return (x, y, z, w);
	}
}

/// <summary>
/// Call of Duty / United Offensive XModel (version 14): xmodel/NAME references
/// xmodelparts/LOD (skeleton) and xmodelsurfs/LOD (geometry). Images are in skins/.
/// </summary>
public static class XModel
{
	public sealed class Header
	{
		public int Version;
		public List<(string Name, float Distance, List<string> Materials)> Lods = new();
	}

	public static Header ReadHeader( byte[] data )
	{
		var b = new Bin( data );
		int version = b.U16( 0 );
		if ( version != 14 ) return null;
		var h = new Header { Version = version };
		int o = 2 + 24;
		var names = new List<(string, float)>();
		for ( int i = 0; i < 3; i++ )
		{
			float dist = b.F32( o ); o += 4;
			var n = b.CStr( ref o );
			if ( n.Length > 0 ) names.Add( (n, dist) );
		}
		o += 4;
		uint padding = b.U32( o ); o += 4;
		if ( padding > 4096 ) return null;
		for ( int i = 0; i < padding; i++ )
		{
			uint sub = b.U32( o ); o += 4;
			o += (int)(sub * 48 + 36);
			if ( o > data.Length ) return null;
		}
		foreach ( var (n, dist) in names )
		{
			int count = b.U16( o ); o += 2;
			var mats = new List<string>();
			for ( int k = 0; k < count && o < data.Length; k++ ) mats.Add( b.CStr( ref o ) );
			h.Lods.Add( (n, dist, mats) );
		}
		return h;
	}

	struct Xf { public Vector3 P; public float Qx, Qy, Qz, Qw; }

	static Vector3 Rotate( Vector3 v, float qx, float qy, float qz, float qw )
	{
		// v' = q v q*
		var u = new Vector3( qx, qy, qz );
		var t = u.Cross( v ) * 2;
		return v + t * qw + u.Cross( t );
	}

	static (float, float, float, float) Mul( float ax, float ay, float az, float aw, float bx, float by, float bz, float bw ) =>
		(aw * bx + ax * bw + ay * bz - az * by,
		 aw * by - ax * bz + ay * bw + az * bx,
		 aw * bz + ax * by - ay * bx + az * bw,
		 aw * bw - ax * bx - ay * by - az * bz);

	public static List<ModelBone> ReadParts( byte[] data )
	{
		var bones = new List<ModelBone>();
		if ( data is null ) return bones;
		var b = new Bin( data );
		if ( b.U16( 0 ) != 14 ) return bones;
		int count = b.U16( 2 ), roots = b.U16( 4 );
		int o = 6;
		var local = new List<Xf>();
		var parents = new List<int>();
		for ( int i = 0; i < roots; i++ ) { local.Add( new Xf { Qw = 1 } ); parents.Add( -1 ); }
		for ( int i = 0; i < count; i++ )
		{
			int parent = (sbyte)b.U8( o ); o += 1;
			var p = b.V3( o ); o += 12;
			float qx = b.I16( o ) / 32768f, qy = b.I16( o + 2 ) / 32768f, qz = b.I16( o + 4 ) / 32768f; o += 6;
			float qw = MathF.Sqrt( MathF.Max( 0, 1 - qx * qx - qy * qy - qz * qz ) );
			local.Add( new Xf { P = p, Qx = qx, Qy = qy, Qz = qz, Qw = qw } );
			parents.Add( parent );
		}

		var world = new Xf[local.Count];
		for ( int i = 0; i < local.Count; i++ )
		{
			var name = b.CStr( ref o );
			o += 24;
			var l = local[i];
			int p = parents[i];
			if ( p >= 0 && p < i )
			{
				var pw = world[p];
				var pos = pw.P + Rotate( l.P, pw.Qx, pw.Qy, pw.Qz, pw.Qw );
				var (x, y, z, w) = Mul( pw.Qx, pw.Qy, pw.Qz, pw.Qw, l.Qx, l.Qy, l.Qz, l.Qw );
				world[i] = new Xf { P = pos, Qx = x, Qy = y, Qz = z, Qw = w };
			}
			else world[i] = l;

			bones.Add( new ModelBone { Name = name, Parent = p < i ? p : -1, Position = world[i].P, Qx = world[i].Qx, Qy = world[i].Qy, Qz = world[i].Qz, Qw = world[i].Qw } );
		}
		return bones;
	}

	public static List<ModelSurface> ReadSurfaces( byte[] data, List<ModelBone> bones )
	{
		var surfaces = new List<ModelSurface>();
		if ( data is null ) return surfaces;
		var b = new Bin( data );
		if ( b.U16( 0 ) != 14 ) return surfaces;
		int o = 2;
		int surfaceCount = b.U16( o ); o += 2;
		const int rigged = 65535;

		for ( int s = 0; s < surfaceCount && o < data.Length; s++ )
		{
			o += 1;
			int vertexCount = b.U16( o ); o += 2;
			int triangleCount = b.U16( o ); o += 2;
			o += 2;
			int defaultBone = b.U16( o ); o += 2;
			bool isRigged = defaultBone == rigged;
			if ( isRigged ) { o += 4; defaultBone = 0; }

			var surf = new ModelSurface { Name = $"surface{s}" };
			var tris = surf.Indices;

			// triangle strips
			int guard = 0;
			while ( tris.Count / 3 < triangleCount && o < data.Length && guard++ < 1_000_000 )
			{
				int idxCount = b.U8( o ); o += 1;
				int i1 = b.U16( o ), i2 = b.U16( o + 2 ), i3 = b.U16( o + 4 ); o += 6;
				if ( i1 != i2 && i1 != i3 && i2 != i3 ) { tris.Add( i1 ); tris.Add( i2 ); tris.Add( i3 ); }

				int i = 3;
				while ( i < idxCount )
				{
					int i4 = i3;
					int i5 = b.U16( o ); o += 2;
					if ( i4 != i2 && i4 != i5 && i2 != i5 ) { tris.Add( i4 ); tris.Add( i2 ); tris.Add( i5 ); }

					int v = i + 1;
					if ( v >= idxCount ) break;

					i2 = i5;
					i3 = b.U16( o ); o += 2;
					if ( i4 != i2 && i4 != i3 && i2 != i3 ) { tris.Add( i4 ); tris.Add( i2 ); tris.Add( i3 ); }
					i = v + 1;
				}
			}

			var weightCounts = new int[vertexCount];
			for ( int v = 0; v < vertexCount; v++ )
			{
				var n = b.V3( o ); o += 12;
				var uv = b.V2( o ); o += 8;
				int weights = 0, bone = defaultBone;
				if ( isRigged ) { weights = b.U16( o ); bone = b.U16( o + 2 ); o += 4; }
				var p = b.V3( o ); o += 12;
				if ( weights != 0 ) o += 4;
				weightCounts[v] = weights;

				if ( bone >= 0 && bone < bones.Count )
				{
					var bn = bones[bone];
					p = bn.Position + Rotate( p, bn.Qx, bn.Qy, bn.Qz, bn.Qw );
					n = Rotate( n, bn.Qx, bn.Qy, bn.Qz, bn.Qw );
				}

				surf.Vertices.Add( new ModelVertex { Position = p, Normal = n.Normal, TexCoord = uv, B0 = bone, W0 = 1 } );
			}

			for ( int v = 0; v < vertexCount; v++ )
			{
				var vert = surf.Vertices[v];
				for ( int w = 0; w < weightCounts[v]; w++ )
				{
					int bone = b.U16( o ); o += 2;
					o += 12;
					float influence = b.F32( o ); o += 4;
					if ( influence > 1.0001f ) influence /= rigged;
					influence = Math.Clamp( influence, 0, 1 );
					if ( w == 0 ) { vert.B1 = bone; vert.W1 = influence; }
					else if ( w == 1 ) { vert.B2 = bone; vert.W2 = influence; }
					else if ( w == 2 ) { vert.B3 = bone; vert.W3 = influence; }
					vert.W0 -= influence;
				}
				vert.W0 = MathF.Max( 0, vert.W0 );
				surf.Vertices[v] = vert;
			}

			// drop out-of-range indices
			for ( int k = tris.Count - 3; k >= 0; k -= 3 )
				if ( tris[k] >= vertexCount || tris[k + 1] >= vertexCount || tris[k + 2] >= vertexCount )
					tris.RemoveRange( k, 3 );

			surfaces.Add( surf );
		}
		return surfaces;
	}

	/// <summary>Load a complete xmodel through a file lookup.</summary>
	public static ModelData Load( string name, Func<string, byte[]> read )
	{
		var header = ReadHeader( read( $"xmodel/{name}" ) );
		if ( header is null || header.Lods.Count == 0 ) return null;
		var lod = header.Lods[0];

		var bones = ReadParts( read( $"xmodelparts/{lod.Name}" ) );
		var surfaces = ReadSurfaces( read( $"xmodelsurfs/{lod.Name}" ), bones );
		var model = new ModelData { Name = name, Bones = bones };
		for ( int i = 0; i < surfaces.Count; i++ )
		{
			surfaces[i].Shader = i < lod.Materials.Count ? lod.Materials[i] : string.Empty;
			model.Surfaces.Add( surfaces[i] );
		}
		return model;
	}
}

/// <summary>Q3 .skin file: "surfacename,shader" lines.</summary>
public static class SkinFile
{
	public static Dictionary<string, string> Parse( string text )
	{
		var map = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		if ( text is null ) return map;
		foreach ( var raw in text.Split( '\n' ) )
		{
			var line = raw.Trim();
			int comma = line.IndexOf( ',' );
			if ( comma <= 0 ) continue;
			var surf = line[..comma].Trim();
			var shader = line[(comma + 1)..].Trim().Trim( '"' );
			if ( surf.StartsWith( "tag_", StringComparison.OrdinalIgnoreCase ) || shader.Length == 0 ) continue;
			map[surf] = PathUtil.Normalize( PathUtil.StripExtensionIfImage( shader ) );
		}
		return map;
	}
}

/// <summary>Q3 player animation.cfg.</summary>
public sealed class PlayerAnimations
{
	public struct Anim { public int First, Count, Loop, Fps; }

	public const int TorsoStand = 11;
	public const int LegsIdle = 22;
	public const int TorsoGesture = 6;
	public const int LegsWalkCrouch = 13;

	public List<Anim> Anims = new();

	public static PlayerAnimations Parse( string text )
	{
		var r = new PlayerAnimations();
		if ( text is null ) return r;
		var tok = new Tokenizer( text );
		while ( !tok.AtEnd && r.Anims.Count < 31 )
		{
			var t = tok.Next();
			if ( t.Length == 0 ) break;
			if ( !(char.IsDigit( t[0] ) || t[0] == '-') )
			{
				// sex, footsteps, headoffset x y z, fixedlegs, fixedtorso
				if ( t.Equals( "headoffset", StringComparison.OrdinalIgnoreCase ) ) { tok.Next(); tok.Next(); tok.Next(); }
				else if ( t.Equals( "sex", StringComparison.OrdinalIgnoreCase ) || t.Equals( "footsteps", StringComparison.OrdinalIgnoreCase ) ) tok.Next();
				continue;
			}
			var a = new Anim { First = Num.I( t ), Count = Num.I( tok.Next() ), Loop = Num.I( tok.Next() ), Fps = Num.I( tok.Next() ) };
			r.Anims.Add( a );
		}

		// legs frames are numbered after the torso frames in the cfg
		if ( r.Anims.Count > LegsWalkCrouch )
		{
			int skip = r.Anims[LegsWalkCrouch].First - r.Anims[TorsoGesture].First;
			for ( int i = LegsWalkCrouch; i < Math.Min( r.Anims.Count, 25 ); i++ )
			{
				var a = r.Anims[i];
				a.First -= skip;
				r.Anims[i] = a;
			}
		}
		return r;
	}

	public int FrameOf( int anim, int fallback = 0 ) => anim < Anims.Count ? Math.Max( 0, Anims[anim].First ) : fallback;
}
