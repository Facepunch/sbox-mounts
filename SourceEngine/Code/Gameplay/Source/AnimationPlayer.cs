using System;
using Sandbox.Animation;

namespace Source1;

/// <summary>Builds one AG2 state machine from a model's imported clips and an entity's clip choices.</summary>
public sealed class AnimationPlayer : IDisposable
{
	Graph _graph;
	bool _sharedGraph;
	GroundFoot[] _feet = [];
	Vector3 _renderOrigin;
	Model _model;
	Model _unplayable;
	SkinnedModelRenderer _renderer;
	TimeSince _elapsed;
	float _rateStarted, _clipOffset;
	float ClipTime => _clipOffset + ((float)_elapsed - _rateStarted) * _rate;
	Sandbox.Npcs.BaseNpc _npc;
	SourceGameMount _npcMount;
	int _slot;
	static readonly string[] SlotNames = ["first", "second", "third"];
	readonly string[] _slotClips = new string[SlotNames.Length];
	float _fade;
	readonly float[] _rates = [1, 1, 1];
	ModelSequences _sequences;
	float _eventTime;
	float _rate = 1;
	readonly Dictionary<string, Parameter> _poses = new( StringComparer.OrdinalIgnoreCase );
	(string Target, string Release)[] _grips = [];
	readonly Dictionary<string, SourceModelSequenceData.BlendAxis> _axes = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, SoundEvent> _eventSounds = new();
	Dictionary<string, SoundHandle> _stoppableSounds;
	string _gesture;
	int _gestureSlot;
	TimeUntil _gestureEnd;
	const float GestureFade = 0.1f;
	string[] _gestures = [];
	readonly Dictionary<string, Overlay> _overlays = new( StringComparer.OrdinalIgnoreCase );

	const string None = "none";

	/// <summary>A graph parameter's name, built once, and the value to set on the next tick.</summary>
	sealed class Parameter
	{
		public string Name;
		public string Key;
		public float Value;
	}

	sealed class Overlay
	{
		public string[] Clips = [];
		public string Clip;
		public int Slot;
		public string Selection = None;
		public bool Active;
		public bool Looping;
		public float Rate = 1;
		public Clip Animation;
		public SourceModelSequenceData Sequence;
		public float FadeIn = 0.2f;
		public float FadeOut = 0.2f;
		public float Weight;
		public TimeSince Started;
		public float Offset;
		public float ClipTime => Offset + Started * Rate;
		public float Cycle
		{
			get
			{
				if ( Animation is not { Duration: > 0 } ) return 0;
				var cycle = ClipTime / Animation.Duration;
				return Looping ? cycle % 1 : Math.Clamp( cycle, 0, 1 );
			}
		}
		public float PoseWeight => Weight * (Sequence?.PoseBlend?.Weight( Cycle ) ?? 1);

		public bool Holds( string target )
		{
			if ( Sequence is null ) return false;
			foreach ( var grip in Sequence.IkTargets )
				if ( grip.TargetBone == target ) return true;
			return false;
		}
		public string IdKey;
		public string WeightKey;
		public string[] RateKeys;
	}

	/// <summary>
	/// Play sequences that carry a blend grid (movement direction, aim) as a blend over their grid cells,
	/// driven by the pose parameters their axes name. Off, a sequence plays its neutral cell.
	/// </summary>
	public bool UseBlendGrids { get; set; }

	/// <summary>Additive one-shot delta sequences, such as flinch gestures; see <see cref="PlayGesture"/>.</summary>
	public IReadOnlyList<string> Gestures { get; set; } = [];

	/// <summary>
	/// Named overlay layers and the full (non-delta) clips each may play, such as an upper-body reload over locomotion.
	/// A clip blends in through its sequence's bone mask (Source weightlist), or over the whole body without one; see <see cref="PlayOverlay"/>.
	/// </summary>
	public IReadOnlyDictionary<string, IReadOnlyList<string>> OverlayLayers { get; set; } = new Dictionary<string, IReadOnlyList<string>>();

	/// <summary>The overlay that follows the base sequence's animation cycle.</summary>
	public string SynchronizedOverlay { get; set; }

	/// <summary>
	/// Bones pulled onto other bones of the final pose by two-bone IK, such as hands onto a weapon's grip bones,
	/// weighted by the active hold and the release track for each bone.
	/// </summary>
	public IReadOnlyList<(string Effector, string Target)> BoneLocks { get; set; } = [];

	public void SetPoseParameter( string name, float value ) => Set( _poses, "pose:", name, value );
	public void SetMoveYaw( Vector3 velocity, Rotation facing )
	{
		var travel = velocity.WithZ( 0 );
		// The outgoing walk still samples this direction while it fades to idle.
		if ( travel.LengthSquared > 100 )
			SetPoseParameter( "move_yaw", Angles.NormalizeAngle( Rotation.LookAt( travel ).Yaw() - facing.Yaw() ) );
	}

