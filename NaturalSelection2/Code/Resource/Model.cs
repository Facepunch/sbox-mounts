using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace NS2;

/// <summary>
/// Loads NS2 .model files (Spark engine "MDL" format).
/// </summary>
public class ModelLoader( string fullPath ) : ResourceLoader<GameMount>
{
	private string FullPath { get; } = fullPath;
	// Loaded when a model first loads, not whenever this assembly does (mounted or not)
	private static Material DefaultMaterial => field ??= Material.Load( "materials/dev/primary_white.vmat" );

	enum ChunkType
	{
		Vertices = 1,        // int32 n; n × 92-byte vertex
		Indices = 2,         // int32 n; n × int32
		FaceSets = 3,        // material, firstFace, numFaces, bone palette
		Materials = 4,       // int32 n; n × string
		Solids = 5,          // collision hulls: name, bone, coords, mass, points, tris
		Bones = 6,           // name, parent, pos, rot, scale, scaleOrient, uniformScale
		Animations = 7,      // curves (compressed) and/or per-frame poses
		AnimationNodes = 8,  // play/blend/layer graph nodes
		Sequences = 9,       // name → animation node (+ length, v≥2)
		BlendParameters = 10,
		Cameras = 11,
		HitProxies = 12,     // box hitboxes
		AttachPoints = 13,
		Joints = 14,         // ragdoll joints; flag==0 → 6 float limits
		CollisionPairs = 15, // disabled body collision pairs
		CollisionReps = 16,
		BoundingBox = 17,    // 6 floats: mins, maxs
		BoneBounds = 18,     // per-bone AABBs (requires Bones read first)
		ExternalModel = 19,  // path of base model supplying animation data
	}

	// File structures (little-endian, packed); read field by field by Reader
	const int CoordsSize = 48;
	const int PoseSize = 60;

	struct FileCoords // 48 bytes: 3x3 axes + origin
	{
		public Vector3 XAxis, YAxis, ZAxis, Origin;
	}

	struct FilePose // 60 bytes, one bone transform per animation frame
	{
		public Vector3 Position;
		public Quaternion Rotation;
		public Vector3 Scale;
		public Quaternion ScaleOrientation;
		public float UniformScale;
	}

	struct Bone
	{
		public string Name;
		public int Parent;
		public Transform Transform;      // model-space (parents applied)
		public Transform LocalTransform; // parent-relative bind pose
	}

	struct FaceSet
	{
		public int MaterialIndex;
		public int FirstFace;
		public int NumFaces;
		public int[] BonePalette;
	}

	struct Solid
	{
		public string Name;
		public int BoneIndex;
		public Transform Transform;
		public Vector3[] Points;   // converted to s&box space
		public int[] Indices;
	}

	struct Joint
	{
		public string Name;
		public int Body0, Body1;
		public Transform Frame0, Frame1;
		public float SwingMin, SwingMax, TwistMin, TwistMax;
	}

	struct AttachPoint
	{
		public string Name;
		public int BoneIndex;
		public Transform Transform;
	}

	struct AnimationNode
	{
		public int Type; // 1 = animation, 2 = blend, 3 = layered
		public int AnimationIndex;
		public int[] Children;
		public int ParameterIndex; // blend nodes
		public float ParamMin, ParamMax;
	}

	struct Sequence
	{
		public string Name;
		public int AnimationNodeIndex;
		public float Length;
	}

	// A single keyframed curve, loaded only while its animation is being built.
	struct Curve<T>
	{
		public float[] Times;
		public T[] Values;
	}

	// File offsets for one animation's data; resolved lazily so the redundant
	// pose blobs are only read from disk when the animation is actually uncompressed.
	class ParsedAnimation
	{
		public int Flags;
		public int NumFrames;
		public float FrameRate;
		public bool Compressed;
		public long CurvesOffset;      // file offset of curve data (compressed only)
		public int NumCurves;
		public (int BoneIndex, long PosesOffset)[] BoneCurves = [];
	}

	class ParsedModel
	{
		public SkinnedVertex[] Vertices = [];
		public int[] Indices = [];
		public FaceSet[] FaceSets = [];
		public string[] Materials = [];
		public Bone[] Bones = [];
		public Solid[] Solids = [];
		public ParsedAnimation[] Animations = [];
		public AnimationNode[] AnimationNodes = [];
		public Sequence[] Sequences = [];
		public string[] BlendParameters = [];
		public Joint[] Joints = [];
		public AttachPoint[] AttachPoints = [];
		public int CollisionRepBodyCount = -1; // body count of collision rep 0
		public string ExternalModel;
	}

	// Buffered little-endian reader over a seekable stream. Large skips seek past the data
	// instead of reading it, so unused sections (e.g. redundant pose blobs) never load.
	struct Reader
	{
		const int BufferSize = 128 * 1024; // must fit the largest single read (strings ≤ 64KB)

		private readonly Stream stream;
		private readonly byte[] buffer;
		private long bufferStart; // file offset of buffer[0]
		private int pos;
		private int count;

		public readonly long Length;
		public readonly long Offset => bufferStart + pos;

		public Reader( Stream stream, long offset = 0 )
		{
			this.stream = stream;
			buffer = new byte[BufferSize];
			Length = stream.Length;
			Seek( offset );
		}

