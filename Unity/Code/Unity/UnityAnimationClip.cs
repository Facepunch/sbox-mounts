using System.Text;
using SysVec3 = System.Numerics.Vector3;
using SysQuat = System.Numerics.Quaternion;

namespace UnityMount;

/// <summary>
/// Decodes Unity 2021.3 AnimationClip (ClassID 74) assets, including StreamedClip cubic splines,
/// DenseClip sample arrays, ConstantClip curves, and AnimationClipBindingConstant (CRC32 bone paths),
/// plus Animator (95) -> AnimatorController (91) / AnimatorOverrideController (221) clip discovery.
/// </summary>
public sealed class UnityAnimationClip
{
	public readonly record struct CurveBinding(
		uint PathHash,
		uint Attribute, // 1=LocalPosition(3), 2=LocalRotation(4), 3=LocalScale(3), 4=LocalEulerAnglesRaw(3)
		int CurveStart,
		int Dimension );

	readonly record struct StreamKey( float Time, float C0, float C1, float C2, float C3 );

	public string Name { get; init; }
	public float SampleRate { get; init; }
	public float StartTime { get; init; }
	public float StopTime { get; init; }
	public float Duration => MathF.Max( 0.01f, StopTime - StartTime );
	public bool IsLooping { get; init; }

	public int TotalCurveCount { get; init; }
	public int StreamedCurveCount { get; init; }
	public int DenseCurveCount { get; init; }
	public int DenseFrameCount { get; init; }
	public float DenseSampleRate { get; init; }
	public float DenseBeginTime { get; init; }

	readonly List<StreamKey>[] _streamCurves;
	readonly float[] _denseSamples;
	readonly float[] _constantValues;

	public Dictionary<uint, BoneCurves> BoneTracks { get; } = [];

	public sealed class BoneCurves
	{
		public int PosStart = -1;
		public int RotQuatStart = -1;
		public int RotEulerStart = -1;
		public int ScaleStart = -1;
	}

	UnityAnimationClip(
		List<StreamKey>[] streamCurves,
		float[] denseSamples,
		float[] constantValues )
	{
		_streamCurves = streamCurves;
		_denseSamples = denseSamples;
		_constantValues = constantValues;
	}