	/// <summary>Ground distance per second at the same directional blend the graph is playing.</summary>
	public float GroundSpeed( SourceModelSequenceData sequence )
	{
		if ( sequence is null ) return 0;
		if ( sequence.BlendAxes is not [{ Parameter: "move_yaw" } axis] || !_poses.TryGetValue( "move_yaw", out var pose ) )
			return sequence.GroundSpeed;

		var stops = axis.Positions;
		var motion = sequence.BlendGridMovement;
		var speeds = sequence.BlendGridSpeeds;
		if ( motion.Length != stops.Length || speeds.Length != stops.Length ) return sequence.GroundSpeed;

		var yaw = Wrap( axis, pose.Value );
		for ( var i = 1; i < stops.Length; i++ )
		{
			if ( yaw > stops[i] || stops[i] <= stops[i - 1] ) continue;
			var t = Math.Clamp( (yaw - stops[i - 1]) / (stops[i] - stops[i - 1]), 0, 1 );
			var a = motion[i - 1];
			var b = motion[i];
			if ( a.IsNearlyZero() || b.IsNearlyZero() ) return MathX.Lerp( speeds[i - 1], speeds[i], t );
			// The model blends travel per cycle and cycles per second independently.
			return Vector3.Lerp( a, b, t ).Length * MathX.Lerp( speeds[i - 1] / a.Length, speeds[i] / b.Length, t );
		}
		return sequence.GroundSpeed;
	}

	public void ApplyBoneLocks( string holdLayer, string releaseLayer )
	{
		if ( _grips.Length == 0 || !_overlays.TryGetValue( holdLayer, out var hold ) ) return;
		_overlays.TryGetValue( releaseLayer, out var release );
		var clip = release is { Weight: > 0 } ? release.Animation : null;
		var cycle = release?.Cycle ?? 0;
		foreach ( var (target, channel) in _grips )
		{
			var fraction = clip is not null && clip.TryGetFloatChannel( cycle, SourceAnimations.IkRelease, channel, out var value )
				? value * release.Weight : 0f;
			var weight = hold.Holds( target ) ? hold.PoseWeight * (1 - fraction) : 0f;
			if ( clip is not null && release.Holds( target ) )
				weight += (1 - weight) * release.PoseWeight;
			_renderer.Animation.Parameters.TrySet( target, weight );
		}
	}

	static void Set( Dictionary<string, Parameter> parameters, string prefix, string name, float value )
	{
		if ( !parameters.TryGetValue( name, out var parameter ) )
			parameters[name] = parameter = new Parameter { Name = name, Key = prefix + name };
		parameter.Value = value;
	}

	public event Action<SourceModelSequenceData.Event> EventFired;
	public string Current { get; private set; }
	public bool IsReady => _graph.IsValid() && _renderer.IsValid() && _renderer.Animation.IsValid;
	public float Duration => ClipDuration( Current );
	public float ClipDuration( string name ) => _model?.AnimationClips().TryGetValue( name ?? "", out var clip ) == true ? clip.Duration : 0;
	public float Elapsed => _elapsed;
	/// <summary>Source can advance a transition once its authored fade-out begins.</summary>
	public bool InFadeOut => ClipTime >= Duration - (_sequences.Get( Current )?.FadeOut ?? 0);
	public bool Finished => ClipTime >= Duration;

	/// <summary>
	/// Builds the graph once per model, starting directly in <paramref name="initial"/> so the first clip does not blend in from another.
	/// A model with none of the choices playable is not tried again.
	/// </summary>
	public bool Ensure( SkinnedModelRenderer renderer, IEnumerable<(string Name, bool Loop)> choices, string initial = null,
		Model animationModel = null, Skeleton targetSkeleton = null, Func<Clip, Clip> retarget = null )
	{
		if ( !renderer.IsValid() || !renderer.Model.IsValid() ) return false;
		animationModel ??= renderer.Model;
		if ( !animationModel.AnimationSkeleton().IsValid() ) return false;
		if ( _model == animationModel && _graph.IsValid() ) return true;
		if ( _unplayable == animationModel ) return false;
		Dispose();
		_model = animationModel;
		_renderer = renderer;
		_npc = Application.IsDedicatedServer ? null : renderer.GetComponentInParent<Sandbox.Npcs.BaseNpc>();
		_npcMount = _npc?.Renderer == renderer ? Sandbox.Mounting.Directory.Get( MountContent.Of( _model ) ) as SourceGameMount : null;
		_sequences = ModelSequences.Of( _model );
		_eventSounds.Clear();
		_axes.Clear();
		_gestures = [];
		_gesture = null;
		_overlays.Clear();
		var available = choices.Where( x => !string.IsNullOrWhiteSpace( x.Name ) && HasClip( x.Name ) )
			.DistinctBy( x => x.Name, StringComparer.OrdinalIgnoreCase )
			.OrderBy( x => string.Equals( x.Name, initial, StringComparison.OrdinalIgnoreCase ) ? 0 : 1 ).ToArray();
		if ( available.Length == 0 )
		{
			_unplayable = _model;
			_model = null;
			return false;
		}

		if ( retarget is null && !Application.IsDedicatedServer )
		{
			var mount = MountContent.Of( _model );
			var female = _npcMount?.IsFemaleActor( _model.ResourcePath ) == true;
			foreach ( var evt in _sequences.Events )
			{
				if ( string.IsNullOrWhiteSpace( evt.Options ) ) continue;
				if ( evt.Name == "AE_CL_STOPSOUND" )
					(_stoppableSounds ??= new( StringComparer.OrdinalIgnoreCase )).TryAdd( evt.Options, null );
				else if ( Sounds.IsSoundEvent( evt ) && !_eventSounds.ContainsKey( evt.Options ) )
					_eventSounds[evt.Options] = Sounds.Sound( evt.Options, mount, female );
			}
		}

		// Plain sequence players only differ by their ordered clips and looping flags.
		_sharedGraph = targetSkeleton is null && retarget is null && !UseBlendGrids
			&& Gestures.Count == 0 && OverlayLayers.Count == 0 && BoneLocks.Count == 0;
		if ( _npc?.Renderer == renderer && renderer.GameObject != _npc.GameObject && retarget is null
			&& _model.AnimationClips().GroundBones is { Length: > 0 } feet )
		{
			_feet = feet.Where( bone => _model.Bones.GetBone( bone )?.Parent?.Parent is not null && _model.Bones.GetBone( GroundFoot.Pad( bone ) ) is not null && _model.Bones.GetBone( GroundFoot.Ankle( bone ) ) is not null )
				.Select( bone => new GroundFoot( bone ) ).ToArray();
			_renderOrigin = renderer.LocalPosition;
			_sharedGraph = false;
		}
		Graph Build() => BuildGraph( available, targetSkeleton, retarget );
		_graph = _sharedGraph ? _model.AnimationClips().GetPlaybackGraph( available, Build ) : Build();
		renderer.Animation.Graph = _graph;
		renderer.UseAnimGraph = true;
		// The first state is the graph's default, so the first Play lands on slot 0 without a transition.
		_slot = -1;
		Play( available[0].Name, restart: true );
		return true;
	}