		public void Seek( long offset )
		{
			if ( offset < 0 || offset > Length ) throw new InvalidDataException( "NS2 model: offset outside file" );
			stream.Position = offset;
			bufferStart = offset;
			pos = count = 0;
		}

		private ReadOnlySpan<byte> Take( int bytes )
		{
			Check( bytes );
			if ( count - pos < bytes ) Fill( bytes );
			var span = buffer.AsSpan( pos, bytes );
			pos += bytes;
			return span;
		}

		private void Fill( int bytes )
		{
			var remaining = count - pos;
			Array.Copy( buffer, pos, buffer, 0, remaining );
			bufferStart += pos;
			pos = 0;
			count = remaining;
			while ( count < bytes )
			{
				var read = stream.Read( buffer, count, buffer.Length - count );
				if ( read == 0 ) throw new InvalidDataException( "NS2 model: unexpected end of file" );
				count += read;
			}
		}

		public byte Byte() => Take( 1 )[0];

		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public int Int() => BinaryPrimitives.ReadInt32LittleEndian( Take( 4 ) );

		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public float Float() => BinaryPrimitives.ReadSingleLittleEndian( Take( 4 ) );

		public Vector3 ReadVector3() => new( Float(), Float(), Float() );

		public Quaternion ReadQuaternion() => new( Float(), Float(), Float(), Float() );

		public FileCoords ReadCoords() => new()
		{
			XAxis = ReadVector3(),
			YAxis = ReadVector3(),
			ZAxis = ReadVector3(),
			Origin = ReadVector3(),
		};

		public FilePose ReadPose() => new()
		{
			Position = ReadVector3(),
			Rotation = ReadQuaternion(),
			Scale = ReadVector3(),
			ScaleOrientation = ReadQuaternion(),
			UniformScale = Float(),
		};

		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public readonly void Check( long bytes )
		{
			if ( bytes < 0 || Length - Offset < bytes ) throw new InvalidDataException( "NS2 model: unexpected end of file" );
		}

		public string String()
		{
			int len = Int();
			if ( (uint)len > 0x10000 ) throw new InvalidDataException( $"NS2 model: bad string length {len}" );
			return Encoding.ASCII.GetString( Take( len ) );
		}

		public int[] Ints( int count )
		{
			Check( (long)count * 4 );
			var values = new int[count];
			for ( int i = 0; i < count; i++ ) values[i] = Int();
			return values;
		}

		public float[] Floats( int count )
		{
			Check( (long)count * 4 );
			var values = new float[count];
			for ( int i = 0; i < count; i++ ) values[i] = Float();
			return values;
		}

		/// <summary>Advance past the given number of bytes, seeking if they aren't buffered.</summary>
		public void Skip( long bytes )
		{
			Check( bytes );
			if ( bytes <= count - pos ) pos += (int)bytes;
			else Seek( Offset + bytes );
		}
	}

	private ParsedModel ParseFile( string path )
	{
		using var stream = Host.FileSystem.OpenRead( path );
		return Parse( stream );
	}

	private static ParsedModel Parse( Stream stream )
	{
		var r = new Reader( stream );
		r.Check( 4 );
		if ( r.Byte() != 'M' || r.Byte() != 'D' || r.Byte() != 'L' )
			throw new InvalidDataException( "NS2 model: bad magic" );

		int version = r.Byte();
		if ( version < 6 || version > 7 )
			throw new NotSupportedException( $"NS2 model: unsupported version {version}" );

		var model = new ParsedModel();

		while ( r.Offset < r.Length )
		{
			r.Check( 8 );
			var chunkId = (ChunkType)r.Int();
			int chunkLen = r.Int();
			long chunkEnd = r.Offset + chunkLen;
			if ( chunkLen < 0 || chunkEnd > r.Length )
				throw new InvalidDataException( $"NS2 model: chunk {chunkId} overruns file" );

			switch ( chunkId )
			{
				case ChunkType.Vertices: ReadVertices( ref r, model ); break;
				case ChunkType.Indices: model.Indices = r.Ints( r.Int() ); break;
				case ChunkType.FaceSets: ReadFaceSets( ref r, model ); break;
				case ChunkType.Materials: ReadMaterials( ref r, model ); break;
				case ChunkType.Solids: ReadSolids( ref r, model ); break;
				case ChunkType.Bones: ReadBones( ref r, model ); break;
				case ChunkType.Animations: ReadAnimations( ref r, model, version ); break;
				case ChunkType.AnimationNodes: ReadAnimationNodes( ref r, model ); break;
				case ChunkType.Sequences: ReadSequences( ref r, model, version ); break;
				case ChunkType.BlendParameters: ReadBlendParameters( ref r, model ); break;
				case ChunkType.Cameras: ReadCameras( ref r ); break;
				case ChunkType.HitProxies: ReadHitProxies( ref r ); break;
				case ChunkType.AttachPoints: ReadAttachPoints( ref r, model ); break;
				case ChunkType.Joints: ReadJoints( ref r, model ); break;
				case ChunkType.CollisionPairs: r.Skip( (long)r.Int() * 8 ); break;
				case ChunkType.CollisionReps: ReadCollisionReps( ref r, model, version ); break;
				case ChunkType.BoundingBox: r.Skip( 6 * 4 ); break;
				case ChunkType.BoneBounds: r.Skip( (long)model.Bones.Length * 6 * 4 ); break;
				case ChunkType.ExternalModel: model.ExternalModel = r.String(); break;
				default: throw new InvalidDataException( $"NS2 model: unknown chunk {chunkId}" );
			}

			if ( r.Offset != chunkEnd )
				throw new InvalidDataException( $"NS2 model: chunk {chunkId} size mismatch" );
		}

		return model;
	}