	public static UnityAnimationClip Read( UnityWorkspace workspace, SerializedFile file, UnityObjectInfo obj )
	{
		try
		{
			using var r = workspace.OpenObjectReader( file, obj );
			long endPos = obj.ByteStart + obj.ByteSize;

			string name = r.ReadAlignedString();
			bool legacy = r.ReadBool();
			_ = r.ReadBool(); // m_Compressed
			_ = r.ReadBool(); // m_UseHighQualityCurve
			r.Align( 4 );

			if ( legacy )
				return null;

			// 7 legacy curve arrays (empty for Mecanim/Generic clips)
			for ( int i = 0; i < 7; i++ )
			{
				if ( r.ReadInt32() != 0 )
					return null;
			}

			float sampleRate = r.ReadSingle();
			if ( sampleRate <= 1f || sampleRate > 240f ) sampleRate = 30f;
			_ = r.ReadInt32();  // m_WrapMode
			r.Skip( 24 );       // m_Bounds
			uint muscleSize = r.ReadUInt32();
			if ( muscleSize == 0 )
				return null;

			// m_MuscleClip (ClipMuscleConstant)
			SkipHumanPose( r ); // m_DeltaPose
			SkipXform( r );     // m_StartX
			SkipXform( r );     // m_StopX
			SkipXform( r );     // m_LeftFootStartX
			SkipXform( r );     // m_RightFootStartX
			r.Skip( 12 );       // m_AverageSpeed

			// m_Clip -> StreamedClip
			int scDataLen = r.ReadInt32();
			uint[] scData = scDataLen > 0 ? new uint[scDataLen] : [];
			for ( int i = 0; i < scDataLen; i++ ) scData[i] = r.ReadUInt32();
			int scCurveCount = (int)r.ReadUInt32();

			// m_Clip -> DenseClip
			int dcFrameCount = r.ReadInt32();
			int dcCurveCount = (int)r.ReadUInt32();
			float dcSampleRate = r.ReadSingle();
			float dcBeginTime = r.ReadSingle();
			int dcSampleLen = r.ReadInt32();
			float[] dcSamples = dcSampleLen > 0 ? new float[dcSampleLen] : [];
			for ( int i = 0; i < dcSampleLen; i++ ) dcSamples[i] = r.ReadSingle();

			// m_Clip -> ConstantClip
			int ccLen = r.ReadInt32();
			float[] ccValues = ccLen > 0 ? new float[ccLen] : [];
			for ( int i = 0; i < ccLen; i++ ) ccValues[i] = r.ReadSingle();

			float startTime = r.ReadSingle();
			float stopTime = r.ReadSingle();
			r.Skip( 16 ); // m_OrientationOffsetY, m_Level, m_CycleOffset, m_AverageAngularSpeed

			int idxCnt = r.ReadInt32();
			r.Skip( (long)idxCnt * 4 );

			int vdCnt = r.ReadInt32();
			r.Skip( (long)vdCnt * 8 );

			int rpCnt = r.ReadInt32();
			r.Skip( (long)rpCnt * 4 );

			// Flags block (12 bytes: m_HasAdditiveReferencePose, m_LoopTime, m_LoopBlend, ...)
			byte[] flags = r.ReadBytes( 12 );
			bool isLooping = flags.Length > 1 && flags[1] != 0;

			if ( r.Position + 4 > endPos )
				return null;

			// m_ClipBindingConstant -> genericBindings (28 bytes each in Unity 2021.3)
			int bindCount = r.ReadInt32();
			if ( bindCount <= 0 || r.Position + (long)bindCount * 28 > endPos )
				return null;

			// Decode StreamedClip into per-curve sorted keyframe lists for O(log K) fast evaluation
			var streamCurves = DecodeStreamedClip( scData, scCurveCount );

			var clip = new UnityAnimationClip( streamCurves, dcSamples, ccValues )
			{
				Name = name,
				SampleRate = sampleRate,
				StartTime = startTime,
				StopTime = stopTime,
				IsLooping = isLooping,
				StreamedCurveCount = scCurveCount,
				DenseCurveCount = dcCurveCount,
				DenseFrameCount = dcFrameCount,
				DenseSampleRate = dcSampleRate,
				DenseBeginTime = dcBeginTime,
				TotalCurveCount = scCurveCount + dcCurveCount + ccLen,
			};

			int curveCursor = 0;
			for ( int i = 0; i < bindCount; i++ )
			{
				uint pathHash = r.ReadUInt32();
				uint attribute = r.ReadUInt32();
				_ = r.ReadPPtr();   // script
				uint typeId = r.ReadUInt32();
				_ = r.ReadByte();   // customType
				_ = r.ReadByte();   // isPPtrCurve
				r.Skip( 2 );        // padding to 28 bytes

				int dim = typeId == UnityClassId.Transform
					? attribute switch
					{
						1 => 3, // LocalPosition
						2 => 4, // LocalRotation (Quaternion)
						3 => 3, // LocalScale
						4 => 3, // LocalEulerAnglesRaw (degrees)
						_ => 1
					}
					: 1;

				if ( typeId == UnityClassId.Transform && attribute is 1 or 2 or 3 or 4 )
				{
					if ( !clip.BoneTracks.TryGetValue( pathHash, out var bt ) )
						clip.BoneTracks[pathHash] = bt = new BoneCurves();

					switch ( attribute )
					{
						case 1: bt.PosStart = curveCursor; break;
						case 2: bt.RotQuatStart = curveCursor; break;
						case 3: bt.ScaleStart = curveCursor; break;
						case 4: bt.RotEulerStart = curveCursor; break;
					}
				}

				curveCursor += dim;
			}

			return clip;
		}
		catch
		{
			return null;
		}
	}

