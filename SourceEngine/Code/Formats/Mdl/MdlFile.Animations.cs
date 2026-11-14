using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Source1;

internal sealed partial class MdlFile
{
	/// <summary>
	/// One clip of a model. <see cref="ReadFrames"/> bakes its frames on demand, as parent-space transforms
	/// in the model's bone order, and throws when the animation data is invalid.
	/// </summary>
	internal sealed record ImportedAnimation( string Name, float FrameRate, bool Looping, int FrameCount, Func<Transform[][]> ReadFrames,
		ModelSequenceData Metadata, ModelPoseLayerData PoseLayer = null, bool Delta = false );

	/// <summary>
	/// <paramref name="IkBones"/> are bones to add to the skeleton behind the model's own, each a child of the model
	/// bone <c>Parent</c>; clip frames carry a transform for each of them after the model's bones.
	/// <paramref name="BoneMasks"/> holds the distinct sequence weight lists by bone, in that same order.
	/// </summary>
	internal sealed record ImportedAnimations( IReadOnlyList<ImportedAnimation> Clips, ModelPoseParameterData[] PoseParameters,
		IReadOnlyList<(string Name, float[] Weights)> BoneMasks, IReadOnlyList<(string Name, int Parent)> IkBones )
	{
		/// <summary>
		/// The sequence data to store on the model. Every read of it parses the stored form,
		/// so members with nothing in them are left out; they read back as empty.
		/// </summary>
		public ModelSequenceData[] StoredSequences()
		{
			var sequences = Clips.Where( clip => clip.Metadata is not null ).Select( clip => clip.Metadata ).ToArray();
			foreach ( var sequence in sequences )
			{
				if ( sequence.Activity is { Length: 0 } ) sequence.Activity = null;
				if ( sequence.Events is { Length: 0 } ) sequence.Events = null;
				if ( sequence.BlendAxes is { Length: 0 } ) sequence.BlendAxes = null;
				if ( sequence.BlendGrid is { Length: 0 } ) sequence.BlendGrid = null;
				if ( sequence.BlendGridMovement is { Length: 0 } ) sequence.BlendGridMovement = null;
				if ( sequence.BlendGridSpeeds is { Length: 0 } ) sequence.BlendGridSpeeds = null;
				if ( sequence.BlendPositions is { Length: 0 } ) sequence.BlendPositions = null;
				if ( sequence.BlendClips is { Length: 0 } ) sequence.BlendClips = null;
				if ( sequence.AdditiveLayers is { Length: 0 } ) sequence.AdditiveLayers = null;
				if ( sequence.IkTargets is { Length: 0 } ) sequence.IkTargets = null;
			}
			return sequences;
		}
	}

	// virtualmodel_t::AppendModels stops at 64 includes per model.
	private const int MaxAnimationSources = 65;
	// A graph addresses a bone mask by an 8-bit index. HL2 models have fewer than ten distinct weight lists.
	private const int MaxBoneMasks = 64;

	// mstudioseqdesc_t flags
	private const int SequenceLooping = 0x0001;
	private const int SequenceSnap = 0x0002;
	private const int SequenceDelta = 0x0004;
	private const int SequenceAutoplay = 0x0008;
	private const int SequencePost = 0x0010;
	private const int SequenceLocal = 0x0200;
	private const int SequenceOverride = 0x0800;
	private const int SequenceWorld = 0x4000;

	// mstudioautolayer_t flags
	private const int LayerSpline = 0x0040;
	private const int LayerCrossfade = 0x0080;
	private const int LayerNoBlend = 0x0200;
	private const int LayerLocal = 0x1000;
	private const int LayerPose = 0x4000;

	/// <summary>
	/// Lists the sequences of this model and of the models it includes as clips, posed as Source's
	/// bone setup poses them: blend grids, bone weights, ramped auto layers and autoplay layers.
	/// The clip named after a sequence has every pose parameter at zero; blend grid cells are separate clips.
	/// Only the sequence tables are read here; each clip decodes its frames when asked for them.
	/// Not executed: IK and root motion.
	/// <paramref name="shared"/> holds included animation models already read for another model of the same mount.
	/// </summary>
	internal ImportedAnimations ReadAnimations( Stream stream, string filename, Func<string, Stream> open,
		Dictionary<string, WeakReference<AnimationFile>> shared = null )
		=> new AnimationImporter( this, ReadFile( stream ), filename, open, shared ).Import();

	internal static bool IsDataError( Exception e ) => e is IOException or InvalidDataException or NotSupportedException
		or ArgumentException or IndexOutOfRangeException or ArithmeticException;

	private static string ReadRelativeString( byte[] data, long start, int offset )
		=> offset == 0 ? string.Empty : ReadString( data, start + offset );

	private StudioSeqDesc[] ReadSequences( byte[] data )
	{
		if ( _header.NumLocalSeq is < 0 or > 4096 )
			throw new InvalidDataException( "Invalid sequence count." );
		return LumpReader<StudioSeqDesc>.ReadLump( data, _header.LocalSeqIndex, _header.NumLocalSeq * Unsafe.SizeOf<StudioSeqDesc>() );
	}

	private (string Name, float Start, float End, float Loop)[] ReadPoseParameters( byte[] data )
	{
		if ( _header.NumLocalPoseParams is < 0 or > 1024 )
			throw new InvalidDataException( "Invalid pose parameter count." );
		var result = new (string, float, float, float)[_header.NumLocalPoseParams];
		for ( var i = 0; i < result.Length; i++ )
		{
			// mstudioposeparamdesc_t: name offset, flags, start, end, loop
			var offset = checked((int)(_header.LocalPoseParamOffset + i * 20L));
			var entry = data.AsSpan( offset, 20 );
			result[i] = (ReadRelativeString( data, offset, BinaryPrimitives.ReadInt32LittleEndian( entry ) ),
				BinaryPrimitives.ReadSingleLittleEndian( entry[8..] ),
				BinaryPrimitives.ReadSingleLittleEndian( entry[12..] ),
				BinaryPrimitives.ReadSingleLittleEndian( entry[16..] ));
		}
		return result;
	}

	private ModelSequenceData ReadSequenceMetadata( byte[] data, long offset, StudioSeqDesc sequence, string name )
	{
		const int eventSize = 80; // mstudioevent_t: cycle, id, type, options[64], name offset
		var events = new List<ModelSequenceData.Event>();
		var count = sequence.NumEvents is < 0 or > 4096 ? 0 : sequence.NumEvents;
		for ( var i = 0; i < count; i++ )
		{
			var eventOffset = checked((int)(offset + sequence.EventIndex + (long)i * eventSize));
			var entry = data.AsSpan( eventOffset, eventSize );
			var cycle = BinaryPrimitives.ReadSingleLittleEndian( entry );
			var options = entry.Slice( 12, 64 );
			var terminator = options.IndexOf( (byte)0 );
			events.Add( new()
			{
				// Source does not validate the cycle; an event outside the sequence fires at its nearest end.
				Cycle = float.IsFinite( cycle ) ? Math.Clamp( cycle, 0, 1 ) : 0,
				Id = BinaryPrimitives.ReadInt32LittleEndian( entry[4..] ),
				Type = BinaryPrimitives.ReadInt32LittleEndian( entry[8..] ),
				Options = System.Text.Encoding.Latin1.GetString( terminator < 0 ? options : options[..terminator] ),
				Name = ReadRelativeString( data, eventOffset, BinaryPrimitives.ReadInt32LittleEndian( entry[76..] ) )
			} );
		}
		return new()
		{
			Name = name,
			Activity = ReadRelativeString( data, offset, sequence.ActivityNameIndex ),
			ActivityWeight = sequence.ActWeight,
			Looping = (sequence.Flags & SequenceLooping) != 0,
			Snap = (sequence.Flags & SequenceSnap) != 0,
			Delta = (sequence.Flags & SequenceDelta) != 0,
			FadeIn = float.IsFinite( sequence.FadeInTime ) ? Math.Max( 0, sequence.FadeInTime ) : 0,
			FadeOut = float.IsFinite( sequence.FadeOutTime ) ? Math.Max( 0, sequence.FadeOutTime ) : 0,
			Events = events.OrderBy( e => e.Cycle ).ToArray()
		};
	}