	private static void ReadVertices( ref Reader r, ParsedModel model )
	{
		int count = r.Int();
		r.Check( (long)count * 92 ); // 92 bytes per vertex, v≥6 layout
		var dst = new SkinnedVertex[count];

		for ( int i = 0; i < count; i++ )
		{
			var position = r.ReadVector3();
			var normal = r.ReadVector3();
			var tangent = r.ReadVector3();
			r.Skip( 12 ); // binormal
			var texcoord = new Vector2( r.Float(), r.Float() );
			r.Skip( 4 ); // color
			float w0 = r.Float(); int b0 = r.Int();
			float w1 = r.Float(); int b1 = r.Int();
			float w2 = r.Float(); int b2 = r.Int();
			float w3 = r.Float(); int b3 = r.Int();

			dst[i] = new SkinnedVertex(
				ConvertPosition( position ),
				ConvertDirection( normal ),
				ConvertDirection( tangent ),
				texcoord,
				new Color32( (byte)b0, (byte)b1, (byte)b2, (byte)b3 ),
				NormalizeWeights( w0, w1, w2, w3 )
			);
		}

		model.Vertices = dst;
	}

	private static void ReadFaceSets( ref Reader r, ParsedModel model )
	{
		int count = r.Int();
		var sets = new FaceSet[count];
		for ( int i = 0; i < count; i++ )
		{
			sets[i].MaterialIndex = r.Int();
			sets[i].FirstFace = r.Int();
			sets[i].NumFaces = r.Int();
			sets[i].BonePalette = r.Ints( r.Int() );
		}
		model.FaceSets = sets;
	}

	private static void ReadMaterials( ref Reader r, ParsedModel model )
	{
		int count = r.Int();
		var names = new string[count];
		for ( int i = 0; i < count; i++ ) names[i] = r.String();
		model.Materials = names;
	}

	private static void ReadSolids( ref Reader r, ParsedModel model )
	{
		int count = r.Int();
		var solids = new Solid[count];
		for ( int i = 0; i < count; i++ )
		{
			solids[i].Name = r.String();
			solids[i].BoneIndex = r.Int();
			solids[i].Transform = ConvertCoords( r.ReadCoords() );
			_ = r.Float(); // mass

			int pointCount = r.Int();
			r.Check( (long)pointCount * 12 );
			var points = new Vector3[pointCount];
			for ( int p = 0; p < points.Length; p++ ) points[p] = ConvertPosition( r.ReadVector3() );
			solids[i].Points = points;

			solids[i].Indices = r.Ints( r.Int() * 3 );
		}
		model.Solids = solids;
	}

	private static void ReadBones( ref Reader r, ParsedModel model )
	{
		int count = r.Int();
		var bones = new Bone[count];
		for ( int i = 0; i < count; i++ )
		{
			var name = r.String();
			int parent = r.Int();
			var pose = r.ReadPose(); // bone bind pose has the same 60-byte layout

			var transform = PoseToTransform( pose );
			var local = transform;
			if ( parent >= 0 ) transform = bones[parent].Transform.ToWorld( transform );

			bones[i] = new Bone { Name = name, Parent = parent, Transform = transform, LocalTransform = local };
		}
		model.Bones = bones;
	}

	private static void ReadAnimations( ref Reader r, ParsedModel model, int version )
	{
		int count = r.Int();
		if ( (uint)count > 5000 ) throw new InvalidDataException( "NS2 model: too many animations" );
		var anims = new ParsedAnimation[count];

		for ( int i = 0; i < count; i++ )
		{
			var anim = new ParsedAnimation
			{
				Flags = r.Int(),
				NumFrames = r.Int(),
				FrameRate = r.Float(),
			};

			if ( version >= 7 ) anim.Compressed = r.Int() != 0;

			if ( anim.Compressed )
			{
				anim.CurvesOffset = r.Offset + 4;
				anim.NumCurves = r.Int();
				for ( int j = 0; j < anim.NumCurves; j++ )
				{
					SkipCurve( ref r, 12 ); // position (Vector3)
					SkipCurve( ref r, 12 ); // scale (Vector3)
					SkipCurve( ref r, 4 );  // uniform scale (float)
					SkipCurve( ref r, 16 ); // rotation (Quaternion)
					SkipCurve( ref r, 16 ); // scale orientation (Quaternion)
				}
			}

			// Per-frame pose blobs. The engine ignores these when compressed curves
			// exist — skipping them here means they're never decoded.
			int numBoneCurves = r.Int();
			if ( (uint)numBoneCurves > 5000 ) throw new InvalidDataException( "NS2 model: too many bone curves" );
			anim.BoneCurves = new (int, long)[numBoneCurves];
			for ( int j = 0; j < numBoneCurves; j++ )
			{
				int boneIndex = r.Int();
				anim.BoneCurves[j] = (boneIndex, r.Offset);
				r.Skip( (long)anim.NumFrames * PoseSize );
			}

			if ( version > 2 )
			{
				int numTags = r.Int();
				for ( int j = 0; j < numTags; j++ )
				{
					_ = r.Int(); // frame
					_ = r.String();
				}
			}

			anims[i] = anim;
		}

		model.Animations = anims;

		static void SkipCurve( ref Reader r, int valueSize )
		{
			int keys = r.Int();
			r.Skip( (long)keys * (4 + valueSize) );
		}
	}