	Graph BuildGraph( (string Name, bool Loop)[] available, Skeleton targetSkeleton, Func<Clip, Clip> retarget )
	{
		var builder = Graph.Builder;
		builder.WithSkeleton( targetSkeleton ?? _model.AnimationSkeleton() );
		Clip ResolveClip( string name )
		{
			var clip = _model.AnimationClips()[name];
			return retarget is null ? clip : retarget( clip );
		}
		var selected = builder.IdParameter( "sequence", SlotNames );
		var names = available.Select( x => x.Name ).ToArray();
		var slotIds = SlotNames.Select( ( _, i ) => builder.IdParameter( $"clip{i}", names ) ).ToArray();
		var fade = builder.AddControlParameter( "sequenceFade", GraphBuilder.NodeKind.Float, new() { Max = 4 } );
		var speeds = Enumerable.Range( 0, SlotNames.Length ).Select( slot => builder.AddControlParameter( $"rate{slot}", GraphBuilder.NodeKind.Float,
			new() { DefaultFloat = 1, Min = 0, Max = 4 } ) ).ToArray();
		var poseLayers = (_model.GetData<SourceModelPoseLayerData[]>() ?? []).Where( p => p.Clips.Length >= 2 && p.Clips.Length == p.Positions.Length
			&& p.Clips.All( HasClip ) ).ToArray();
		var poseParameters = poseLayers.GroupBy( p => p.Parameter ).ToDictionary( g => g.Key,
			g => builder.AddControlParameter( $"pose:{g.Key}", GraphBuilder.NodeKind.Float,
				new() { DefaultFloat = 0, Min = g.SelectMany( p => p.Positions ).Min(), Max = g.SelectMany( p => p.Positions ).Max() } ),
			StringComparer.OrdinalIgnoreCase );
		// Only the chosen sequences: checking a blend's clips decodes them.
		var staticBlends = available.Select( x => _sequences.Get( x.Name ) ).Where( s => s is not null && !string.IsNullOrWhiteSpace( s.BlendParameter ) && s.BlendClips.Length >= 2
			&& s.BlendClips.Length == s.BlendPositions.Length && s.BlendClips.All( HasClip ) ).ToDictionary( s => s.Name, StringComparer.OrdinalIgnoreCase );
		foreach ( var blend in staticBlends.Values )
			if ( !poseParameters.ContainsKey( blend.BlendParameter ) )
				poseParameters.Add( blend.BlendParameter, builder.AddControlParameter( $"pose:{blend.BlendParameter}", GraphBuilder.NodeKind.Float,
					new() { DefaultFloat = 0, Min = blend.BlendPositions.Min(), Max = blend.BlendPositions.Max() } ) );

		bool HasGrid( SourceModelSequenceData sequence ) => sequence.BlendAxes.Length is 1 or 2
			&& sequence.BlendGrid.Length == sequence.BlendAxes.Aggregate( 1, ( n, a ) => n * a.Positions.Length )
			&& sequence.BlendGrid.All( HasClip );

		// The builder takes every control parameter before the first node, so the grids' axes, the layer
		// weights and the gesture selection are declared here, ahead of the clips that use them.
		var gridded = available.Select( x => _sequences.Get( x.Name ) )
			.Where( s => UseBlendGrids && s is { Delta: false } && !staticBlends.ContainsKey( s.Name ) && HasGrid( s ) );
		var clipLayers = available.Select( x => _sequences.Get( x.Name ) )
			.Where( s => UseBlendGrids && s is { Delta: false, AdditiveLayers.Length: > 0 } )
			.ToDictionary( s => s.Name, s => s.AdditiveLayers.Select( _sequences.Get ).Where( layer => layer is { Delta: true } && HasGrid( layer ) ).ToArray(), StringComparer.OrdinalIgnoreCase );
		foreach ( var axis in gridded.Concat( clipLayers.Values.SelectMany( layers => layers ).Distinct() ).SelectMany( s => s.BlendAxes ) )
			Parameter( axis );
		_gestures = Gestures.Where( x => _sequences.Get( x ) is { Delta: true } && HasClip( x ) ).Distinct().ToArray();
		var gestureKeys = SlotKeys( _gestures );
		var gestureId = _gestures.Length > 0 ? builder.IdParameter( "gesture", gestureKeys ) : null;
		var gestureWeight = _gestures.Length > 0 ? builder.AddControlParameter( "gesture_weight", GraphBuilder.NodeKind.Float, new() { DefaultFloat = 0, Min = 0, Max = 1 } ) : null;
		var overlayParameters = new List<(string[] Keys, GraphBuilder.Node Id, GraphBuilder.Node Weight, GraphBuilder.Node[] Rates, Dictionary<string, SourceModelSequenceData[]> Companions, bool Synchronized)>();
		foreach ( var (name, list) in OverlayLayers )
		{
			var playable = list.Where( x => _sequences.Get( x ) is { Delta: false } && HasClip( x ) ).Distinct().ToArray();
			if ( playable.Length == 0 )
				continue;

			var overlay = new Overlay
			{
				Clips = playable,
				IdKey = $"overlay:{name}",
				WeightKey = $"overlay_weight:{name}",
				RateKeys = [$"overlay_rate0:{name}", $"overlay_rate1:{name}"]
			};
			_overlays[name] = overlay;
			var keys = SlotKeys( playable );
			var companions = playable.ToDictionary( x => x, x => _sequences.Get( x ).AdditiveLayers.Select( _sequences.Get )
				.Where( a => a is { Delta: true } && (HasGrid( a ) || HasClip( a.Name )) ).ToArray() );
			foreach ( var axis in companions.Values.SelectMany( x => x ).Where( HasGrid ).SelectMany( x => x.BlendAxes ) )
				Parameter( axis );
			overlayParameters.Add( (keys, builder.IdParameter( overlay.IdKey, keys ),
				builder.AddControlParameter( overlay.WeightKey, GraphBuilder.NodeKind.Float, new() { DefaultFloat = 0, Min = 0, Max = 1 } ),
				overlay.RateKeys.Select( key => builder.AddControlParameter( key, GraphBuilder.NodeKind.Float, new() { DefaultFloat = 1, Min = 0, Max = 4 } ) ).ToArray(),
				companions, name == SynchronizedOverlay) );
		}

		var locks = BoneLocks.Where( x => _model.Bones.GetBone( x.Effector ) is not null && _model.Bones.GetBone( x.Target ) is not null ).ToArray();
		_grips = locks.Select( pair => (pair.Target, SourceAnimations.ReleaseChannel( pair.Effector )) ).ToArray();
		var groundParameters = _feet.Select( foot => foot.Parameters( builder, _model ) ).ToArray();
		var lockWeights = _grips.Select( grip => builder.FloatParameter( grip.Target, 0, 1 ) ).ToArray();

		GraphBuilder.Node Parameter( SourceModelSequenceData.BlendAxis axis )
		{
			_axes.TryAdd( axis.Parameter, axis );
			if ( poseParameters.TryGetValue( axis.Parameter, out var existing ) )
				return existing;
			var node = builder.AddControlParameter( $"pose:{axis.Parameter}", GraphBuilder.NodeKind.Float, new() { DefaultFloat = 0, Min = axis.Min, Max = axis.Max } );
			poseParameters.Add( axis.Parameter, node );
			return node;
		}

		GraphBuilder.Node Cell( string clip, bool loop )
		{
			var animation = ResolveClip( clip );
			return animation.FrameCount <= 1
				? builder.Pose( animation, new GraphBuilder.PoseSettings() )
				: builder.Clip( animation, new() { Looping = loop, SampleRootMotion = false } );
		}

		GraphBuilder.Node Grid( SourceModelSequenceData sequence, bool unlayered = false )
		{
			var x = sequence.BlendAxes[0];
			GraphBuilder.Node Sample( string clip ) => Cell( unlayered ? SourceAnimations.UnlayeredClip( clip ) : clip, sequence.Looping );
			GraphBuilder.Node Row( int row )
			{
				var blend = builder.BlendSpace( Parameter( x ), looping: sequence.Looping );
				for ( var i = 0; i < x.Positions.Length; i++ )
					blend.Add( x.Positions[i], Sample( sequence.BlendGrid[i + row * x.Positions.Length] ) );
				return blend;
			}
			if ( sequence.BlendAxes.Length == 1 )
				return Row( 0 );
			var y = sequence.BlendAxes[1];
			// Native 2D blend spaces store vertex indices in a byte.
			if ( sequence.BlendGrid.Length > byte.MaxValue + 1 )
			{
				var rows = builder.BlendSpace( Parameter( y ), looping: sequence.Looping );
				for ( var j = 0; j < y.Positions.Length; j++ )
					rows.Add( y.Positions[j], Row( j ) );
				return rows;
			}

			var width = x.Positions.Length;
			var height = y.Positions.Length;
			var points = new Vector2[width * height];
			var triangles = new List<int>( (width - 1) * (height - 1) * 6 );
			for ( var row = 0; row < height; row++ )
			for ( var col = 0; col < width; col++ )
			{
				var a = row * width + col;
				points[a] = new( x.Positions[col], y.Positions[row] );
				if ( row == height - 1 || col == width - 1 ) continue;

				var b = a + 1;
				var c = a + width;
				var d = c + 1;
				if ( ((row + col) & 1) == 0 )
					triangles.AddRange( [a, b, d, d, c, a] );
				else
					triangles.AddRange( [c, a, b, b, d, c] );
			}

			var hull = new List<int>();
			for ( var col = 0; col < width; col++ ) hull.Add( col );
			for ( var row = 1; row < height; row++ ) hull.Add( row * width + width - 1 );
			for ( var col = width - 2; col >= 0; col-- ) hull.Add( (height - 1) * width + col );
			for ( var row = height - 2; row >= 0; row-- ) hull.Add( row * width );

			return builder.BlendSpace2D( Parameter( x ), Parameter( y ), new GraphBuilder.BlendSpace2DSettings
			{
				Sources = sequence.BlendGrid.Select( Sample ).ToArray(),
				Positions = points, Indices = triangles, HullIndices = hull, Looping = sequence.Looping
			} );
		}

		GraphBuilder.Node BasePose( string name, bool loop, bool unlayered )
		{
			if ( staticBlends.TryGetValue( name, out var sequence ) )
			{
				var blend = builder.BlendSpace( poseParameters[sequence.BlendParameter], looping: false );
				for ( var i = 0; i < sequence.BlendClips.Length; i++ )
					blend.Add( sequence.BlendPositions[i], builder.Pose( ResolveClip( unlayered ? SourceAnimations.UnlayeredClip( sequence.BlendClips[i] ) : sequence.BlendClips[i] ), new GraphBuilder.PoseSettings() ) );
				return blend;
			}
			var metadata = _sequences.Get( name );
			if ( UseBlendGrids && metadata is { Delta: false } && HasGrid( metadata ) )
				return Grid( metadata, unlayered );
			return builder.Clip( ResolveClip( unlayered ? SourceAnimations.UnlayeredClip( name ) : name ), new() { Looping = metadata?.Looping ?? loop, SampleRootMotion = false } );
		}

		// Aiming is authored per sequence. Layer its grids before the state machine blends
		// the pose, so a run, shot or reload each carries its own arm position.
		var poseGrids = new Dictionary<string, GraphBuilder.Node>( StringComparer.OrdinalIgnoreCase );
		GraphBuilder.Node Playback( string name, bool loop )
		{
			if ( !clipLayers.TryGetValue( name, out var companions ) || companions.Length == 0 )
				return BasePose( name, loop, false );

			var layered = builder.Layers( BasePose( name, loop, _model.AnimationClips().ContainsKey( SourceAnimations.UnlayeredClip( name ) ) ) );
			foreach ( var companion in companions )
			{
				if ( !poseGrids.TryGetValue( companion.Name, out var grid ) )
				{
					grid = Grid( companion );
					if ( companion.BlendGrid.All( cell => _model.AnimationClips()[cell].FrameCount <= 1 ) )
						poseGrids[companion.Name] = grid;
				}
				layered.AddLayer( grid, new() { BlendMode = GraphBuilder.LayerBlendMode.Additive, IgnoreEvents = true } );
			}
			GraphBuilder.Node result = layered;
			var targets = companions.SelectMany( companion => companion.IkTargets ).DistinctBy( target => target.Bone );
			foreach ( var target in targets.Concat( _sequences.Get( name ).IkTargets ) )
				result = builder.TwoBoneIK( result, target.Bone, builder.BoneTarget( target.TargetBone ), new() { WorldSpace = false, BlendTime = 0 } );
			return result;
		}

		// One state per key, switched by an id parameter, with a transition from every state to every other.
		GraphBuilder.StateMachineBuilder Selector( GraphBuilder.Node id, IReadOnlyList<string> keys, Func<int, GraphBuilder.Node> state, Func<int, int, float> duration, GraphBuilder.Easing easing = GraphBuilder.Easing.Linear )
		{
			var machine = builder.StateMachine();
			var states = keys.Select( ( _, i ) => machine.AddState( state( i ) ) ).ToArray();
			for ( var to = 0; to < states.Length; to++ )
			{
				var condition = builder.IdCompare( id, keys[to] );
				for ( var from = 0; from < states.Length; from++ )
					if ( from != to )
						machine.AddTransition( states[from], states[to], condition,
							new GraphBuilder.TransitionSettings { Duration = duration( from, to ), BlendEasing = easing, ClampDuration = false, CanBeForced = true } );
			}
			return machine;
		}

		GraphBuilder.StateMachineBuilder SlotMachine()
		{
			var machine = builder.StateMachine();
			GraphBuilder.StateMachineBuilder.State State( int i ) => machine.AddState( builder.SpeedScale( builder.IdBasedSelector( slotIds[i],
				available.Select( x => (x.Name, Playback( x.Name, x.Loop )) ) ), speeds[i] ) );
			// During an interrupted fade both previous slots still contribute to the outgoing pose.
			var states = SlotNames.Select( ( _, i ) => State( i ) ).ToArray();
			var transition = new GraphBuilder.TransitionSettings { DurationOverride = fade, BlendEasing = GraphBuilder.Easing.InOutSine, CanBeForced = true };
			for ( var to = 0; to < states.Length; to++ )
			{
				var condition = builder.IdCompare( selected, SlotNames[to] );
				foreach ( var from in states )
					if ( from != states[to] ) machine.AddTransition( from, states[to], condition, transition );
			}
			return machine;
		}
		var layers = builder.Layers( SlotMachine().Build() );
		foreach ( var overlay in overlayParameters )
		{
			// A two-slot machine per overlay layer, masked per clip by its sequence's weightlist; looping in the graph either way,
			// as a one-shot fades out before its end and a caller may loop a non-looping clip.
			// The inactive state must share the clips' parent space; ZeroPose is additive.
			var machine = Selector( overlay.Id, overlay.Keys, i =>
			{
				if ( i == 0 ) return builder.ReferencePose();
				var clip = ClipOf( overlay.Keys[i] );
				var unlayered = SourceAnimations.UnlayeredClip( clip );
				if ( overlay.Companions[clip].Length > 0 && _model.AnimationClips().ContainsKey( unlayered ) )
					clip = unlayered;
				return builder.SpeedScale( builder.Clip( ResolveClip( clip ), new() { Looping = true, SampleRootMotion = false } ), overlay.Rates[SlotOf( overlay.Keys[i] )] );
			}, ( from, to ) => from == 0 || to == 0 ? 0 : 0.15f );
			layers.AddLayer( machine, new() { BlendMode = GraphBuilder.LayerBlendMode.Overlay, Synchronized = overlay.Synchronized, IgnoreEvents = true, Weight = overlay.Weight, BoneMask = OverlayMask( builder, overlay.Keys, overlay.Id ) } );

			// A masked clip's additive layers (the spine of an arm gesture, an aim matrix) play beside it, keyed by the overlay's selection
			// so they switch and restart with it, one layer per position in the clips' AdditiveLayers lists.
			var depth = overlay.Companions.Values.Max( x => x.Length );
			for ( var index = 0; index < depth; index++ )
			{
				var position = index;
				var companions = Selector( overlay.Id, overlay.Keys, i =>
				{
					var clip = i == 0 ? null : ClipOf( overlay.Keys[i] );
					if ( clip is null || overlay.Companions[clip].Length <= position )
						return builder.ZeroPose();
					var sequence = overlay.Companions[clip][position];
					var node = HasGrid( sequence ) ? Grid( sequence ) : Cell( sequence.Name, true );
					return builder.SpeedScale( node, overlay.Rates[SlotOf( overlay.Keys[i] )] );
				}, ( _, _ ) => 0.15f );
				layers.AddLayer( companions, new() { BlendMode = GraphBuilder.LayerBlendMode.Additive, Synchronized = overlay.Synchronized, IgnoreEvents = true, Weight = overlay.Weight } );
			}

			var lockedClips = overlay.Companions.SelectMany( pair => pair.Value.SelectMany( s => s.IkTargets )
				.Select( t => (Clip: pair.Key, t.Bone, t.TargetBone) ) );
			foreach ( var group in lockedClips.GroupBy( t => (t.Bone, t.TargetBone) ) )
			{
				var clipsWithLock = group.Select( t => t.Clip ).ToHashSet();
				var weight = overlay.Weight;
				if ( clipsWithLock.Count != overlay.Companions.Count )
				{
					var conditions = overlay.Keys.Skip( 1 ).Where( key => clipsWithLock.Contains( ClipOf( key ) ) );
					weight = builder.Multiply( weight, builder.FloatSelector( conditions.Select( key => (builder.IdCompare( overlay.Id, key ), 1f) ), easeTime: 0.15f ) );
				}
				var ik = builder.TwoBoneIK( layers, group.Key.Bone, builder.BoneTarget( group.Key.TargetBone ), new() { WorldSpace = false, BlendTime = 0, Weight = weight } );
				layers = builder.Layers( ik );
			}
		}
		foreach ( var pose in poseLayers )
		{
			var blend = builder.BlendSpace( poseParameters[pose.Parameter], looping: false );
			for ( var i = 0; i < pose.Clips.Length; i++ )
				blend.Add( pose.Positions[i], builder.Pose( ResolveClip( pose.Clips[i] ), new GraphBuilder.PoseSettings() ) );
			layers.AddLayer( blend, new() { BlendMode = GraphBuilder.LayerBlendMode.Additive, IgnoreEvents = true } );
		}
		if ( _gestures.Length > 0 )
		{
			// One-shot additive gestures, two slots each so a repeat restarts cleanly.
			var gestures = Selector( gestureId, gestureKeys, i => i == 0 ? builder.ZeroPose()
				: builder.Clip( ResolveClip( ClipOf( gestureKeys[i] ) ), new() { Looping = false, SampleRootMotion = false } ), ( _, _ ) => GestureFade );
			layers.AddLayer( gestures, new() { BlendMode = GraphBuilder.LayerBlendMode.Additive, IgnoreEvents = true, Weight = gestureWeight } );
		}
		GraphBuilder.Node root = layers;
		for ( var i = 0; i < locks.Length; i++ )
			root = builder.TwoBoneIK( root, locks[i].Effector, builder.BoneTarget( locks[i].Target ), new() { WorldSpace = false, Weight = lockWeights[i], BlendTime = 0 } );
		for ( var i = 0; i < _feet.Length; i++ ) root = _feet[i].Build( builder, root, groundParameters[i] );
		return builder.Create( root, "Mounted animation" );
	}