	// The ground motion studiomdl extracted from one animation, over its whole length:
	// Studio_AnimMovement from cycle 0 to 1 over the mstudiomovement_t pieces.
	private Vector3 ReadAnimationMovement( byte[] data, int index, StudioAnimDesc animation )
	{
		if ( animation.NumMovements is < 1 or > 1024 )
			return default;
		const int size = 44; // endframe, motion flags, v0, v1, angle, vector, position
		var first = _header.LocalAnimIndex + (long)index * Unsafe.SizeOf<StudioAnimDesc>() + animation.MovementIndex;
		float lastFrame = animation.NumFrames - 1;
		var previousFrame = 0f;
		var position = Vector3.Zero;
		for ( var i = 0; i < animation.NumMovements; i++ )
		{
			var piece = data.AsSpan( checked((int)(first + (long)i * size)), size );
			float endFrame = BinaryPrimitives.ReadInt32LittleEndian( piece );
			if ( endFrame >= lastFrame )
			{
				// The piece accelerates from v0 to v1 along its direction.
				var fraction = (lastFrame - previousFrame) / (endFrame - previousFrame);
				var v0 = BinaryPrimitives.ReadSingleLittleEndian( piece[8..] );
				var v1 = BinaryPrimitives.ReadSingleLittleEndian( piece[12..] );
				var distance = v0 * fraction + 0.5f * (v1 - v0) * fraction * fraction;
				if ( float.IsFinite( distance ) )
					position += Vector96( piece[20..] ) * distance;
				break;
			}
			previousFrame = endFrame;
			position = Vector96( piece[32..] );
		}
		return float.IsFinite( position.x ) && float.IsFinite( position.y ) && float.IsFinite( position.z ) ? position : default;
	}

	// mathlib's QuaternionScale: scales the rotation angle and keeps the axis.
	internal static Rotation QuaternionScale( Rotation p, float t )
	{
		var sine = MathF.Min( MathF.Sqrt( p.x * p.x + p.y * p.y + p.z * p.z ), 1 );
		var scaled = MathF.Sin( MathF.Asin( sine ) * t );
		var factor = scaled / (sine + 1.1920929e-7f);
		var w = MathF.Sqrt( MathF.Max( 0, 1 - scaled * scaled ) );
		return new Rotation( p.x * factor, p.y * factor, p.z * factor, p.w < 0 ? -w : w );
	}

	// mathlib's QuaternionBlend: normalized linear blend along the shorter arc.
	internal static Rotation QuaternionBlend( Rotation p, Rotation q, float t )
	{
		var sign = p.x * q.x + p.y * q.y + p.z * q.z + p.w * q.w < 0 ? -t : t;
		return new Rotation( p.x * (1 - t) + q.x * sign, p.y * (1 - t) + q.y * sign,
			p.z * (1 - t) + q.z * sign, p.w * (1 - t) + q.w * sign ).Normal;
	}

	/// <summary>
	/// One MDL of an include graph, in memory with its external animation blocks.
	/// It does not depend on the model that includes it, so every model sharing the include can share this.
	/// </summary>
	internal sealed class AnimationFile
	{
		public readonly MdlFile Mdl;
		public readonly byte[] Data;
		public readonly string Path;
		public readonly StudioSeqDesc[] Sequences;
		public readonly (string Name, float Start, float End, float Loop)[] PoseParameters;
		public readonly IkChain[] IkChains = [];
		// The external .ani file, or null when the MDL has none or it is missing.
		private readonly byte[] blocks;

		public AnimationFile( MdlFile mdl, byte[] data, string path, Func<string, Stream> open )
		{
			Mdl = mdl;
			Data = data;
			Path = path;
			Sequences = mdl.ReadSequences( data );
			PoseParameters = mdl.ReadPoseParameters( data );
			try
			{
				IkChains = mdl.ReadIkChains( data );
			}
			catch ( Exception e ) when ( IsDataError( e ) )
			{
				Log.Trace( $"{path}: IK chains were not read: {e.Message}" );
			}
			if ( mdl._header.NumAnimBlocks <= 0 || mdl._header.AnimBlockNameOffset == 0 )
				return;
			try
			{
				// Read now: the opener is only valid while the model is being read.
				blocks = mdl.ReadAnimationBlockFile( data, file =>
					open( file.StartsWith( "models/", StringComparison.OrdinalIgnoreCase ) ? file : path[..(path.LastIndexOf( '/' ) + 1)] + file ) );
			}
			catch ( Exception e ) when ( IsDataError( e ) )
			{
				Log.Trace( $"{path}: external animation blocks were not read: {e.Message}" );
			}
		}

		public byte[] Blocks() => blocks ?? throw new FileNotFoundException( "The external animation block file is missing." );
	}

	/// <summary>An <see cref="AnimationFile"/> as seen from the model that includes it.</summary>
	private sealed class AnimationSource
	{
		public readonly AnimationFile File;
		// The master model's bone to this MDL's bone, by name; -1 when this MDL lacks it.
		public readonly int[] BoneMap;
		// This MDL's bone to the master model's bone, or -1.
		public readonly int[] MasterBone;

		public AnimationSource( AnimationFile file, MdlFile master )
		{
			File = file;
			BoneMap = master._boneNames.Select( name => Array.FindIndex( file.Mdl._boneNames, x => x.Equals( name, StringComparison.OrdinalIgnoreCase ) ) ).ToArray();
			MasterBone = Enumerable.Repeat( -1, file.Mdl.BoneCount ).ToArray();
			for ( var bone = BoneMap.Length - 1; bone >= 0; bone-- )
			{
				if ( BoneMap[bone] >= 0 )
					MasterBone[BoneMap[bone]] = bone;
			}
		}

		public MdlFile Mdl => File.Mdl;
		public byte[] Data => File.Data;
		public string Path => File.Path;
		public StudioSeqDesc[] Sequences => File.Sequences;
		public (string Name, float Start, float End, float Loop)[] PoseParameters => File.PoseParameters;

		public long SequenceOffset( int index ) => Mdl._header.LocalSeqIndex + (long)index * Unsafe.SizeOf<StudioSeqDesc>();

		public ReadOnlySpan<byte> SequenceData( int index, long offset, int size ) => Data.AsSpan( checked((int)(SequenceOffset( index ) + offset)), size );