	static List<StreamKey>[] DecodeStreamedClip( uint[] data, int curveCount )
	{
		var curves = new List<StreamKey>[curveCount];
		if ( data.Length == 0 || curveCount <= 0 )
			return curves;

		int p = 0;
		int n = data.Length;
		while ( p + 2 <= n )
		{
			float time = BitConverter.UInt32BitsToSingle( data[p] );
			int keyCount = (int)data[p + 1];
			p += 2;

			if ( keyCount < 0 || p + keyCount * 5 > n )
				break;

			bool validTime = !float.IsInfinity( time ) && !float.IsNaN( time ) && time > -1e10f && time < 1e10f;

			for ( int k = 0; k < keyCount; k++ )
			{
				int idx = (int)data[p];
				if ( validTime && (uint)idx < (uint)curveCount )
				{
					float c0 = BitConverter.UInt32BitsToSingle( data[p + 1] );
					float c1 = BitConverter.UInt32BitsToSingle( data[p + 2] );
					float c2 = BitConverter.UInt32BitsToSingle( data[p + 3] );
					float c3 = BitConverter.UInt32BitsToSingle( data[p + 4] );
					curves[idx] ??= [];
					curves[idx].Add( new StreamKey( time, c0, c1, c2, c3 ) );
				}
				p += 5;
			}
		}

		return curves;
	}

	/// <summary>Evaluates a single scalar animation curve at time t (in clip seconds).</summary>
	public float EvaluateCurve( int curveIndex, float time )
	{
		if ( curveIndex < 0 )
			return 0f;

		// 1. StreamedClip cubic polynomial
		if ( curveIndex < StreamedCurveCount )
		{
			var keys = _streamCurves[curveIndex];
			if ( keys is null || keys.Count == 0 )
				return 0f;

			if ( time <= keys[0].Time )
				return keys[0].C3;

			// Binary search for the last keyframe with key.Time <= time
			int lo = 0;
			int hi = keys.Count - 1;
			while ( lo < hi )
			{
				int mid = (lo + hi + 1) >> 1;
				if ( keys[mid].Time <= time ) lo = mid;
				else hi = mid - 1;
			}

			var key = keys[lo];
			float dt = time - key.Time;
			// Clamp dt if past the very last keyframe so cubic polynomial doesn't extrapolate wildly
			if ( lo == keys.Count - 1 )
				dt = 0f;
			return ((key.C0 * dt + key.C1) * dt + key.C2) * dt + key.C3;
		}

		// 2. DenseClip uniform-rate float samples
		int denseIdx = curveIndex - StreamedCurveCount;
		if ( denseIdx < DenseCurveCount )
		{
			if ( DenseFrameCount <= 0 || _denseSamples.Length == 0 )
				return 0f;

			float f = (time - DenseBeginTime) * DenseSampleRate;
			if ( f <= 0f )
				return _denseSamples[denseIdx];
			if ( f >= DenseFrameCount - 1 )
				return _denseSamples[(DenseFrameCount - 1) * DenseCurveCount + denseIdx];

			int f0 = (int)MathF.Floor( f );
			int f1 = Math.Min( f0 + 1, DenseFrameCount - 1 );
			float frac = f - f0;
			float v0 = _denseSamples[f0 * DenseCurveCount + denseIdx];
			float v1 = _denseSamples[f1 * DenseCurveCount + denseIdx];
			return v0 + (v1 - v0) * frac;
		}

		// 3. ConstantClip constant values
		int constIdx = denseIdx - DenseCurveCount;
		if ( (uint)constIdx < (uint)_constantValues.Length )
			return _constantValues[constIdx];

		return 0f;
	}