	private static void ReadAnimationNodes( ref Reader r, ParsedModel model )
	{
		int count = r.Int();
		var nodes = new AnimationNode[count];
		for ( int i = 0; i < count; i++ )
		{
			int type = r.Int();
			_ = r.Int(); // flags
			nodes[i].Type = type;
			switch ( type )
			{
				case 1: // play animation
					nodes[i].AnimationIndex = r.Int();
					break;
				case 2: // blend by parameter
					nodes[i].ParameterIndex = r.Int();
					nodes[i].ParamMin = r.Float();
					nodes[i].ParamMax = r.Float();
					nodes[i].Children = r.Ints( r.Int() );
					break;
				case 3: // layered
					nodes[i].Children = r.Ints( r.Int() );
					break;
				default:
					throw new InvalidDataException( $"NS2 model: bad animation node type {type}" );
			}
		}
		model.AnimationNodes = nodes;
	}

	private static void ReadSequences( ref Reader r, ParsedModel model, int version )
	{
		int count = r.Int();
		var seqs = new Sequence[count];
		for ( int i = 0; i < count; i++ )
		{
			seqs[i].Name = r.String();
			seqs[i].AnimationNodeIndex = r.Int();
			seqs[i].Length = version >= 2 ? r.Float() : 1.0f;
		}
		model.Sequences = seqs;
	}

	private static void ReadBlendParameters( ref Reader r, ParsedModel model )
	{
		int count = r.Int();
		var names = new string[count];
		for ( int i = 0; i < count; i++ ) names[i] = r.String();
		model.BlendParameters = names;
	}

	private static void ReadCameras( ref Reader r )
	{
		int count = r.Int();
		for ( int i = 0; i < count; i++ )
		{
			_ = r.String();
			r.Skip( 8 ); // bone index, fov
			r.Skip( CoordsSize );
		}
	}

	private static void ReadHitProxies( ref Reader r )
	{
		int count = r.Int();
		for ( int i = 0; i < count; i++ )
		{
			_ = r.String();
			_ = r.Int(); // bone index
			r.Skip( CoordsSize );
			int shapeType = r.Int();
			if ( shapeType != 1 ) throw new InvalidDataException( $"NS2 model: hit proxy shape {shapeType} != box" );
			r.Skip( 12 ); // extents
		}
	}

	private static void ReadAttachPoints( ref Reader r, ParsedModel model )
	{
		int count = r.Int();
		var points = new AttachPoint[count];
		for ( int i = 0; i < count; i++ )
		{
			points[i].Name = r.String();
			points[i].BoneIndex = r.Int();
			points[i].Transform = ConvertCoords( r.ReadCoords() );
		}
		model.AttachPoints = points;
	}

	private static void ReadJoints( ref Reader r, ParsedModel model )
	{
		int count = r.Int();
		var joints = new List<Joint>( count );
		for ( int i = 0; i < count; i++ )
		{
			var name = r.String();
			int body0 = r.Int();
			var frame0 = ConvertCoords( r.ReadCoords() );
			int body1 = r.Int();
			var frame1 = ConvertCoords( r.ReadCoords() );

			// flag != 0 → engine discards the joint and no limits follow
			if ( r.Int() != 0 )
				continue;

			float swingMin = r.Float();
			float twistMin = r.Float();
			_ = r.Float();
			float swingMax = r.Float();
			float twistMax = r.Float();
			_ = r.Float();
			joints.Add( new Joint
			{
				Name = name,
				Body0 = body0,
				Body1 = body1,
				Frame0 = frame0,
				Frame1 = frame1,
				SwingMin = swingMin,
				TwistMin = twistMin,
				SwingMax = swingMax,
				TwistMax = twistMax,
			} );
		}
		model.Joints = joints.ToArray();
	}

	private static void ReadCollisionReps( ref Reader r, ParsedModel model, int version )
	{
		int count = r.Int();
		for ( int i = 0; i < count; i++ )
		{
			if ( version < 5 ) _ = r.String();
			int bodyCount = r.Int();
			r.Skip( (version >= 5 ? 5 : 3) * 4 );
			if ( i == 0 ) model.CollisionRepBodyCount = bodyCount;
		}

		if ( version >= 5 )
		{
			int nameCount = r.Int();
			for ( int i = 0; i < nameCount; i++ )
			{
				_ = r.String();
				_ = r.Int();
			}
		}
	}

	static Vector3 SampleVector3( in Curve<Vector3> curve, float t )
	{
		var times = curve.Times;
		if ( times is not { Length: > 0 } ) return default;
		if ( t <= times[0] ) return curve.Values[0];
		if ( t >= times[^1] ) return curve.Values[^1];

		int hi = LowerBound( times, t );
		int lo = hi - 1;
		float u = (t - times[lo]) / (times[hi] - times[lo]);
		return Vector3.Lerp( curve.Values[lo], curve.Values[hi], u );
	}