		public string SequenceName( int index ) => ReadRelativeString( Data, SequenceOffset( index ), Sequences[index].LabelIndex );

		public int Animation( int index, int x, int y )
		{
			var cell = x + y * Sequences[index].GroupSize;
			return BinaryPrimitives.ReadInt16LittleEndian( SequenceData( index, Sequences[index].AnimIndexIndex + cell * 2L, 2 ) );
		}

		public float Weight( int index, int bone )
		{
			var list = Sequences[index].WeightListIndex;
			if ( list == 0 )
				return 1;
			var weight = BinaryPrimitives.ReadSingleLittleEndian( SequenceData( index, list + bone * 4L, 4 ) );
			return float.IsFinite( weight ) ? Math.Clamp( weight, 0, 1 ) : 0;
		}
	}

	private sealed partial class AnimationImporter
	{
		private static readonly Dictionary<string, float> NoParameters = new( StringComparer.OrdinalIgnoreCase );

		private readonly MdlFile master;
		private readonly Transform[] bind;
		private readonly Dictionary<string, WeakReference<AnimationFile>> shared;
		private readonly List<AnimationSource> sources = [];
		private readonly HashSet<string> visited = new( StringComparer.OrdinalIgnoreCase );
		// The virtual model's sequence table: the first definition of a name wins.
		private readonly List<(AnimationSource Source, int Index, string Name)> sequences = [];
		private readonly Dictionary<string, int> sequenceSlots = new( StringComparer.OrdinalIgnoreCase );
		private readonly Dictionary<string, (float Start, float End, float Loop)> poseParameters = new( StringComparer.OrdinalIgnoreCase );
		// Animations decoded for the clip being baked.
		private readonly Dictionary<(AnimationSource, int), AnimationFrames> decoded = [];
		private readonly List<ImportedAnimation> result = [];
		private readonly HashSet<string> names = new( StringComparer.OrdinalIgnoreCase );
		// Distinct sequence weight lists by master bone, exported as the bone masks "weights/N".
		private readonly List<float[]> boneMasks = [];
		// IK target bones added behind the model's own: a chain and the master bone its rule is relative to.
		private readonly List<(int Chain, int Bone)> ikBones = [];
		private List<(AnimationSource Source, int Index, string Name)> autoplay;

		public AnimationImporter( MdlFile master, byte[] data, string path, Func<string, Stream> open, Dictionary<string, WeakReference<AnimationFile>> shared )
		{
			this.master = master;
			this.shared = shared;
			bind = master.Bones.Select( bone => new Transform( bone.Position, bone.Quaternion.Normal ) ).ToArray();
			visited.Add( path );
			Load( new AnimationFile( master, data, path, open ), open );
		}

		private void Load( AnimationFile file, Func<string, Stream> open )
		{
			var source = new AnimationSource( file, master );
			sources.Add( source );
			foreach ( var parameter in source.PoseParameters )
			{
				// A parameter shared between models spans the range of every definition.
				if ( poseParameters.TryGetValue( parameter.Name, out var existing ) )
				{
					var start = MathF.Min( MathF.Min( existing.Start, existing.End ), MathF.Min( parameter.Start, parameter.End ) );
					var end = MathF.Max( MathF.Max( existing.Start, existing.End ), MathF.Max( parameter.Start, parameter.End ) );
					poseParameters[parameter.Name] = (start, end, existing.Loop);
				}
				else
				{
					poseParameters.Add( parameter.Name, (parameter.Start, parameter.End, parameter.Loop) );
				}
			}
			for ( var i = 0; i < source.Sequences.Length; i++ )
			{
				var name = source.SequenceName( i );
				if ( string.IsNullOrWhiteSpace( name ) )
					continue;
				if ( !sequenceSlots.TryGetValue( name, out var slot ) )
				{
					sequenceSlots.Add( name, sequences.Count );
					sequences.Add( (source, i, name) );
				}
				else if ( (sequences[slot].Source.Sequences[sequences[slot].Index].Flags & SequenceOverride) != 0 )
				{
					// A forward declaration is replaced by the later definition.
					sequences[slot] = (source, i, name);
				}
			}

			var header = file.Mdl._header;
			for ( var i = 0; i < header.NumIncludeModels; i++ )
			{
				if ( sources.Count >= MaxAnimationSources )
				{
					Log.Warning( $"{file.Path}: further included animation models were ignored; the limit is {MaxAnimationSources - 1}." );
					break;
				}
				var include = "";
				try
				{
					// mstudiomodelgroup_t: label offset, name offset
					var entry = checked((int)(header.IncludeModelOffset + i * 8L));
					include = ReadRelativeString( file.Data, entry, BinaryPrimitives.ReadInt32LittleEndian( file.Data.AsSpan( entry + 4, 4 ) ) ).Replace( '\\', '/' );
					if ( !include.StartsWith( "models/", StringComparison.OrdinalIgnoreCase ) || include.Contains( ':' ) || include.Split( '/' ).Contains( ".." ) )
						throw new InvalidDataException( "Invalid included animation model path." );
					if ( !visited.Add( include ) )
						continue;
					Load( OpenInclude( include, open ), open );
				}
				catch ( Exception e ) when ( IsDataError( e ) )
				{
					// Source skips an include it cannot load and keeps the rest of the model.
					Log.Trace( $"{file.Path}: included animation model '{include}' was not imported: {e.Message}" );
				}
			}
		}

		private AnimationFile OpenInclude( string path, Func<string, Stream> open )
		{
			if ( shared is not null )
			{
				lock ( shared )
				{
					if ( shared.TryGetValue( path, out var reference ) && reference.TryGetTarget( out var existing ) )
						return existing;
				}
			}
			using var stream = open( path );
			var mdl = new MdlFile( stream );
			var file = new AnimationFile( mdl, mdl.ReadFile( stream ), path, open );
			if ( shared is not null )
			{
				lock ( shared )
				{
					shared[path] = new WeakReference<AnimationFile>( file );
				}
			}
			return file;
		}

		public ImportedAnimations Import()
		{
			chains = sources[0].File.IkChains;
			autoplay = sequences.Where( x => (x.Source.Sequences[x.Index].Flags & SequenceAutoplay) != 0 ).ToList();
			if ( autoplay.Count > 64 )
			{
				Log.Warning( $"{sources[0].Path}: {autoplay.Count - 64} automatic animation layers were ignored." );
				autoplay.RemoveRange( 64, autoplay.Count - 64 );
			}

			// Listing costs no decoding, so every sequence is offered. Activity-tagged gameplay is listed first.
			foreach ( var gameplay in new[] { true, false } )
			{
				foreach ( var entry in sequences )
				{
					var sequence = entry.Source.Sequences[entry.Index];
					// Autoplay layers are baked into every other clip.
					if ( (sequence.Flags & SequenceAutoplay) != 0 )
						continue;
					var activity = ReadRelativeString( entry.Source.Data, entry.Source.SequenceOffset( entry.Index ), sequence.ActivityNameIndex );
					if ( (!string.IsNullOrWhiteSpace( activity ) && !activity.Equals( "ACT_INVALID", StringComparison.OrdinalIgnoreCase )) == gameplay )
						ExportSequence( entry.Source, entry.Index, entry.Name );
				}
				if ( !gameplay )
					continue;
				foreach ( var layer in autoplay )
				{
					try
					{
						ExportPoseLayer( layer.Source, layer.Index, layer.Name );
					}
					catch ( Exception e ) when ( IsDataError( e ) )
					{
						Log.Trace( $"{layer.Source.Path}: pose layer '{layer.Name}' was not imported: {e.Message}" );
					}
				}
			}

			var parameters = poseParameters.Select( x => new ModelPoseParameterData
			{
				Name = x.Key,
				Min = MathF.Min( x.Value.Start, x.Value.End ),
				Max = MathF.Max( x.Value.Start, x.Value.End ),
				Loop = x.Value.Loop
			} ).ToArray();
			// A masked clip drives the IK targets it carries, so every mask weights them fully.
			var masks = boneMasks.Select( ( weights, i ) => ($"weights/{i}", (float[])[.. weights, .. Enumerable.Repeat( 1f, ikBones.Count )]) ).ToArray();
			return new( result, parameters, masks, ikBones.Select( x => (IkBoneName( x ), x.Bone) ).ToArray() );
		}