	/// <summary>Evaluates a bone's animated local TRS in Unity space at time t, keeping rest defaults for unbound channels.</summary>
	public void EvaluateBone(
		uint pathHash,
		float time,
		ref SysVec3 pos,
		ref SysQuat rot,
		ref SysVec3 scale )
	{
		if ( !BoneTracks.TryGetValue( pathHash, out var bt ) )
			return;

		if ( bt.PosStart >= 0 )
		{
			float px = EvaluateCurve( bt.PosStart + 0, time );
			float py = EvaluateCurve( bt.PosStart + 1, time );
			float pz = EvaluateCurve( bt.PosStart + 2, time );
			if ( IsFinite( px ) && IsFinite( py ) && IsFinite( pz ) )
				pos = new SysVec3( px, py, pz );
		}

		if ( bt.RotQuatStart >= 0 )
		{
			float qx = EvaluateCurve( bt.RotQuatStart + 0, time );
			float qy = EvaluateCurve( bt.RotQuatStart + 1, time );
			float qz = EvaluateCurve( bt.RotQuatStart + 2, time );
			float qw = EvaluateCurve( bt.RotQuatStart + 3, time );
			var q = new SysQuat( qx, qy, qz, qw );
			float lenSq = q.LengthSquared();
			if ( IsFinite( lenSq ) && lenSq > 1e-8f )
				rot = SysQuat.Normalize( q );
		}
		else if ( bt.RotEulerStart >= 0 )
		{
			float ex = EvaluateCurve( bt.RotEulerStart + 0, time );
			float ey = EvaluateCurve( bt.RotEulerStart + 1, time );
			float ez = EvaluateCurve( bt.RotEulerStart + 2, time );
			if ( IsFinite( ex ) && IsFinite( ey ) && IsFinite( ez ) )
				rot = UnityEulerDegreesToQuaternion( ex, ey, ez );
		}

		if ( bt.ScaleStart >= 0 )
		{
			float sx = EvaluateCurve( bt.ScaleStart + 0, time );
			float sy = EvaluateCurve( bt.ScaleStart + 1, time );
			float sz = EvaluateCurve( bt.ScaleStart + 2, time );
			if ( IsFinite( sx ) && IsFinite( sy ) && IsFinite( sz ) )
				scale = new SysVec3( sx, sy, sz );
		}
	}

	/// <summary>Unity localEulerAnglesRaw (OrderZXY): verified exact against Unity Transform bindRot quaternions.</summary>
	public static SysQuat UnityEulerDegreesToQuaternion( float xDeg, float yDeg, float zDeg )
	{
		const float Deg2Rad = MathF.PI / 180f;
		float hx = xDeg * Deg2Rad * 0.5f;
		float hy = yDeg * Deg2Rad * 0.5f;
		float hz = zDeg * Deg2Rad * 0.5f;

		float cx = MathF.Cos( hx ), sx = MathF.Sin( hx );
		float cy = MathF.Cos( hy ), sy = MathF.Sin( hy );
		float cz = MathF.Cos( hz ), sz = MathF.Sin( hz );

		return SysQuat.Normalize( new SysQuat(
			cy * sx * cz - sy * cx * sz,
			sy * cx * cz + cy * sx * sz,
			cy * cx * sz - sy * sx * cz,
			cy * cx * cz + sy * sx * sz
		) );
	}

	static bool IsFinite( float v ) => !float.IsNaN( v ) && !float.IsInfinity( v );

	static void SkipXform( UnityBinaryReader r ) => r.Skip( 40 );

	static void SkipHandPose( UnityBinaryReader r )
	{
		SkipXform( r );
		int n = r.ReadInt32();
		r.Skip( (long)n * 4 );
		r.Skip( 16 );
	}

	static void SkipHumanPose( UnityBinaryReader r )
	{
		SkipXform( r );
		r.Skip( 28 ); // m_LookAtPosition (12) + m_LookAtWeight (16)
		int goalCount = r.ReadInt32();
		for ( int i = 0; i < goalCount; i++ )
		{
			SkipXform( r );
			r.Skip( 24 ); // WeightT, WeightR, HintT(12), HintWeightT
		}
		SkipHandPose( r );
		SkipHandPose( r );
		int dofCount = r.ReadInt32();
		r.Skip( (long)dofCount * 4 );
		int tdofCount = r.ReadInt32();
		r.Skip( (long)tdofCount * 12 );
	}

	// Standard IEEE 802.3 CRC32 (matches Unity's AnimationClip path hashing)
	static readonly uint[] Crc32Table = BuildCrc32Table();

	static uint[] BuildCrc32Table()
	{
		var t = new uint[256];
		for ( uint i = 0; i < 256; i++ )
		{
			uint c = i;
			for ( int k = 0; k < 8; k++ )
				c = (c & 1) != 0 ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
			t[i] = c;
		}
		return t;
	}