	// Mounted clips decode on first lookup; a sequence that fails to decode is listed but has no clip.
	bool HasClip( string name ) => _model.AnimationClips().TryGetValue( name, out var clip ) && clip is not null;

	/// <summary>"none", then two slot keys per clip, such as reload:0 and reload:1.</summary>
	static string[] SlotKeys( IEnumerable<string> clips ) => clips.SelectMany( x => new[] { $"{x}:0", $"{x}:1" } ).Prepend( None ).ToArray();
	static string ClipOf( string key ) => key[..key.LastIndexOf( ':' )];
	static int SlotOf( string key ) => key[^1] - '0';

	/// <summary>The overlay's bone mask, switched with its selection when its clips' weightlists differ.</summary>
	GraphBuilder.Node OverlayMask( GraphBuilder builder, string[] keys, GraphBuilder.Node id )
	{
		var masks = new Dictionary<string, GraphBuilder.Node>();
		GraphBuilder.Node Mask( string clip )
		{
			var name = _sequences.Get( clip ).BoneMask ?? "";
			if ( !masks.TryGetValue( name, out var mask ) )
				masks[name] = mask = name == "" ? builder.BoneMask( 1f ) : builder.BoneMask( name );
			return mask;
		}
		var options = keys.Skip( 1 ).Select( key => (key, Mask( ClipOf( key ) )) ).ToArray();
		return masks.Count == 1 ? masks.Values.First()
			: builder.BoneMaskSelector( id, builder.BoneMask( 0f ), options, new() { SwitchDynamically = true, BlendTime = 0.15f } );
	}