		private void ExportSequence( AnimationSource source, int index, string name )
		{
			var sequence = source.Sequences[index];
			var delta = (sequence.Flags & SequenceDelta) != 0;
			var looping = (sequence.Flags & SequenceLooping) != 0;
			ModelSequenceData metadata;
			bool masked;
			try
			{
				// A delta sequence without the post flag applies before the pose it is layered onto,
				// which an additive clip cannot express. Such sequences are still baked where they are used as layers.
				if ( delta && (sequence.Flags & SequencePost) == 0 )
					throw new NotSupportedException( "Pre-multiplied delta sequence." );
				var (frameCount, rate) = Timing( source, index, NeutralCell( source, index ) );
				metadata = source.Mdl.ReadSequenceMetadata( source.Data, source.SequenceOffset( index ), sequence, name );
				(metadata.Movement, metadata.GroundSpeed) = GroundMotion( source, index, null );
				var mask = BoneMask( source, index );
				masked = mask >= 0;
				metadata.BoneMask = masked ? $"weights/{mask}" : null;
				var deltaLayers = DeltaLayers( source, index );
				metadata.AdditiveLayers = deltaLayers.Where( x => !masked || IsBlendGrid( x.Source, x.Index ) ).Select( x => x.Source.SequenceName( x.Index ) )
					.Where( x => !string.IsNullOrWhiteSpace( x ) ).Distinct().ToArray();
				if ( masked && deltaLayers.Any( x => !IsBlendGrid( x.Source, x.Index ) && ReachesPast( x.Source, x.Index, boneMasks[mask] ) ) )
				{
					// The clip of a masked sequence has its delta layers baked in on the bones it weights. What they
					// do to the other bones would be lost behind the mask, so it is offered as an additive clip of its own.
					var remainder = $"__additive/{name}";
					var weights = boneMasks[mask];
					result.Add( new( remainder, rate, looping, frameCount, () => BakeRemainder( source, index, frameCount, weights ),
						new ModelSequenceData { Name = remainder, Looping = looping, Delta = true }, Delta: true ) );
					names.Add( remainder );
					metadata.AdditiveLayers = [.. metadata.AdditiveLayers, remainder];
				}
				if ( !delta )
					metadata.IkTargets = IkTargets( source, index );
				names.Add( name );
				result.Add( new( name, rate, looping, frameCount, () => Bake( source, index, frameCount, null, NoParameters, masked ), metadata, Delta: delta ) );
			}
			catch ( Exception e ) when ( IsDataError( e ) )
			{
				Log.Trace( $"{source.Path}: sequence '{name}' was not imported: {e.Message}" );
				return;
			}

			try
			{
				ExportGrid( source, index, name, metadata, masked );
			}
			catch ( Exception e ) when ( IsDataError( e ) )
			{
				Log.Trace( $"{source.Path}: blend grid of sequence '{name}' was not imported: {e.Message}" );
			}
		}

		// Every cell of a blended sequence as its own clip, so a consumer can blend them by pose parameter.
		private void ExportGrid( AnimationSource source, int index, string name, ModelSequenceData metadata, bool masked )
		{
			var sequence = source.Sequences[index];
			var width = sequence.GroupSize;
			var height = sequence.GroupSize2;
			if ( sequence.NumBlends < 2 || width < 2 )
				return;
			var axes = new List<ModelSequenceData.BlendAxis> { Axis( source, index, 0 ) };
			if ( height > 1 )
				axes.Add( Axis( source, index, 1 ) );
			if ( axes.Contains( null ) )
				return;

			var single = true;
			for ( var cell = 0; cell < width * height; cell++ )
			{
				single &= Timing( source, index, (cell % width, cell / width) ).FrameCount == 1;
			}

			// Cells that share an animation are the same clip unless a layer could tell them apart.
			var sameAnimation = sequence.NumAutoLayers == 0 && autoplay.Count == 0 ? new Dictionary<int, string>() : null;
			var delta = (sequence.Flags & SequenceDelta) != 0;
			var clips = new string[width * height];
			var movement = new Vector3[clips.Length];
			var speeds = new float[clips.Length];
			var added = new List<ImportedAnimation>();
			for ( var cell = 0; cell < clips.Length; cell++ )
			{
				var x = cell % width;
				var y = cell / width;
				(movement[cell], speeds[cell]) = GroundMotion( source, index, (x, y) );
				var animation = source.Animation( index, x, y );
				if ( sameAnimation is not null && sameAnimation.TryGetValue( animation, out var existing ) )
				{
					clips[cell] = existing;
					continue;
				}
				var clip = $"__blend/{name}/{cell}";
				if ( names.Contains( clip ) )
					throw new InvalidDataException( "Blend clip name is already in use." );
				var parameters = new Dictionary<string, float>( StringComparer.OrdinalIgnoreCase ) { [axes[0].Parameter] = axes[0].Positions[x] };
				if ( height > 1 )
					parameters[axes[1].Parameter] = axes[1].Positions[y];
				var (frameCount, rate) = Timing( source, index, (x, y) );
				added.Add( new( clip, rate, (sequence.Flags & SequenceLooping) != 0, frameCount,
					() => Bake( source, index, frameCount, (x, y), parameters, masked ), null, Delta: delta ) );
				clips[cell] = clip;
				sameAnimation?.Add( animation, clip );
			}

			foreach ( var clip in added )
			{
				names.Add( clip.Name );
				result.Add( clip );
			}
			metadata.BlendAxes = axes.ToArray();
			metadata.BlendGrid = clips;
			metadata.BlendGridMovement = movement;
			metadata.BlendGridSpeeds = speeds;
			if ( !delta && height == 1 && single )
			{
				metadata.BlendParameter = axes[0].Parameter;
				metadata.BlendPositions = axes[0].Positions;
				metadata.BlendClips = clips;
			}
		}