	public static uint ComputeCrc32( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return 0u;

		byte[] bytes = Encoding.UTF8.GetBytes( path );
		uint crc = 0xFFFFFFFFu;
		for ( int i = 0; i < bytes.Length; i++ )
			crc = Crc32Table[(crc ^ bytes[i]) & 0xFF] ^ (crc >> 8);
		return crc ^ 0xFFFFFFFFu;
	}

	/// <summary>
	/// Extracts all AnimationClip PPtrs referenced by an Animator component (ClassID 95)
	/// via its AnimatorController (91) or AnimatorOverrideController (221).
	/// </summary>
	public static List<(SerializedFile File, UnityObjectInfo Obj)> ResolveAnimatorClips(
		UnityWorkspace workspace,
		SerializedFile file,
		UnityObjectInfo animatorObj )
	{
		var results = new List<(SerializedFile, UnityObjectInfo)>();
		var seen = new HashSet<AssetKey>();

		try
		{
			PPtr ctrlPtr;
			using ( var r = workspace.OpenObjectReader( file, animatorObj ) )
			{
				_ = r.ReadPPtr(); // m_GameObject
				_ = r.ReadBool(); // m_Enabled
				r.Align( 4 );
				_ = r.ReadPPtr(); // m_Avatar
				ctrlPtr = r.ReadPPtr(); // m_Controller
			}

			if ( ctrlPtr.IsNull || !workspace.TryResolve( file, ctrlPtr, out var ctrlFile, out var ctrlObj ) )
				return results;

			if ( ctrlObj.ClassId == UnityClassId.AnimatorOverrideController )
			{
				using var r = workspace.OpenObjectReader( ctrlFile, ctrlObj );
				_ = r.ReadAlignedString(); // m_Name
				var baseCtrlPtr = r.ReadPPtr();
				int count = r.ReadInt32();
				for ( int i = 0; i < count; i++ )
				{
					_ = r.ReadPPtr(); // m_OriginalClip
					var ovr = r.ReadPPtr(); // m_OverrideClip
					if ( !ovr.IsNull && workspace.TryResolve( ctrlFile, ovr, out var cf, out var co ) && co.ClassId == UnityClassId.AnimationClip )
					{
						if ( seen.Add( new AssetKey( cf.Key, co.PathId ) ) )
							results.Add( (cf, co) );
					}
				}

				// Also grab any non-overridden clips from base controller if overrides were sparse
				if ( results.Count == 0 && !baseCtrlPtr.IsNull && workspace.TryResolve( ctrlFile, baseCtrlPtr, out var bcf, out var bco ) && bco.ClassId == UnityClassId.AnimatorController )
					ScanAnimatorControllerClips( workspace, bcf, bco, results, seen );
			}
			else if ( ctrlObj.ClassId == UnityClassId.AnimatorController )
			{
				ScanAnimatorControllerClips( workspace, ctrlFile, ctrlObj, results, seen );
			}
		}
		catch
		{
			// Ignore malformed controllers
		}

		return results;
	}

	static void ScanAnimatorControllerClips(
		UnityWorkspace workspace,
		SerializedFile ctrlFile,
		UnityObjectInfo ctrlObj,
		List<(SerializedFile, UnityObjectInfo)> results,
		HashSet<AssetKey> seen )
	{
		// Scan 4-byte aligned PPtr<AnimationClip> entries inside the AnimatorController blob
		byte[] blob;
		using ( var r = workspace.OpenObjectReader( ctrlFile, ctrlObj ) )
			blob = r.ReadBytes( (int)ctrlObj.ByteSize );

		var span = blob.AsSpan();
		for ( int off = 0; off + 12 <= span.Length; off += 4 )
		{
			int fid = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian( span[off..] );
			long pid = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian( span[(off + 4)..] );
			if ( fid < 0 || fid > ctrlFile.Externals.Count || pid <= 0 )
				continue;

			var ptr = new PPtr( fid, pid );
			if ( workspace.TryResolve( ctrlFile, ptr, out var cf, out var co ) && co.ClassId == UnityClassId.AnimationClip )
			{
				if ( seen.Add( new AssetKey( cf.Key, co.PathId ) ) )
					results.Add( (cf, co) );
			}
		}
	}
}