	/// <summary>
	/// Plays a clip from <see cref="OverlayLayers"/> on the named layer, fading in by the sequence's fadein; a one-shot fades out
	/// on its own at the end. <paramref name="loop"/> overrides the sequence's looping. Returns false when the layer cannot play it.
	/// </summary>
	public bool PlayOverlay( string layer, string clip, float rate = 1, bool restart = false, bool? loop = null, bool fade = true )
	{
		if ( !_overlays.TryGetValue( layer ?? "", out var overlay ) || !overlay.Clips.Contains( clip ) )
			return false;

		if ( overlay.Clip != clip || restart || !overlay.Active && overlay.Weight <= 0 )
		{
			var sequence = _sequences.Get( clip );
			overlay.Clip = clip;
			overlay.Slot = 1 - overlay.Slot;
			overlay.Selection = $"{clip}:{overlay.Slot}";
			overlay.Started = 0;
			overlay.Offset = 0;
			overlay.Looping = loop ?? sequence.Looping;
			overlay.Animation = _model.AnimationClips()[clip];
			overlay.Sequence = sequence;
			overlay.FadeIn = fade ? MathF.Max( sequence.FadeIn, 0.05f ) : 0;
			overlay.FadeOut = fade ? MathF.Max( sequence.FadeOut, 0.05f ) : 0;
		}

		overlay.Active = true;
		rate = Math.Clamp( rate, 0.01f, 4 );
		if ( overlay.Rate != rate )
		{
			overlay.Offset = overlay.ClipTime;
			overlay.Started = 0;
			overlay.Rate = rate;
		}
		return true;
	}