		// A static autoplay pose grid maps to additive layers a consumer drives by parameter.
		// Its neutral pose is already baked into every clip, so the layer clips are relative to it.
		private void ExportPoseLayer( AnimationSource source, int index, string label )
		{
			var sequence = source.Sequences[index];
			var delta = (sequence.Flags & SequenceDelta) != 0;
			// A delta grid has to be post-multiplied to be an additive clip; a grid of absolute poses always can be.
			if ( (sequence.Flags & SequenceAutoplay) == 0 || (delta && (sequence.Flags & SequencePost) == 0)
				|| sequence.GroupSize < 2 || sequence.GroupSize2 != 1 || sequence.NumAutoLayers != 0 )
				return;
			var axis = Axis( source, index, 0 );
			if ( axis is null )
				return;
			for ( var i = 0; i < sequence.GroupSize; i++ )
			{
				var animation = source.Mdl.ReadAnimationDescriptor( source.Data, source.Animation( index, i, 0 ) );
				if ( animation.NumFrames != 1 || ((animation.Flags & AnimationDelta) != 0) != delta )
					return;
			}
			var clips = Enumerable.Range( 0, sequence.GroupSize ).Select( i => $"__pose/{label}/{i}" ).ToArray();
			if ( clips.Any( names.Contains ) )
				return;
			var layer = new ModelPoseLayerData { Name = label, Parameter = axis.Parameter, Positions = axis.Positions, Clips = clips };
			for ( var i = 0; i < clips.Length; i++ )
			{
				var cell = i;
				names.Add( clips[i] );
				result.Add( new( clips[i], 30, false, 1, () => [WithIkBones( PoseLayerFrame( source, index, cell ), null )], null, layer, Delta: true ) );
			}
		}

		private Transform[] PoseLayerFrame( AnimationSource source, int index, int cell )
		{
			decoded.Clear();
			var delta = (source.Sequences[index].Flags & SequenceDelta) != 0;
			var neutral = CalcPose( source, index, 0, null, NoParameters );
			var sample = CalcPose( source, index, 0, (cell, 0), NoParameters );
			var frame = new Transform[bind.Length];
			for ( var bone = 0; bone < frame.Length; bone++ )
			{
				var mapped = source.BoneMap[bone];
				var weight = mapped < 0 ? 0 : source.Weight( index, mapped );
				// Absolute poses replace the bones they weight fully, so the step from the baked neutral pose
				// to the selected one is exact there and scaled by the weight elsewhere.
				var rotation = delta
					? QuaternionScale( neutral[bone].Rotation, weight ).Inverse * QuaternionScale( sample[bone].Rotation, weight )
					: QuaternionScale( (neutral[bone].Rotation.Inverse * sample[bone].Rotation).Normal, weight );
				frame[bone] = new Transform( (sample[bone].Position - neutral[bone].Position) * weight, rotation.Normal );
			}
			return frame;
		}

		// The weight a sequence poses each bone with: its own weight list, and those of the non-delta
		// sequences it layers on. Gestures often weight nothing themselves and animate only through
		// their layers. Delta layers add to whatever pose is below them and need no mask.
		private float[] AbsoluteWeights( AnimationSource source, int index, int depth )
		{
			var weights = new float[bind.Length];
			if ( depth > 8 || (uint)index >= source.Sequences.Length )
				return weights;
			var sequence = source.Sequences[index];
			if ( (sequence.Flags & SequenceDelta) != 0 )
				return weights;
			for ( var bone = 0; bone < weights.Length; bone++ )
			{
				var mapped = source.BoneMap[bone];
				if ( mapped >= 0 )
					weights[bone] = source.Weight( index, mapped );
			}
			if ( sequence.NumAutoLayers is < 0 or > 64 )
				return weights;
			for ( var i = 0; i < sequence.NumAutoLayers; i++ )
			{
				var layer = source.SequenceData( index, sequence.AutoLayerIndex + i * 24L, 24 );
				// Local layers pose the sequence before its own weights apply, so those still bound them.
				if ( (BinaryPrimitives.ReadInt32LittleEndian( layer[4..] ) & LayerLocal) != 0 )
					continue;
				if ( Defined( source, BinaryPrimitives.ReadInt16LittleEndian( layer ) ) is not { } child )
					continue;
				var layered = AbsoluteWeights( child.Source, child.Index, depth + 1 );
				for ( var bone = 0; bone < weights.Length; bone++ )
				{
					weights[bone] = MathF.Max( weights[bone], layered[bone] );
				}
			}
			return weights;
		}

		// The delta sequences a sequence layers on itself, directly or through its non-delta layers.
		private List<(AnimationSource Source, int Index)> DeltaLayers( AnimationSource source, int index, int depth = 0, List<(AnimationSource Source, int Index)> layers = null )
		{
			layers ??= [];
			var sequence = source.Sequences[index];
			for ( var i = 0; i < sequence.NumAutoLayers && i < 64 && depth <= 8; i++ )
			{
				int layer = BinaryPrimitives.ReadInt16LittleEndian( source.SequenceData( index, sequence.AutoLayerIndex + i * 24L, 24 ) );
				if ( (uint)layer >= source.Sequences.Length || Defined( source, layer ) is not { } child )
					continue;
				if ( (child.Source.Sequences[child.Index].Flags & SequenceDelta) == 0 )
					DeltaLayers( child.Source, child.Index, depth + 1, layers );
				else if ( !layers.Contains( child ) )
					layers.Add( child );
			}
			return layers;
		}

		// bone_setup's iRelativeSeq: a layer names a sequence of its own model, where a $declaresequence placeholder stands for the
		// sequence of that name the including model defines. A placeholder nothing defines (monk's layer_Aim_all) has no animation.
		private (AnimationSource Source, int Index)? Defined( AnimationSource source, int index )
		{
			if ( (uint)index >= source.Sequences.Length || (source.Sequences[index].Flags & SequenceOverride) == 0 )
				return (source, index);
			if ( !sequenceSlots.TryGetValue( source.SequenceName( index ), out var slot ) )
				return null;
			var defined = sequences[slot];
			return (defined.Source.Sequences[defined.Index].Flags & SequenceOverride) != 0 ? null : (defined.Source, defined.Index);
		}

		// A pose parameter grid is left for the consumer to drive rather than folded into another clip.
		private static bool IsBlendGrid( AnimationSource source, int index ) => source.Sequences[index].NumBlends > 1;

		// Whether a delta sequence moves a bone the mask does not weight fully.
		private bool ReachesPast( AnimationSource source, int index, float[] mask )
		{
			for ( var bone = 0; bone < bind.Length; bone++ )
			{
				var mapped = source.BoneMap[bone];
				if ( mapped >= 0 && mask[bone] < 1 && source.Weight( index, mapped ) > 0 )
					return true;
			}
			return false;
		}

		// What a masked sequence's delta layers add to each bone, less the share its masked clip already carries.
		private Transform[][] BakeRemainder( AnimationSource source, int index, int frameCount, float[] mask )
		{
			decoded.Clear();
			var frames = new Transform[frameCount][];
			for ( var frame = 0; frame < frameCount; frame++ )
			{
				var cycle = frameCount == 1 ? 0 : (float)frame / (frameCount - 1);
				var pose = Enumerable.Repeat( Transform.Zero, bind.Length ).ToArray();
				Accumulate( pose, source, index, cycle, 1, 0, null, NoParameters, deltasOnly: true );
				for ( var bone = 0; bone < pose.Length; bone++ )
				{
					var rest = 1 - mask[bone];
					pose[bone] = new Transform( pose[bone].Position * rest, QuaternionScale( pose[bone].Rotation, rest ).Normal );
				}
				frames[frame] = WithIkBones( pose, null );
			}
			decoded.Clear();
			return frames;
		}