	static Rotation SampleRotation( in Curve<Quaternion> curve, float t )
	{
		var times = curve.Times;
		if ( times is not { Length: > 0 } ) return Rotation.Identity;
		if ( t <= times[0] ) return ToRotation( curve.Values[0] );
		if ( t >= times[^1] ) return ToRotation( curve.Values[^1] );

		int hi = LowerBound( times, t );
		int lo = hi - 1;
		float u = (t - times[lo]) / (times[hi] - times[lo]);
		return Rotation.Slerp( ToRotation( curve.Values[lo] ), ToRotation( curve.Values[hi] ), u ).Normal;
	}

	static int LowerBound( float[] times, float t )
	{
		int lo = 0, hi = times.Length - 1;
		while ( lo < hi )
		{
			int mid = (lo + hi) >> 1;
			if ( times[mid] < t ) lo = mid + 1;
			else hi = mid;
		}
		return lo;
	}

	static Rotation ToRotation( Quaternion q ) => new( q.X, q.Y, q.Z, q.W );

	private static void BuildAnimations( ModelBuilder builder, ParsedModel model, Stream animSource )
	{
		if ( model.Animations.Length == 0 || model.Bones.Length == 0 )
			return;

		// Name animations after sequences where possible:
		// sequence → node → first type-1 descendant → animation
		var names = new string[model.Animations.Length];
		foreach ( var seq in model.Sequences )
		{
			int animIndex = ResolveAnimation( model.AnimationNodes, seq.AnimationNodeIndex );
			if ( animIndex >= 0 && animIndex < names.Length && names[animIndex] is null )
				names[animIndex] = seq.Name;
		}

		// The rest are blend-node poses — name them "{parameter}_{value}" (e.g. arc_yaw_-90)
		// from the blend node that references them so they're usable in animgraphs.
		foreach ( var node in model.AnimationNodes )
		{
			if ( node.Type != 2 || node.Children is not { Length: > 0 } )
				continue;

			var param = (uint)node.ParameterIndex < (uint)model.BlendParameters.Length
				? model.BlendParameters[node.ParameterIndex] : $"blend{node.ParameterIndex}";

			for ( int k = 0; k < node.Children.Length; k++ )
			{
				int animIndex = ResolveAnimation( model.AnimationNodes, node.Children[k] );
				if ( animIndex < 0 || animIndex >= names.Length || names[animIndex] is not null )
					continue;

				float value = node.Children.Length > 1
					? node.ParamMin + (node.ParamMax - node.ParamMin) * k / (node.Children.Length - 1)
					: node.ParamMin;
				names[animIndex] = $"{param}_{value:0.##}";
			}
		}

		var boneTransforms = new Transform[model.Bones.Length];
		var r = new Reader( animSource );

		for ( int i = 0; i < model.Animations.Length; i++ )
		{
			var anim = model.Animations[i];
			if ( anim.NumFrames <= 0 || anim.FrameRate <= 0 )
				continue;

			var animBuilder = builder.AddAnimation( names[i] ?? $"animation_{i}", anim.FrameRate );

			if ( anim.Compressed )
				AddCompressedFrames( animBuilder, anim, model, ref r, boneTransforms );
			else
				AddUncompressedFrames( animBuilder, anim, model, ref r, boneTransforms );
		}
	}

	private static void AddCompressedFrames( AnimationBuilder animBuilder, ParsedAnimation anim,
		ParsedModel model, ref Reader r, Transform[] boneTransforms )
	{
		r.Seek( anim.CurvesOffset );

		// Curves are parallel to the bone-curve list, which carries the actual bone
		// indices — bones without curves keep their bind pose.
		int boneCount = model.Bones.Length;
		var posCurves = new Curve<Vector3>[boneCount];
		var rotCurves = new Curve<Quaternion>[boneCount];
		var hasCurve = new bool[boneCount];

		for ( int j = 0; j < anim.NumCurves; j++ )
		{
			int boneIndex = j < anim.BoneCurves.Length ? anim.BoneCurves[j].BoneIndex : j;
			bool used = (uint)boneIndex < (uint)boneCount;

			// position
			var pos = used ? ReadVector3Curve( ref r ) : default;
			if ( !used ) SkipCurve( ref r, 12 );
			// scale + uniform scale + (below) scale orientation are unused by s&box skeletal animation
			SkipCurve( ref r, 12 );
			SkipCurve( ref r, 4 );
			// rotation
			var rot = used ? ReadQuaternionCurve( ref r ) : default;
			if ( !used ) SkipCurve( ref r, 16 );
			SkipCurve( ref r, 16 );

			if ( used )
			{
				posCurves[boneIndex] = pos;
				rotCurves[boneIndex] = rot;
				hasCurve[boneIndex] = true;
			}
		}

		for ( int frame = 0; frame < anim.NumFrames; frame++ )
		{
			float t = frame / anim.FrameRate;

			for ( int b = 0; b < boneTransforms.Length; b++ )
			{
				if ( !hasCurve[b] )
				{
					boneTransforms[b] = model.Bones[b].LocalTransform;
					continue;
				}

				var pos = ConvertPosition( SampleVector3( in posCurves[b], t ) );
				var rot = ConvertRotation( SampleRotation( in rotCurves[b], t ) );
				var tx = new Transform( pos, rot );
				boneTransforms[b] = tx.IsValid ? tx : model.Bones[b].LocalTransform;
			}

			animBuilder.AddFrame( boneTransforms.AsSpan() );
		}

		static Curve<Vector3> ReadVector3Curve( ref Reader r )
		{
			int keys = r.Int();
			var times = r.Floats( keys );
			r.Check( (long)keys * 12 );
			var values = new Vector3[keys];
			for ( int i = 0; i < keys; i++ ) values[i] = r.ReadVector3();
			return new Curve<Vector3> { Times = times, Values = values };
		}

		static Curve<Quaternion> ReadQuaternionCurve( ref Reader r )
		{
			int keys = r.Int();
			var times = r.Floats( keys );
			r.Check( (long)keys * 16 );
			var values = new Quaternion[keys];
			for ( int i = 0; i < keys; i++ ) values[i] = r.ReadQuaternion();
			return new Curve<Quaternion> { Times = times, Values = values };
		}

		static void SkipCurve( ref Reader r, int valueSize )
		{
			int keys = r.Int();
			r.Skip( (long)keys * (4 + valueSize) );
		}
	}