	/// <summary>Fades the named overlay layer out.</summary>
	public void StopOverlay( string layer )
	{
		if ( _overlays.TryGetValue( layer ?? "", out var overlay ) )
			overlay.Active = false;
	}

	/// <summary>How far the named overlay layer has faded in.</summary>
	public float OverlayWeight( string layer ) => _overlays.TryGetValue( layer ?? "", out var overlay ) ? overlay.Weight : 0;

	/// <summary>The clip the named overlay layer is showing, until it has faded out.</summary>
	public string OverlayClip( string layer ) => _overlays.TryGetValue( layer ?? "", out var overlay ) && (overlay.Active || overlay.Weight > 0) ? overlay.Clip : null;

	/// <summary>Layers a one-shot additive gesture from <see cref="Gestures"/> over whatever is playing.</summary>
	public bool PlayGesture( string name )
	{
		if ( !_gestures.Contains( name ) || !_model.AnimationClips().TryGetValue( name, out var clip ) )
			return false;
		_gestureSlot = 1 - _gestureSlot;
		_gesture = $"{name}:{_gestureSlot}";
		_gestureEnd = clip.Duration;
		return true;
	}

	public void Play( string name, float rate = 1, bool restart = false )
	{
		if ( _model?.AnimationClips().ContainsKey( name ?? "" ) != true ) return;
		if ( Current != name || restart )
		{
			// Flush the last interval before changing clips, including cycle-one
			// events when the caller switches to idle on the finishing frame.
			if ( Current is not null ) DispatchEvents();
			_fade = _sequences.Get( Current ?? "" ) is { } outgoing && _sequences.Get( name ) is { } incoming
				? incoming.Snap ? 0 : MathF.Min( outgoing.FadeOut, incoming.FadeIn ) : 0.08f;
			Current = name;
			_elapsed = 0;
			_rateStarted = _clipOffset = 0;
			_eventTime = -0.0001f;
			_slot = (_slot + 1) % SlotNames.Length;
			_slotClips[_slot] = name;
		}
		rate = Math.Clamp( rate, 0.01f, 4 );
		if ( rate != _rate )
		{
			_clipOffset = ClipTime;
			_rateStarted = _elapsed;
			_rate = rate;
		}
		_rates[_slot] = _rate;
	}