		// The bone mask for a sequence that poses only part of the skeleton, or -1 when it needs none:
		// a delta sequence is baked already scaled by its weights, and full weights mask nothing.
		private int BoneMask( AnimationSource source, int index )
		{
			if ( (source.Sequences[index].Flags & SequenceDelta) != 0 )
				return -1;
			var weights = AbsoluteWeights( source, index, 0 );
			var partial = false;
			for ( var bone = 0; bone < weights.Length; bone++ )
			{
				partial |= source.BoneMap[bone] >= 0 && weights[bone] < 1;
			}
			if ( !partial )
				return -1;
			var existing = boneMasks.FindIndex( x => x.AsSpan().SequenceEqual( weights ) );
			if ( existing >= 0 || boneMasks.Count >= MaxBoneMasks )
				return existing;
			boneMasks.Add( weights );
			return boneMasks.Count - 1;
		}

		private ModelSequenceData.BlendAxis Axis( AnimationSource source, int index, int axis )
		{
			var sequence = source.Sequences[index];
			var count = axis == 0 ? sequence.GroupSize : sequence.GroupSize2;
			var parameter = axis == 0 ? sequence.ParamIndex : sequence.ParamIndex2;
			if ( count is < 2 or > 64 || (uint)parameter >= source.PoseParameters.Length )
				return null;
			var name = source.PoseParameters[parameter].Name;
			if ( string.IsNullOrWhiteSpace( name ) )
				return null;
			var start = axis == 0 ? sequence.ParamStart : sequence.ParamStart2;
			var end = axis == 0 ? sequence.ParamEnd : sequence.ParamEnd2;
			var positions = new float[count];
			for ( var i = 0; i < count; i++ )
			{
				positions[i] = sequence.PoseKeyIindex != 0 ? PoseKey( source, index, axis, i ) : start + (end - start) * i / (count - 1);
				if ( !float.IsFinite( positions[i] ) )
					throw new InvalidDataException( "Invalid sequence pose key." );
			}
			var range = poseParameters[name];
			return new() { Parameter = name, Min = MathF.Min( range.Start, range.End ), Max = MathF.Max( range.Start, range.End ), Loop = range.Loop, Positions = positions };
		}

		private static float PoseKey( AnimationSource source, int index, int axis, int key )
		{
			var sequence = source.Sequences[index];
			return BinaryPrimitives.ReadSingleLittleEndian( source.SequenceData( index, sequence.PoseKeyIindex + (axis * sequence.GroupSize + key) * 4L, 4 ) );
		}

		// The grid cell nearest the pose with every parameter at zero; it gives the clip its length and rate.
		private (int X, int Y) NeutralCell( AnimationSource source, int index )
		{
			var x = LocalPoseParameter( source, index, 0, NoParameters );
			var y = LocalPoseParameter( source, index, 1, NoParameters );
			return (x.Weight > 0.5f ? x.Index + 1 : x.Index, y.Weight > 0.5f ? y.Index + 1 : y.Index);
		}

		private (int FrameCount, float Rate) Timing( AnimationSource source, int index, (int X, int Y) cell )
		{
			ValidateGrid( source.Sequences[index] );
			var animation = source.Mdl.ReadAnimationDescriptor( source.Data, source.Animation( index, cell.X, cell.Y ) );
			if ( animation.NumFrames is < 1 or > MaxAnimationFrames || !float.IsFinite( animation.Fps ) || animation.Fps <= 0 )
				throw new InvalidDataException( "Invalid animation frame count or rate." );
			return (animation.NumFrames, animation.Fps);
		}

		// The ground motion of one cycle and the speed it is authored for, as Studio_SeqMovement and
		// Studio_Duration compute them: weighted over the grid cells blended at the pose parameters.
		private (Vector3 Movement, float Speed) GroundMotion( AnimationSource source, int index, (int X, int Y)? cell )
		{
			var movement = Vector3.Zero;
			var cyclesPerSecond = 0f;
			foreach ( var (animationIndex, weight) in BlendedAnimations( source, index, cell, NoParameters ) )
			{
				var animation = source.Mdl.ReadAnimationDescriptor( source.Data, animationIndex );
				movement += source.Mdl.ReadAnimationMovement( source.Data, animationIndex, animation ) * weight;
				if ( animation.NumFrames > 1 && float.IsFinite( animation.Fps ) )
					cyclesPerSecond += animation.Fps / (animation.NumFrames - 1) * weight;
			}
			var duration = cyclesPerSecond > 0 ? 1 / cyclesPerSecond : 0.1f;
			return (movement, movement.Length / duration);
		}

		private static void ValidateGrid( StudioSeqDesc sequence )
		{
			if ( sequence.GroupSize < 1 || sequence.GroupSize2 < 1 || (long)sequence.GroupSize * sequence.GroupSize2 != sequence.NumBlends )
				throw new InvalidDataException( "Invalid sequence blend grid." );
		}

		// A masked sequence is meant to be layered through its bone mask, which applies the weights; baking
		// them in as well would apply them twice, and a layer ramping in would drag the bind pose along.
		// So the first non-delta sequence to weight a bone poses it fully, whatever its weight at that moment;
		// later ones blend over it by their weights as in Source.
		private Transform[][] Bake( AnimationSource source, int index, int frameCount, (int X, int Y)? cell, Dictionary<string, float> parameters, bool masked = false )
		{
			decoded.Clear();
			var delta = (source.Sequences[index].Flags & SequenceDelta) != 0;
			var frames = new Transform[frameCount][];
			for ( var frame = 0; frame < frameCount; frame++ )
			{
				var cycle = frameCount == 1 ? 0 : (float)frame / (frameCount - 1);
				var pose = delta ? Enumerable.Repeat( Transform.Zero, bind.Length ).ToArray() : bind.ToArray();
				var ik = delta ? null : NewIkContext();
				Accumulate( pose, source, index, cycle, 1, 0, cell, parameters, masked ? new bool[pose.Length] : null, ik );
				if ( !delta )
				{
					// Source runs autoplay sequences on their own clock; here they follow the clip's cycle.
					foreach ( var layer in autoplay )
						Accumulate( pose, layer.Source, layer.Index, cycle, 1, 0, null, parameters );
				}
				// A masked sequence leaves the rest of the body to whatever it is layered over, so where its
				// chains end up is only known at runtime; a whole-body sequence is solved here as Source solves it.
				if ( !masked )
					SolveIk( pose, ik );
				for ( var bone = 0; bone < pose.Length; bone++ )
				{
					var p = pose[bone].Position;
					var q = pose[bone].Rotation;
					var length = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
					if ( !float.IsFinite( p.x ) || !float.IsFinite( p.y ) || !float.IsFinite( p.z ) || !float.IsFinite( length ) || length < 1e-12f )
						throw new InvalidDataException( "Non-finite or degenerate animation pose." );
					pose[bone].Rotation = q.Normal;
				}
				frames[frame] = WithIkBones( pose, ik );
			}
			decoded.Clear();
			return frames;
		}