	private static void AddUncompressedFrames( AnimationBuilder animBuilder, ParsedAnimation anim,
		ParsedModel model, ref Reader r, Transform[] boneTransforms )
	{
		int boneCount = model.Bones.Length;

		// Load each bone's pose blob (frames are stored contiguously per bone)
		var poses = new FilePose[boneCount][];
		foreach ( var (boneIndex, offset) in anim.BoneCurves )
		{
			if ( boneIndex < 0 || boneIndex >= boneCount )
				continue;

			r.Seek( offset );
			r.Check( (long)anim.NumFrames * PoseSize );
			var blob = new FilePose[anim.NumFrames];
			for ( int frame = 0; frame < blob.Length; frame++ ) blob[frame] = r.ReadPose();
			poses[boneIndex] = blob;
		}

		for ( int frame = 0; frame < anim.NumFrames; frame++ )
		{
			for ( int b = 0; b < boneCount; b++ )
			{
				if ( poses[b] is not { } blob )
				{
					boneTransforms[b] = model.Bones[b].LocalTransform;
					continue;
				}

				var pose = blob[frame];
				var pos = ConvertPosition( pose.Position );
				var rot = ConvertRotation( ToRotation( pose.Rotation ) );
				var tx = new Transform( pos, rot );
				boneTransforms[b] = tx.IsValid ? tx : model.Bones[b].LocalTransform;
			}

			animBuilder.AddFrame( boneTransforms.AsSpan() );
		}
	}

	private static int ResolveAnimation( AnimationNode[] nodes, int nodeIndex )
	{
		// Walk to the first type-1 (play animation) descendant.
		for ( int depth = 0; depth < 16; depth++ )
		{
			if ( nodeIndex < 0 || nodeIndex >= nodes.Length ) return -1;
			var node = nodes[nodeIndex];
			if ( node.Type == 1 ) return node.AnimationIndex;
			if ( node.Children is not { Length: > 0 } ) return -1;
			nodeIndex = node.Children[0];
		}
		return -1;
	}

	protected override object Load()
	{
		var model = ParseFile( FullPath );

		// Skin variants store only mesh data and reference a base model for animations.
		var animSourcePath = FullPath;
		if ( !string.IsNullOrEmpty( model.ExternalModel ) )
		{
			var externalPath = ResolveExternalPath( model.ExternalModel );
			if ( externalPath is not null )
			{
				var external = ParseFile( externalPath );
				if ( model.Animations.Length == 0 && external.Animations.Length > 0 )
				{
					model.Animations = external.Animations;
					model.AnimationNodes = external.AnimationNodes;
					model.Sequences = external.Sequences;
					model.BlendParameters = external.BlendParameters;
					animSourcePath = externalPath;
				}
			}
			else
			{
				Log.Warning( $"NS2 model: could not resolve external model '{model.ExternalModel}'" );
			}
		}

		var builder = Model.Builder.WithName( Path );

		foreach ( var attachment in model.AttachPoints )
		{
			var parentName = attachment.BoneIndex >= 0 && attachment.BoneIndex < model.Bones.Length
				? model.Bones[attachment.BoneIndex].Name : null;
			builder.AddAttachment( attachment.Name, attachment.Transform.Position, attachment.Transform.Rotation, parentName );
		}

		foreach ( var bone in model.Bones )
		{
			var parentName = bone.Parent >= 0 && bone.Parent < model.Bones.Length
				? model.Bones[bone.Parent].Name : null;
			builder.AddBone( bone.Name, bone.Transform.Position, bone.Transform.Rotation, parentName );
		}

		if ( model.Animations.Length > 0 && model.Bones.Length > 0 )
		{
			using var animSource = Host.FileSystem.OpenRead( animSourcePath );
			BuildAnimations( builder, model, animSource );
		}
		BuildMeshes( builder, model );
		BuildPhysics( builder, model );

		// trace mesh from render geometry so editor traces (selection etc.) hit what you see
		if ( Application.IsEditor )
			AddRenderTraceMesh( builder, model );

		return builder.Create();
	}

