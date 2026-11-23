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
		BoundingBox = 17,    // model-space center and half-extents
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
		public BBox Bounds;              // bone-local render bounds
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
		public int BoneIndex;
		public Transform Transform;
		public Vector3[] Points;   // converted to s&box space
	}

	struct Joint
	{
		public int Body0, Body1;
		public Transform Frame0, Frame1;
		public Vector3 Minimum, Maximum;
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

		private int StringLength()
		{
			int len = Int();
			if ( (uint)len > 0x10000 ) throw new InvalidDataException( $"NS2 model: bad string length {len}" );
			return len;
		}

		public string String() => Encoding.ASCII.GetString( Take( StringLength() ) );
		public void SkipString() => Skip( StringLength() );

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

	private readonly object sourceLock = new();
	private ParsedModel Source
	{
		get
		{
			lock ( sourceLock )
				return field ??= ParseFile( FullPath );
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
				case ChunkType.Sequences: ReadSequences( ref r, model ); break;
				case ChunkType.BlendParameters: ReadBlendParameters( ref r, model ); break;
				case ChunkType.Cameras: ReadCameras( ref r ); break;
				case ChunkType.HitProxies: ReadHitProxies( ref r ); break;
				case ChunkType.AttachPoints: ReadAttachPoints( ref r, model ); break;
				case ChunkType.Joints: ReadJoints( ref r, model ); break;
				case ChunkType.CollisionPairs: r.Skip( (long)r.Int() * 8 ); break;
				case ChunkType.CollisionReps: ReadCollisionReps( ref r, model, version ); break;
				case ChunkType.BoundingBox: r.Skip( 6 * 4 ); break;
				case ChunkType.BoneBounds:
					for ( int i = 0; i < model.Bones.Length; i++ )
					{
						var center = ConvertPosition( r.ReadVector3() );
						var extent = ConvertPosition( r.ReadVector3() );
						model.Bones[i].Bounds = new BBox( center - extent, center + extent );
					}
					break;
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
			var color = ConvertColor( (uint)r.Int() );
			float w0 = r.Float(); int b0 = r.Int();
			float w1 = r.Float(); int b1 = r.Int();
			float w2 = r.Float(); int b2 = r.Int();
			float w3 = r.Float(); int b3 = r.Int();

			dst[i] = new SkinnedVertex(
				ConvertPosition( position ),
				ConvertDirection( normal ),
				ConvertDirection( tangent ),
				texcoord,
				color,
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
			r.SkipString(); // solid name
			solids[i].BoneIndex = r.Int();
			solids[i].Transform = ConvertCoords( r.ReadCoords() );
			_ = r.Float(); // mass

			int pointCount = r.Int();
			r.Check( (long)pointCount * 12 );
			var points = new Vector3[pointCount];
			for ( int p = 0; p < points.Length; p++ ) points[p] = ConvertPosition( r.ReadVector3() );
			solids[i].Points = points;

			r.Skip( (long)r.Int() * 3 * sizeof( int ) ); // hulls only use the points
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
			r.Skip( sizeof( int ) ); // flags
			var anim = new ParsedAnimation
			{
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
					r.SkipString();
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

	private static void ReadSequences( ref Reader r, ParsedModel model )
	{
		int count = r.Int();
		var seqs = new Sequence[count];
		for ( int i = 0; i < count; i++ )
		{
			seqs[i].Name = r.String();
			seqs[i].AnimationNodeIndex = r.Int();
			r.Skip( sizeof( float ) ); // length (present in both supported versions)
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
			r.SkipString();
			r.Skip( 8 ); // bone index, fov
			r.Skip( CoordsSize );
		}
	}

	private static void ReadHitProxies( ref Reader r )
	{
		int count = r.Int();
		for ( int i = 0; i < count; i++ )
		{
			r.SkipString();
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
			r.SkipString(); // joint name
			int body0 = r.Int();
			var frame0 = ConvertCoords( r.ReadCoords() );
			int body1 = r.Int();
			var frame1 = ConvertCoords( r.ReadCoords() );

			// flag != 0 → engine discards the joint and no limits follow
			if ( r.Int() != 0 )
				continue;

			var minimum = r.ReadVector3();
			var maximum = r.ReadVector3();
			joints.Add( new Joint
			{
				Body0 = body0,
				Body1 = body1,
				Frame0 = frame0,
				Frame1 = frame1,
				Minimum = minimum,
				Maximum = maximum,
			} );
		}
		model.Joints = joints.ToArray();
	}

	private static void ReadCollisionReps( ref Reader r, ParsedModel model, int version )
	{
		int count = r.Int();
		for ( int i = 0; i < count; i++ )
		{
			if ( version < 5 ) r.SkipString();
			int bodyCount = r.Int();
			r.Skip( (version >= 5 ? 5 : 3) * 4 );
			if ( i == 0 ) model.CollisionRepBodyCount = bodyCount;
		}

		if ( version >= 5 )
		{
			int nameCount = r.Int();
			for ( int i = 0; i < nameCount; i++ )
			{
				r.SkipString();
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

	Dictionary<string, string> sequenceAliases;
	internal string ResolveSequenceName( string name ) => sequenceAliases?.GetValueOrDefault( name ) ?? name;

	internal PropAnimationGraph PropGraph => field ??= LoadPropGraph();

	PropAnimationGraph LoadPropGraph()
	{
		var path = System.IO.Path.ChangeExtension( FullPath, ".animation_graph" );
		if ( Host.FileSystem.FileExists( path ) )
		{
			try
			{
				return PropAnimationGraph.Read( Host.ReadAllBytes( path ) );
			}
			catch ( InvalidDataException e )
			{
				Log.Warning( e, $"Could not read prop animation graph {path}." );
			}
		}
		return new();
	}

	private void BuildAnimations( ModelBuilder builder, ParsedModel model, ParsedModel animations, Stream animSource )
	{
		if ( animations.Animations.Length == 0 || model.Bones.Length == 0 )
			return;

		// Name animations after sequences where possible:
		// sequence → node → first type-1 descendant → animation
		var names = new string[animations.Animations.Length];
		foreach ( var seq in animations.Sequences )
		{
			int animIndex = ResolveAnimation( animations.AnimationNodes, seq.AnimationNodeIndex );
			if ( (uint)animIndex >= names.Length ) continue;
			if ( names[animIndex] is null )
				names[animIndex] = seq.Name;
			else
				(sequenceAliases ??= new( StringComparer.OrdinalIgnoreCase ))[seq.Name] = names[animIndex];
		}

		// The rest are blend-node poses — name them "{parameter}_{value}" (e.g. arc_yaw_-90)
		// from the blend node that references them so they're usable in animgraphs.
		foreach ( var node in animations.AnimationNodes )
		{
			if ( node.Type != 2 || node.Children is not { Length: > 0 } )
				continue;

			var param = (uint)node.ParameterIndex < (uint)animations.BlendParameters.Length
				? animations.BlendParameters[node.ParameterIndex] : $"blend{node.ParameterIndex}";

			for ( int k = 0; k < node.Children.Length; k++ )
			{
				int animIndex = ResolveAnimation( animations.AnimationNodes, node.Children[k] );
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

		for ( int i = 0; i < animations.Animations.Length; i++ )
		{
			var anim = animations.Animations[i];
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
		var model = Source;
		var animations = model;

		// Skin variants store only mesh data and reference a base model for animations.
		var animSourcePath = FullPath;
		if ( model.Animations.Length == 0 && !string.IsNullOrEmpty( model.ExternalModel ) )
		{
			var externalPath = ResolveExternalPath( model.ExternalModel );
			if ( externalPath is not null )
			{
				var loader = Host.GetByPath( $"mount://{Host.Ident}/{externalPath}.vmdl" ) as ModelLoader;
				animations = loader?.Source ?? ParseFile( externalPath );
				animSourcePath = externalPath;
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
			builder.AddBone( bone.Name, bone.Transform.Position, bone.Transform.Rotation, bone.Bounds, parentName );
		}

		if ( animations.Animations.Length > 0 && model.Bones.Length > 0 )
		{
			using var animSource = Host.FileSystem.OpenRead( animSourcePath );
			BuildAnimations( builder, model, animations, animSource );
		}
		BuildMeshes( builder, model );
		BuildPhysics( builder, model, animations.Animations.Length > 0 || model.Joints.Length > 0 );

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

	internal sealed record StaticProp( StaticGeometry.Part[] Parts, Model Translucent, Vector3[][] Hulls, Surface Surface );

	internal StaticProp BuildStaticProp()
	{
		var model = Source;
		var parts = new List<StaticGeometry.Part>();
		ModelBuilder translucent = null;

		foreach ( var (_, material, vertices, indices, bounds) in MeshParts( model ) )
		{
			var data = Array.ConvertAll( vertices, v => new WorldVertex( v.position, v.normal, v.tangent, v.texcoord ) { color = v.color } );
			if ( !material.Flags.IsTranslucent )
			{
				parts.Add( new( material, data, indices, bounds ) );
				continue;
			}

			translucent ??= Model.Builder.WithName( $"{FullPath}/translucent" );
			var mesh = new Mesh( material ) { Bounds = bounds };
			mesh.CreateVertexBuffer( data.Length, data );
			mesh.CreateIndexBuffer( indices.Length, indices );
			translucent.AddMesh( mesh );
		}

		// Simplify in model space once so repeated placements use the same convex surface.
		var hulls = StaticHulls( model );
		return new( parts.ToArray(), translucent?.Create(), hulls, hulls.Length > 0 ? CollisionSurface( model ) : null );
	}

	private Vector3[][] StaticHulls( ParsedModel model )
	{
		if ( model.CollisionRepBodyCount <= 0 || model.Solids.Length == 0 ) return [];
		var body = new PhysicsBodyBuilder();
		foreach ( var (_, points) in CollisionHulls( model, false ) )
			body.AddHull( points, Transform.Zero, HullSimplification );

		var physics = PhysicsGroupDescription.Create( $"{FullPath}/hulls.vphys", [body] );
		return physics.Parts.SelectMany( part => part.Hulls ).Select( hull => hull.GetPoints().ToArray() ).ToArray();
	}

	private IEnumerable<(FaceSet Set, Material Material, SkinnedVertex[] Vertices, int[] Indices, BBox Bounds)> MeshParts( ParsedModel model )
	{
		var remap = new int[model.Vertices.Length];
		Array.Fill( remap, -1 );
		foreach ( var set in model.FaceSets )
		{
			if ( set.NumFaces == 0 ) continue;
			var vertices = new List<SkinnedVertex>();
			var indices = new int[set.NumFaces * 3];
			var bounds = new BBox { Mins = float.MaxValue, Maxs = float.MinValue };
			int first = set.FirstFace * 3;
			for ( int i = 0; i < indices.Length; i++ )
			{
				int index = model.Indices[first + i];
				if ( remap[index] < 0 )
				{
					var v = model.Vertices[index];
					remap[index] = vertices.Count;
					vertices.Add( v );
					bounds = bounds.AddPoint( v.position );
				}
				indices[i] = remap[index];
			}

			for ( int i = 0; i < indices.Length; i++ )
				remap[model.Indices[first + i]] = -1;

			var name = (uint)set.MaterialIndex < (uint)model.Materials.Length ? model.Materials[set.MaterialIndex] : null;
			yield return (set, ResolveMaterial( name ), vertices.ToArray(), indices, bounds);
		}
	}

	private void BuildMeshes( ModelBuilder builder, ParsedModel model )
	{
		foreach ( var (set, material, vertices, indices, bounds) in MeshParts( model ) )
		{
			var palette = set.BonePalette;
			for ( int i = 0; i < vertices.Length; i++ )
			{
				var b = vertices[i].blendIndices;
				vertices[i].blendIndices = new Color32(
					(byte)(b.r < palette.Length ? palette[b.r] : 255),
					(byte)(b.g < palette.Length ? palette[b.g] : 255),
					(byte)(b.b < palette.Length ? palette[b.b] : 255),
					(byte)(b.a < palette.Length ? palette[b.a] : 255) );
			}
			var mesh = new Mesh( material ) { Bounds = bounds };
			mesh.CreateVertexBuffer( vertices.Length, vertices );
			mesh.CreateIndexBuffer( indices.Length, indices );
			builder.AddMesh( mesh );
		}
	}

	private Material ResolveMaterial( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return DefaultMaterial;

		var relative = name.StartsWith( "ns2/", StringComparison.OrdinalIgnoreCase ) ? name : $"ns2/{name}";
		var path = $"mount://{Host.Ident}/{relative}.vmat";

		return Host.GetByPath( path ) is null ? DefaultMaterial : Material.Load( path );
	}

	private static readonly PhysicsBodyBuilder.HullSimplify HullSimplification = new()
	{
		AngleTolerance = 0.1f,
		DistanceTolerance = 0.1f,
		Method = PhysicsBodyBuilder.SimplifyMethod.QEM
	};

	private static IEnumerable<(int Bone, Vector3[] Points)> CollisionHulls( ParsedModel model, bool preserveBones )
	{
		int count = Math.Min( model.CollisionRepBodyCount, model.Solids.Length );
		for ( int i = 0; i < count; i++ )
		{
			var solid = model.Solids[i];
			var bone = (uint)solid.BoneIndex < (uint)model.Bones.Length ? solid.BoneIndex : -1;
			var transform = solid.Transform;
			if ( !preserveBones && bone >= 0 )
			{
				transform = model.Bones[bone].Transform.ToWorld( transform );
				bone = -1;
			}
			yield return (bone, Array.ConvertAll( solid.Points, p => transform.PointToWorld( p ) ));
		}
	}

	// Spark hulls do not carry render-face indices. Use the largest face set with an authored material.
	private Surface CollisionSurface( ParsedModel model )
	{
		int largest = -1;
		string material = null;
		foreach ( var set in model.FaceSets )
		{
			if ( set.NumFaces <= largest || (uint)set.MaterialIndex >= (uint)model.Materials.Length ) continue;
			largest = set.NumFaces;
			material = model.Materials[set.MaterialIndex];
		}
		return Host.MaterialSurface( material );
	}

	private void BuildPhysics( ModelBuilder builder, ParsedModel model, bool preserveBones )
	{
		Surface surface = null;
		var bodyMap = new List<int>();
		var bodies = new Dictionary<int, (PhysicsBodyBuilder Builder, int Index)>();
		foreach ( var (bone, points) in CollisionHulls( model, preserveBones ) )
		{
			if ( !bodies.TryGetValue( bone, out var body ) )
			{
				body = (builder.AddBody( surface: surface ??= CollisionSurface( model ), boneName: bone >= 0 ? model.Bones[bone].Name : "" ), bodies.Count);
				bodies.Add( bone, body );
			}
			body.Builder.AddHull( points, Transform.Zero, HullSimplification );
			bodyMap.Add( body.Index );
		}

		foreach ( var joint in model.Joints )
		{
			if ( (uint)joint.Body0 >= (uint)bodyMap.Count || (uint)joint.Body1 >= (uint)bodyMap.Count ) continue;
			var body0 = bodyMap[joint.Body0];
			var body1 = bodyMap[joint.Body1];

			if ( body0 == body1 )
				continue;

			var frame0 = model.Solids[joint.Body0].Transform.ToWorld( joint.Frame0 );
			var frame1 = model.Solids[joint.Body1].Transform.ToWorld( joint.Frame1 );

			var limits = GetJointLimits( joint.Minimum, joint.Maximum );
			frame0.Rotation *= limits.Basis;
			frame1.Rotation *= limits.Basis;

			if ( limits.Revolute )
			{
				var hinge = builder.AddHingeJoint( body0, body1, frame0, frame1 );
				if ( limits.EnableTwistLimit )
					hinge.WithTwistLimit( limits.Twist.x, limits.Twist.y );
			}
			else
			{
				float swing = MathF.Max( limits.Swing.x, limits.Swing.y );
				if ( swing == 0 && limits.Twist == Vector2.Zero )
				{
					builder.AddFixedJoint( body0, body1, frame0, frame1 );
					continue;
				}
				var ball = builder.AddBallJoint( body0, body1, frame0, frame1 );
				if ( swing < 180 )
					ball.WithSwingLimit( Math.Clamp( swing, 0, 89.1f ) );
				if ( limits.Twist.y - limits.Twist.x < 360 )
					ball.WithTwistLimit( limits.Twist.x, limits.Twist.y );
			}
		}
	}

	private const float JointLimitEpsilon = 1.1920929e-7f;
	private const float MinimumSwingLimit = 0.01f;

	private static (Rotation Basis, Vector2 Twist, Vector2 Swing, bool Revolute, bool EnableTwistLimit) GetJointLimits( Vector3 minimum, Vector3 maximum )
	{
		if ( float.IsNaN( minimum.x ) || float.IsNaN( minimum.y ) || float.IsNaN( minimum.z )
			|| float.IsNaN( maximum.x ) || float.IsNaN( maximum.y ) || float.IsNaN( maximum.z )
			|| minimum.x > maximum.x || minimum.y > maximum.y || minimum.z > maximum.z )
			throw new InvalidDataException( "NS2 model: invalid joint angular limits" );

		var range = maximum - minimum;
		int lockedAxes = (range.x < JointLimitEpsilon ? 1 : 0)
			+ (range.y < JointLimitEpsilon ? 1 : 0)
			+ (range.z < JointLimitEpsilon ? 1 : 0);

		if ( lockedAxes == 3 )
			return (Rotation.Identity, Vector2.Zero, Vector2.Zero, false, false);

		bool revolute = lockedAxes == 2;
		int twistAxis = SelectJointTwistAxis( minimum, maximum, lockedAxes );
		var rebase = Rotation.Identity;
		if ( twistAxis == 1 )
		{
			rebase = Rotation.FromAxis( Vector3.Up, -90 );
			if ( revolute )
				rebase *= Rotation.FromAxis( Vector3.Forward, -90 );
		}
		else if ( twistAxis == 2 )
		{
			rebase = Rotation.FromAxis( Vector3.Left, 90 );
		}

		var basis = ConvertRotation( rebase ) * Rotation.LookAt( Vector3.Up, Vector3.Left );
		if ( twistAxis < 0 )
			return (basis, new( -180, 180 ), revolute ? Vector2.Zero : new( 180, 180 ), revolute, false);

		var (twistMinimum, twistMaximum, swingMinimum, swingMaximum) = twistAxis switch
		{
			0 => (minimum.x, maximum.x, new Vector2( minimum.y, minimum.z ), new Vector2( maximum.y, maximum.z )),
			1 => (minimum.y, maximum.y, new Vector2( minimum.x, minimum.z ), new Vector2( maximum.x, maximum.z )),
			_ => (minimum.z, maximum.z, new Vector2( minimum.y, minimum.x ), new Vector2( maximum.y, maximum.x ))
		};
		var twist = GetTwistLimit( twistMinimum, twistMaximum, revolute );
		var swing = Vector2.Zero;
		if ( !revolute )
		{
			swing.x = GetSwingLimit( swingMinimum.x, swingMaximum.x );
			swing.y = GetSwingLimit( swingMinimum.y, swingMaximum.y );
		}
		return (basis, twist, swing, revolute, revolute);
	}

	private static int SelectJointTwistAxis( Vector3 minimum, Vector3 maximum, int lockedAxes )
	{
		bool freeX = float.IsNegativeInfinity( minimum.x ) || float.IsPositiveInfinity( maximum.x );
		bool freeY = float.IsNegativeInfinity( minimum.y ) || float.IsPositiveInfinity( maximum.y );
		bool freeZ = float.IsNegativeInfinity( minimum.z ) || float.IsPositiveInfinity( maximum.z );

		if ( lockedAxes == 2 )
		{
			if ( !freeX && maximum.x - minimum.x >= JointLimitEpsilon )
				return 0;
			if ( !freeY && maximum.y - minimum.y >= JointLimitEpsilon )
				return 1;
			return freeZ ? -1 : 2;
		}

		var midpoint = minimum + maximum;
		var asymmetry = new Vector3( MathF.Abs( midpoint.x ), MathF.Abs( midpoint.y ), MathF.Abs( midpoint.z ) );
		float asymmetrySquared = asymmetry.LengthSquared;
		float other = asymmetry.y > asymmetry.z ? asymmetry.y : asymmetry.z;
		if ( !freeX && ( asymmetry.x > other || asymmetrySquared < JointLimitEpsilon ) )
			return 0;

		other = asymmetry.x > asymmetry.z ? asymmetry.x : asymmetry.z;
		if ( !freeY && ( other < asymmetry.y || asymmetrySquared < JointLimitEpsilon ) )
			return 1;

		return freeZ ? -1 : 2;
	}

	private static Vector2 GetTwistLimit( float minimum, float maximum, bool revolute )
	{
		if ( float.IsNegativeInfinity( minimum ) || float.IsPositiveInfinity( maximum ) )
			return new( -180, 180 );
		if ( !revolute && maximum - minimum < JointLimitEpsilon )
			return Vector2.Zero;
		return new( minimum.RadianToDegree(), maximum.RadianToDegree() );
	}

	private static float GetSwingLimit( float minimum, float maximum )
	{
		float limit = MathF.Max( MathF.Abs( minimum ), MathF.Abs( maximum ) );
		if ( float.IsPositiveInfinity( limit ) )
			return 180;
		if ( limit < JointLimitEpsilon )
			return 0;
		return MathF.Max( limit, MinimumSwingLimit ).RadianToDegree();
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

	private static Color32 ConvertColor( uint c ) =>
		new Color32( (byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)(c >> 24) );

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
	private struct SkinnedVertex( Vector3 position, Vector3 normal, Vector3 tangent, Vector2 texcoord, Color32 color, Color32 blendIndices, Color32 blendWeights )
	{
		[VertexLayout.Position] public Vector3 position = position;
		[VertexLayout.Normal] public Vector3 normal = normal;
		[VertexLayout.Tangent] public Vector3 tangent = tangent;
		[VertexLayout.TexCoord] public Vector2 texcoord = texcoord;
		[VertexLayout.Color] public Color32 color = color;
		[VertexLayout.BlendIndices] public Color32 blendIndices = blendIndices;
		[VertexLayout.BlendWeight] public Color32 blendWeights = blendWeights;
	}
}