		// bone_setup's AccumulatePose: blends one sequence, then its auto layers, onto the pose, and gathers
		// its IK rules. Without a pose it only gathers the rules.
		private void Accumulate( Transform[] pose, AnimationSource source, int index, float cycle, float weight, int depth, (int X, int Y)? cell,
			Dictionary<string, float> parameters, bool[] covered = null, List<IkRule>[] ik = null, bool deltasOnly = false )
		{
			if ( depth > 8 || (uint)index >= source.Sequences.Length )
				throw new InvalidDataException( "Invalid or cyclic animation layer." );
			weight = float.IsFinite( weight ) ? Math.Clamp( weight, 0, 1 ) : 0;
			var flags = source.Sequences[index].Flags;
			var local = (flags & SequenceLocal) != 0;
			// A lock holds on to the pose below a layer; the base sequence has nothing below it.
			var locks = pose is not null && depth > 0 && !deltasOnly ? RecordIkLocks( source, index, pose ) : null;
			if ( pose is null )
			{
				if ( local )
					AddLayers( null, source, index, cycle, 1, depth, true, parameters, null, ik );
			}
			else if ( !deltasOnly || (flags & SequenceDelta) != 0 )
			{
				// With deltasOnly the pose is the sum of the delta sequences alone; absolute poses are passed over.
				var sequencePose = CalcPose( source, index, cycle, cell, parameters );
				if ( local )
					AddLayers( sequencePose, source, index, cycle, 1, depth, true, parameters, null, ik, deltasOnly );
				SlerpBones( pose, source, index, sequencePose, weight, covered );
			}
			AddIkRules( ik, source, index, cycle, weight, cell, parameters );
			AddLayers( pose, source, index, cycle, weight, depth, false, parameters, covered, ik, deltasOnly );
			if ( locks is not null )
				SolveIkLocks( pose, locks );
		}

		// bone_setup's AddSequenceLayers and AddLocalLayers.
		private void AddLayers( Transform[] pose, AnimationSource source, int index, float cycle, float weight, int depth, bool local, Dictionary<string, float> parameters, bool[] covered = null, List<IkRule>[] ik = null, bool deltasOnly = false )
		{
			var sequence = source.Sequences[index];
			if ( sequence.NumAutoLayers is < 0 or > 64 )
				throw new InvalidDataException( "Invalid animation layer count." );
			for ( var i = 0; i < sequence.NumAutoLayers; i++ )
			{
				// mstudioautolayer_t: sequence, pose parameter, flags, start, peak, tail, end
				var layer = source.SequenceData( index, sequence.AutoLayerIndex + i * 24L, 24 );
				var flags = BinaryPrimitives.ReadInt32LittleEndian( layer[4..] );
				if ( ((flags & LayerLocal) != 0) != local )
					continue;
				var start = BinaryPrimitives.ReadSingleLittleEndian( layer[8..] );
				var peak = BinaryPrimitives.ReadSingleLittleEndian( layer[12..] );
				var tail = BinaryPrimitives.ReadSingleLittleEndian( layer[16..] );
				var end = BinaryPrimitives.ReadSingleLittleEndian( layer[20..] );
				var layerCycle = cycle;
				var layerWeight = weight;
				if ( start != end && float.IsFinite( start ) && float.IsFinite( peak ) && float.IsFinite( tail ) && float.IsFinite( end ) )
				{
					// The layer ramps in and out over the cycle, or over a pose parameter.
					var driven = (flags & LayerPose) != 0;
					var position = driven ? ParameterValue( source, BinaryPrimitives.ReadInt16LittleEndian( layer[2..] ), parameters ) : cycle;
					if ( position < start || position >= end )
						continue;
					var ramp = 1f;
					if ( position < peak && start != peak )
						ramp = (position - start) / (peak - start);
					else if ( position > tail && end != tail )
						ramp = (end - position) / (end - tail);
					if ( (flags & LayerSpline) != 0 )
						ramp = ramp * ramp * (3 - 2 * ramp);
					if ( (flags & LayerCrossfade) != 0 && position > tail )
						layerWeight = ramp * weight / (1 - weight + ramp * weight);
					else if ( (flags & LayerNoBlend) != 0 )
						layerWeight = ramp;
					else
						layerWeight = weight * ramp;
					if ( !driven )
						layerCycle = (cycle - start) / (end - start);
				}
				int child = BinaryPrimitives.ReadInt16LittleEndian( layer );
				if ( Defined( source, child ) is not { } resolved )
					continue;
				(var layerSource, child) = resolved;
				// A delta grid is not part of the remainder; the consumer drives it by its parameters.
				if ( deltasOnly && (uint)child < layerSource.Sequences.Length && (layerSource.Sequences[child].Flags & SequenceDelta) != 0 && IsBlendGrid( layerSource, child ) )
					continue;
				Accumulate( pose, layerSource, child, layerCycle, layerWeight, depth + 1, null, parameters, covered, ik, deltasOnly );
			}
		}

		// bone_setup's SlerpBones, by the sequence's bone weights.
		// <paramref name="covered"/> is set while baking a masked sequence: the bones a non-delta sequence has posed so far.
		private void SlerpBones( Transform[] pose, AnimationSource source, int index, Transform[] local, float weight, bool[] covered )
		{
			var flags = source.Sequences[index].Flags;
			if ( covered is not null && (flags & SequenceDelta) == 0 )
			{
				for ( var bone = 0; bone < pose.Length; bone++ )
				{
					var mapped = source.BoneMap[bone];
					if ( mapped < 0 || covered[bone] || source.Weight( index, mapped ) <= 0 )
						continue;
					pose[bone] = local[bone];
					covered[bone] = true;
				}
			}
			if ( weight <= 0 )
				return;
			// A masked clip is layered over a body only known at runtime, by its mask in parent space.
			if ( covered is null && (flags & (SequenceDelta | SequenceWorld)) == SequenceWorld )
			{
				WorldSpaceSlerp( pose, source, index, local, weight );
				return;
			}
			for ( var bone = 0; bone < pose.Length; bone++ )
			{
				var mapped = source.BoneMap[bone];
				if ( mapped < 0 )
					continue;
				var amount = weight * source.Weight( index, mapped );
				if ( amount <= 0 )
					continue;
				if ( (flags & SequenceDelta) != 0 )
				{
					var rotation = QuaternionScale( local[bone].Rotation, amount );
					pose[bone].Rotation = ((flags & SequencePost) != 0 ? pose[bone].Rotation * rotation : rotation * pose[bone].Rotation).Normal;
					pose[bone].Position += local[bone].Position * amount;
				}
				else
				{
					pose[bone].Position = Vector3.Lerp( pose[bone].Position, local[bone].Position, amount );
					pose[bone].Rotation = Rotation.Slerp( pose[bone].Rotation, local[bone].Rotation, amount );
				}
			}
		}