	private string ResolveExternalPath( string relative )
	{
		// The reference is relative to the game content root (e.g. "models/marine/...").
		// Walk up from this model's directory until the referenced file exists.
		var normalized = relative.Replace( '\\', '/' ).TrimStart( '/' );
		var dir = FullPath;
		while ( true )
		{
			var slash = dir.LastIndexOf( '/' );
			dir = slash < 0 ? "" : dir[..slash];
			var candidate = dir.Length == 0 ? normalized : $"{dir}/{normalized}";
			if ( Host.FileSystem.FileExists( candidate ) ) return candidate;
			if ( dir.Length == 0 ) return null;
		}
	}

	private void BuildMeshes( ModelBuilder builder, ParsedModel model )
	{
		var vertices = model.Vertices;
		var indices = model.Indices;
		var marker = new byte[vertices.Length];
		var remap = new int[vertices.Length];

		foreach ( var faceSet in model.FaceSets )
		{
			Array.Clear( marker, 0, marker.Length );

			var submeshIndices = indices.AsSpan( faceSet.FirstFace * 3, faceSet.NumFaces * 3 );
			var uniqueIndices = new int[submeshIndices.Length];
			var uniqueCount = 0;
			for ( int i = 0; i < submeshIndices.Length; i++ )
			{
				var idx = submeshIndices[i];
				if ( marker[idx] == 0 )
				{
					marker[idx] = 1;
					uniqueIndices[uniqueCount++] = idx;
				}
			}

			for ( int i = 0; i < uniqueCount; i++ ) remap[uniqueIndices[i]] = i;

			var palette = faceSet.BonePalette;
			var bounds = new BBox { Mins = float.MaxValue, Maxs = float.MinValue };
			var subVertices = new SkinnedVertex[uniqueCount];
			for ( int i = 0; i < uniqueCount; i++ )
			{
				var v = vertices[uniqueIndices[i]];
				v.blendIndices = new Color32(
					(byte)(v.blendIndices.r < palette.Length ? palette[v.blendIndices.r] : 255),
					(byte)(v.blendIndices.g < palette.Length ? palette[v.blendIndices.g] : 255),
					(byte)(v.blendIndices.b < palette.Length ? palette[v.blendIndices.b] : 255),
					(byte)(v.blendIndices.a < palette.Length ? palette[v.blendIndices.a] : 255)
				);
				subVertices[i] = v;
				bounds = bounds.AddPoint( v.position );
			}

			var remappedIndices = new int[submeshIndices.Length];
			for ( int i = 0; i < submeshIndices.Length; i++ ) remappedIndices[i] = remap[submeshIndices[i]];

			var materialName = faceSet.MaterialIndex >= 0 && faceSet.MaterialIndex < model.Materials.Length
				? model.Materials[faceSet.MaterialIndex] : null;
			var material = string.IsNullOrWhiteSpace( materialName )
				? DefaultMaterial : Material.Load( $"mount://{Host.Ident}/ns2/{materialName}.vmat" );
			var mesh = new Mesh( material )
			{
				Bounds = bounds
			};
			mesh.CreateVertexBuffer( subVertices.Length, subVertices );
			mesh.CreateIndexBuffer( remappedIndices.Length, remappedIndices );
			builder.AddMesh( mesh );
		}
	}

	private static void BuildPhysics( ModelBuilder builder, ParsedModel model )
	{
		if ( model.CollisionRepBodyCount <= 0 || model.Solids.Length == 0 )
			return;

		var bodyMap = new Dictionary<int, int>();
		var builders = new Dictionary<int, (PhysicsBodyBuilder Builder, int Index)>();

		int builderIndex = 0;
		int bodyCount = Math.Min( model.CollisionRepBodyCount, model.Solids.Length );

		for ( int i = 0; i < bodyCount; i++ )
		{
			var solid = model.Solids[i];
			var boneIndex = solid.BoneIndex;
			var points = solid.Points.ToArray();
			var transform = solid.Transform;

			if ( boneIndex >= model.Bones.Length )
				boneIndex = -1;

			if ( model.Joints.Length == 0 )
			{
				if ( boneIndex >= 0 )
					transform = model.Bones[boneIndex].Transform.ToWorld( transform );

				boneIndex = -1;
			}

			if ( !builders.TryGetValue( boneIndex, out var body ) )
			{
				body = (builder.AddBody(), builderIndex++);
				builders[boneIndex] = body;
			}

			for ( int v = 0; v < points.Length; v++ )
				points[v] = transform.PointToWorld( points[v] );

			bodyMap[i] = body.Index;

			body.Builder.BoneName = boneIndex >= 0 ? model.Bones[boneIndex].Name : "";
			body.Builder.AddHull( points, Transform.Zero, new PhysicsBodyBuilder.HullSimplify
			{
				AngleTolerance = 0.1f,
				DistanceTolerance = 0.1f,
				Method = PhysicsBodyBuilder.SimplifyMethod.QEM
			} );
		}

		foreach ( var joint in model.Joints )
		{
			if ( !bodyMap.TryGetValue( joint.Body0, out var body0 ) ) continue;
			if ( !bodyMap.TryGetValue( joint.Body1, out var body1 ) ) continue;

			if ( body0 == body1 )
				continue;

			var frame0 = model.Solids[joint.Body0].Transform.ToWorld( joint.Frame0 );
			var frame1 = model.Solids[joint.Body1].Transform.ToWorld( joint.Frame1 );

			float swing = MathF.Max( MathF.Abs( joint.SwingMin ), MathF.Abs( joint.SwingMax ) ).RadianToDegree();
			float twistLow = -MathF.Max( joint.TwistMin, joint.TwistMax ).RadianToDegree();
			float twistHigh = -MathF.Min( joint.TwistMin, joint.TwistMax ).RadianToDegree();

			builder.AddBallJoint( body0, body1, frame0, frame1 )
				.WithSwingLimit( swing )
				.WithTwistLimit( twistLow, twistHigh );
		}
	}