	internal void ApplySequence( SkinnedModelRenderer renderer )
	{
		var parameters = renderer.Animation.Parameters;
		parameters.TrySet( "clip0", _slotClips[0] ?? Current );
		parameters.TrySet( "clip1", _slotClips[1] ?? Current );
		parameters.TrySet( "clip2", _slotClips[2] ?? Current );
		parameters.TrySet( "rate2", _rates[2] );
		parameters.TrySet( "sequenceFade", _fade );
		parameters.TrySet( "sequence", SlotNames[_slot] );
		parameters.TrySet( "rate0", _rates[0] );
		parameters.TrySet( "rate1", _rates[1] );
		foreach ( var pose in _poses.Values )
			parameters.TrySet( pose.Key, _axes.TryGetValue( pose.Name, out var axis ) ? Wrap( axis, pose.Value ) : pose.Value );
	}

	public void Tick()
	{
		if ( !IsReady ) return;
		ApplySequence( _renderer );
		if ( _feet.Length > 0 )
		{
			var origin = _renderer.WorldTransform.WithPosition( _renderer.GameObject.Parent.WorldTransform.PointToWorld( _renderOrigin ) );
			float low = 0, high = 0;
			foreach ( var foot in _feet )
			{
				float unlatch = 0;
				foreach ( var overlay in _overlays.Values )
					if ( overlay.Weight > 0 && overlay.Animation.TryGetFloatChannel( overlay.Cycle, SourceAnimations.IkRelease, foot.UnlatchChannel, out var value ) )
						unlatch = MathF.Max( unlatch, value * overlay.Weight );
				if ( foot.Update( _renderer, _npc, origin, unlatch ) is not { } contact ) continue;
				low = MathF.Min( low, contact );
				high = MathF.Max( high, contact );
			}
			const float step = 18;
			var offset = Math.Clamp( low, -step + Math.Clamp( high - low - step, 0, step ), 0 );
			_renderer.LocalPosition = _renderOrigin + Vector3.Up * MathX.Lerp( _renderer.LocalPosition.z - _renderOrigin.z, offset / origin.Scale.z, 1 - MathF.Exp( -Time.Delta * 24 ) );
		}
		var parameters = _renderer.Animation.Parameters;
		if ( _gestures.Length > 0 )
		{
			var active = _gesture is not null && _gestureEnd > 0;
			parameters.TrySet( "gesture", active ? _gesture : None );
			// Keep evaluating the transition back to the additive zero pose.
			parameters.TrySet( "gesture_weight", _gesture is not null && _gestureEnd > -GestureFade ? 1f : 0f );
		}
		foreach ( var overlay in _overlays.Values )
		{
			// A one-shot starts fading out so that it is gone as its last frame plays.
			var remaining = overlay.Looping ? float.MaxValue : ((overlay.Animation?.Duration ?? 0) - overlay.ClipTime) / overlay.Rate;
			if ( remaining <= 0 )
				overlay.Active = false;
			if ( overlay.FadeIn == 0 )
				overlay.Weight = overlay.Active ? 1 : 0;
			else if ( overlay.Sequence?.PoseBlend.HasValue == true && overlay.Active )
				overlay.Weight = 1;
			else
			{
				var target = overlay.Active ? MathF.Min( 1, remaining / overlay.FadeOut ) : 0;
				var fade = target > overlay.Weight ? overlay.FadeIn : overlay.FadeOut;
				overlay.Weight = target > overlay.Weight ? MathF.Min( target, overlay.Weight + Time.Delta / fade ) : MathF.Max( target, overlay.Weight - Time.Delta / fade );
			}
			parameters.TrySet( overlay.IdKey, overlay.Weight > 0 && overlay.Clip is not null ? overlay.Selection : None );
			parameters.TrySet( overlay.WeightKey, overlay.PoseWeight );
			parameters.TrySet( overlay.RateKeys[overlay.Slot], overlay.Rate );
		}
		DispatchEvents();
	}