		// bone_setup's WorldSpaceSlerp: a weighted bone turns towards the sequence's model-space rotation, so it keeps
		// that orientation whatever the bones above it do. Positions still blend in parent space, and the root's stays.
		private void WorldSpaceSlerp( Transform[] pose, AnimationSource source, int index, Transform[] local, float weight )
		{
			var from = new Rotation[pose.Length];
			var to = new Rotation[pose.Length];
			var result = new Rotation[pose.Length];
			for ( var bone = 0; bone < pose.Length; bone++ )
			{
				var parent = master.Bones[bone].Parent;
				if ( parent >= bone )
					throw new InvalidDataException( "A bone precedes its parent." );
				from[bone] = parent < 0 ? pose[bone].Rotation : from[parent] * pose[bone].Rotation;
				to[bone] = parent < 0 ? local[bone].Rotation : to[parent] * local[bone].Rotation;
				var mapped = source.BoneMap[bone];
				var amount = mapped < 0 ? 0 : weight * source.Weight( index, mapped );
				if ( amount <= 0 )
				{
					result[bone] = parent < 0 ? pose[bone].Rotation : result[parent] * pose[bone].Rotation;
					continue;
				}
				result[bone] = Rotation.Slerp( from[bone], to[bone], amount );
				if ( parent < 0 )
				{
					pose[bone].Rotation = result[bone];
					continue;
				}
				pose[bone].Rotation = (result[parent].Inverse * result[bone]).Normal;
				pose[bone].Position = Vector3.Lerp( pose[bone].Position, local[bone].Position, amount );
			}
		}

		// bone_setup's CalcPoseSingle: the sequence's blend grid at the current pose parameters, in master bone order.
		private Transform[] CalcPose( AnimationSource source, int index, float cycle, (int X, int Y)? cell, Dictionary<string, float> parameters )
		{
			var sequence = source.Sequences[index];
			ValidateGrid( sequence );
			if ( !float.IsFinite( cycle ) )
				cycle = 0;
			if ( cycle < 0 || cycle > 1 )
				cycle = (sequence.Flags & SequenceLooping) != 0 ? cycle - MathF.Floor( cycle ) : Math.Clamp( cycle, 0, 1 );
			var x = cell is { } pinned ? (Index: pinned.X, Weight: 0f) : LocalPoseParameter( source, index, 0, parameters );
			var y = cell is { } pinnedRow ? (Index: pinnedRow.Y, Weight: 0f) : LocalPoseParameter( source, index, 1, parameters );

			Transform[] Row( int row )
			{
				if ( x.Weight > 0.999f )
					return Sample( source, source.Animation( index, x.Index + 1, row ), cycle );
				var pose = Sample( source, source.Animation( index, x.Index, row ), cycle );
				if ( x.Weight >= 0.001f )
					Blend( pose, Sample( source, source.Animation( index, x.Index + 1, row ), cycle ), x.Weight );
				return pose;
			}

			if ( y.Weight > 0.999f )
				return Row( y.Index + 1 );
			var result = Row( y.Index );
			if ( y.Weight >= 0.001f )
				Blend( result, Row( y.Index + 1 ), y.Weight );
			return result;
		}

		private static void Blend( Transform[] pose, Transform[] other, float weight )
		{
			for ( var bone = 0; bone < pose.Length; bone++ )
			{
				pose[bone].Position = Vector3.Lerp( pose[bone].Position, other[bone].Position, weight );
				pose[bone].Rotation = QuaternionBlend( pose[bone].Rotation, other[bone].Rotation, weight );
			}
		}

		// One animation at a cycle, mapped onto the master skeleton. A bone the animation has no track for
		// keeps the master model's bind pose, as CalcVirtualAnimation does.
		private Transform[] Sample( AnimationSource source, int animation, float cycle )
		{
			if ( !decoded.TryGetValue( (source, animation), out var frames ) )
				decoded.Add( (source, animation), frames = source.Mdl.DecodeAnimation( source.Data, source.File.Blocks, animation ) );
			var position = cycle * (frames.FrameCount - 1);
			var frame = Math.Clamp( (int)position, 0, frames.FrameCount - 1 );
			var blend = position - frame;
			// A cycle computed from a frame number can land a hair before that frame.
			if ( blend > 0.999f && frame + 1 < frames.FrameCount )
			{
				frame++;
				blend = 0;
			}
			var interpolate = blend > 0.001f && frame + 1 < frames.FrameCount;
			var stride = source.Mdl.BoneCount;
			var pose = new Transform[bind.Length];
			for ( var bone = 0; bone < pose.Length; bone++ )
			{
				var mapped = source.BoneMap[bone];
				if ( mapped < 0 || !frames.Animated[mapped] )
				{
					pose[bone] = frames.Delta ? Transform.Zero : bind[bone];
					continue;
				}
				var at = frame * stride + mapped;
				pose[bone] = interpolate
					? new Transform( Vector3.Lerp( frames.Positions[at], frames.Positions[at + stride], blend ),
						QuaternionBlend( frames.Rotations[at], frames.Rotations[at + stride], blend ) )
					: new Transform( frames.Positions[at], frames.Rotations[at] );
			}
			return pose;
		}

		// A pose parameter's value in authored units, wrapped and clamped to its range as Studio_SetPoseParameter does.
		// Parameters nobody set are zero, as on a freshly spawned entity.
		private float ParameterValue( string name, Dictionary<string, float> parameters )
		{
			parameters.TryGetValue( name, out var value );
			if ( !poseParameters.TryGetValue( name, out var range ) )
				return value;
			if ( range.Loop != 0 )
			{
				var wrap = (range.Start + range.End) / 2 + range.Loop / 2;
				value -= range.Loop * MathF.Floor( (value + range.Loop - wrap) / range.Loop );
			}
			var normalized = (value - range.Start) / (range.End - range.Start);
			return float.IsFinite( normalized ) ? Math.Clamp( normalized, 0, 1 ) * (range.End - range.Start) + range.Start : range.Start;
		}

		private float ParameterValue( AnimationSource source, int parameter, Dictionary<string, float> parameters )
			=> (uint)parameter < source.PoseParameters.Length ? ParameterValue( source.PoseParameters[parameter].Name, parameters ) : 0;

		// bone_setup's Studio_LocalPoseParameter: the lower cell along one grid axis and the blend towards the next.
		private (int Index, float Weight) LocalPoseParameter( AnimationSource source, int index, int axis, Dictionary<string, float> parameters )
		{
			var sequence = source.Sequences[index];
			var count = axis == 0 ? sequence.GroupSize : sequence.GroupSize2;
			var parameter = axis == 0 ? sequence.ParamIndex : sequence.ParamIndex2;
			if ( count < 2 || (uint)parameter >= source.PoseParameters.Length )
				return (0, 0);
			var value = ParameterValue( source, parameter, parameters );
			var cell = 0;
			float weight;
			if ( sequence.PoseKeyIindex == 0 )
			{
				var start = axis == 0 ? sequence.ParamStart : sequence.ParamStart2;
				var end = axis == 0 ? sequence.ParamEnd : sequence.ParamEnd2;
				weight = (value - start) / (end - start);
				weight = float.IsFinite( weight ) ? Math.Clamp( weight, 0, 1 ) : 0;
				if ( count > 2 )
				{
					cell = Math.Min( (int)(weight * (count - 1)), count - 2 );
					weight = weight * (count - 1) - cell;
				}
			}
			else
			{
				while ( true )
				{
					var low = PoseKey( source, index, axis, cell );
					weight = (value - low) / (PoseKey( source, index, axis, cell + 1 ) - low);
					if ( cell >= count - 2 || !(weight > 1) )
						break;
					cell++;
				}
				weight = float.IsFinite( weight ) ? Math.Clamp( weight, 0, 1 ) : 0;
			}
			return (cell, weight);
		}
	}
}