	// Trace mesh from render geometry (bind pose) so editor traces hit what you see
	private static void AddRenderTraceMesh( ModelBuilder builder, ParsedModel model )
	{
		if ( model.Vertices.Length == 0 || model.Indices.Length == 0 || model.FaceSets.Length == 0 )
			return;

		// face sets partition the index buffer; triangle order doesn't matter for tracing,
		// so if they cover it fully, reuse it as-is (zero copy)
		int total = 0;
		foreach ( var faceSet in model.FaceSets )
			total += faceSet.NumFaces * 3;

		if ( total == 0 )
			return;

		var indices = model.Indices;
		if ( total != indices.Length )
		{
			indices = new int[total];
			int offset = 0;
			foreach ( var faceSet in model.FaceSets )
			{
				var span = model.Indices.AsSpan( faceSet.FirstFace * 3, faceSet.NumFaces * 3 );
				span.CopyTo( indices.AsSpan( offset ) );
				offset += span.Length;
			}
		}

		// positions only — one tight pass over the interleaved vertex data
		var vertices = new Vector3[model.Vertices.Length];
		for ( int i = 0; i < vertices.Length; i++ )
			vertices[i] = model.Vertices[i].position;

		builder.AddTraceMesh( vertices, indices );
	}

	// Spark → s&box: y-up right-handed → z-up, ×40
	private const float Scale = 40.0f;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static Vector3 ConvertPosition( Vector3 v ) => new Vector3( v.z, v.x, v.y ) * Scale;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static Vector3 ConvertDirection( Vector3 v ) => new( v.z, v.x, v.y );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static Rotation ConvertRotation( Rotation r ) => new( r.z, r.x, r.y, r.w );

	private static Transform PoseToTransform( in FilePose pose )
	{
		var pos = ConvertPosition( pose.Position );
		var rot = ConvertRotation( ToRotation( pose.Rotation ) );
		var scale = ConvertDirection( pose.Scale ) * pose.UniformScale;
		return new Transform( pos, rot, scale );
	}

	private static Transform ConvertCoords( in FileCoords coords )
	{
		var m1 = ConvertDirection( coords.XAxis );
		var m2 = ConvertDirection( coords.YAxis );
		var m0 = ConvertDirection( coords.ZAxis );
		var pos = ConvertPosition( coords.Origin );

		var mat = new Matrix(
			m0.x, m0.y, m0.z, 0,
			m1.x, m1.y, m1.z, 0,
			m2.x, m2.y, m2.z, 0,
			pos.x, pos.y, pos.z, 1
		);

		Matrix4x4.Decompose( mat, out var scale, out var rot, out var pos2 );
		return new Transform { Position = pos2, Rotation = rot, Scale = scale };
	}

	private static Color32 NormalizeWeights( float w0, float w1, float w2, float w3 )
	{
		var iw0 = (int)((w0 * 255) + 0.5f);
		var iw1 = (int)((w1 * 255) + 0.5f);
		var iw2 = (int)((w2 * 255) + 0.5f);
		var iw3 = (int)((w3 * 255) + 0.5f);

		var diff = 255 - (iw0 + iw1 + iw2 + iw3);
		if ( diff != 0 )
		{
			var max = Math.Max( Math.Max( iw0, iw1 ), Math.Max( iw2, iw3 ) );
			if ( iw0 == max ) iw0 += diff;
			else if ( iw1 == max ) iw1 += diff;
			else if ( iw2 == max ) iw2 += diff;
			else iw3 += diff;
		}

		return new Color32( (byte)iw0, (byte)iw1, (byte)iw2, (byte)iw3 );
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct SkinnedVertex( Vector3 position, Vector3 normal, Vector3 tangent, Vector2 texcoord, Color32 blendIndices, Color32 blendWeights )
	{
		[VertexLayout.Position] public Vector3 position = position;
		[VertexLayout.Normal] public Vector3 normal = normal;
		[VertexLayout.Tangent] public Vector3 tangent = tangent;
		[VertexLayout.TexCoord] public Vector2 texcoord = texcoord;
		[VertexLayout.BlendIndices] public Color32 blendIndices = blendIndices;
		[VertexLayout.BlendWeight] public Color32 blendWeights = blendWeights;

		public static readonly VertexAttribute[] Layout =
		[
			new( VertexAttributeType.Position, VertexAttributeFormat.Float32, 3 ),
			new( VertexAttributeType.Normal, VertexAttributeFormat.Float32, 3 ),
			new( VertexAttributeType.Tangent, VertexAttributeFormat.Float32, 3 ),
			new( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2 ),
			new( VertexAttributeType.BlendIndices, VertexAttributeFormat.UInt8, 4 ),
			new( VertexAttributeType.BlendWeights, VertexAttributeFormat.UInt8, 4 )
		];
	}
}