	/// <summary>A pose parameter in its axis range: wrapped for periodic axes such as move_yaw, clamped otherwise.</summary>
	static float Wrap( SourceModelSequenceData.BlendAxis axis, float value )
	{
		if ( axis.Loop > 0 )
			value = axis.Min + ((value - axis.Min) % axis.Loop + axis.Loop) % axis.Loop;
		return Math.Clamp( value, axis.Min, axis.Max );
	}

	SoundEvent EventSound( SourceModelSequenceData.Event evt )
	{
		if ( !Sounds.IsSoundEvent( evt ) || evt.Options is not { } name || !_eventSounds.TryGetValue( name, out var sound ) ) return null;
		return sound.IsValid() ? sound : _eventSounds[name] = Sounds.Refresh( sound );
	}

	internal bool PlaysSound( string clip, SoundEvent sound ) =>
		_sequences?.Get( clip )?.Events.Any( evt => EventSound( evt ) == sound ) == true;

	void DispatchEvents()
	{
		if ( _sequences?.Get( Current ) is not { } sequence || Duration <= 0 ) return;
		var time = ClipTime;
		var end = sequence.Looping ? time : MathF.Min( time, Duration );
		var firstLoop = sequence.Looping ? Math.Max( 0, (int)(_eventTime / Duration) ) : 0;
		var lastLoop = sequence.Looping ? (int)(end / Duration) : 0;
		// A stalled tab must not release minutes of accumulated footsteps at once.
		firstLoop = Math.Max( firstLoop, lastLoop - 1 );
		for ( var loop = firstLoop; loop <= lastLoop; loop++ )
			foreach ( var evt in sequence.Events )
			{
				var eventTime = (loop + evt.Cycle) * Duration;
				if ( eventTime > _eventTime && eventTime <= end )
				{
					if ( EventSound( evt ) is { } sound )
					{
						var handle = _renderer.GameObject.PlaySound( sound );
						if ( _stoppableSounds?.TryGetValue( evt.Options, out var previous ) == true )
						{
							previous?.Stop();
							_stoppableSounds[evt.Options] = handle;
						}
					}
					else if ( evt.Name == "AE_CL_STOPSOUND" && evt.Options is { } name )
						_stoppableSounds?.GetValueOrDefault( name )?.Stop();
					else
						_npcMount?.PlayNpcEvent( _npc, sequence, evt );
					EventFired?.Invoke( evt );
				}
			}
		_eventTime = end;
	}

	public void Dispose()
	{
		if ( _feet.Length > 0 && _renderer.IsValid() ) _renderer.LocalPosition = _renderOrigin;
		_feet = [];
		if ( _renderer.IsValid() && _renderer.Animation.Graph == _graph ) _renderer.Animation.Graph = null;
		if ( _stoppableSounds is not null )
		{
			foreach ( var handle in _stoppableSounds.Values ) handle?.Stop();
			_stoppableSounds = null;
		}
		if ( !_sharedGraph ) _graph?.Dispose();
		_graph = null;
		_model = null;
		Current = null;
	}
}
