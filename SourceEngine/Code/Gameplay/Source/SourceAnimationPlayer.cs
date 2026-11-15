using System;
using Sandbox.Animation;

namespace Source1;

/// <summary>Builds one AG2 state machine from a model's imported clips and an entity's clip choices.</summary>
public sealed class SourceAnimationPlayer : IDisposable
{
	Graph _graph;
	Model _model;
	Model _unplayable;
	SkinnedModelRenderer _renderer;
	TimeSince _elapsed;
	int _slot;
	string _selection;
	readonly float[] _rates = [1, 1];
	SourceModelSequences _sequences;
	float _eventTime;
	float _rate = 1;
	readonly Dictionary<string, Parameter> _poses = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, Parameter> _layerWeights = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, SourceModelSequenceData.BlendAxis> _axes = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, SoundEvent> _eventSounds = new();
	string _gesture;
	int _gestureSlot;
	TimeUntil _gestureEnd;
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
		public float Duration;
		public float FadeIn = 0.2f;
		public float FadeOut = 0.2f;
		public float Weight;
		public TimeSince Started;
		public string IdKey;
		public string WeightKey;
		public string[] RateKeys;
	}

	/// <summary>
	/// Play sequences that carry a blend grid (movement direction, aim) as a blend over their grid cells,
	/// driven by the pose parameters their axes name. Off, a sequence plays its neutral cell.
	/// </summary>
	public bool UseBlendGrids { get; set; }

	/// <summary>Additive delta sequences with a grid, such as an aim matrix, layered over everything; see <see cref="SetLayerWeight"/>.</summary>
	public IReadOnlyList<string> AdditiveLayers { get; set; } = [];

	/// <summary>Additive one-shot delta sequences, such as flinch gestures; see <see cref="PlayGesture"/>.</summary>
	public IReadOnlyList<string> Gestures { get; set; } = [];

	/// <summary>
	/// Named overlay layers and the full (non-delta) clips each may play, such as an upper-body reload over locomotion.
	/// A clip blends in through its sequence's bone mask (Source weightlist), or over the whole body without one; see <see cref="PlayOverlay"/>.
	/// </summary>
	public IReadOnlyDictionary<string, IReadOnlyList<string>> OverlayLayers { get; set; } = new Dictionary<string, IReadOnlyList<string>>();

	/// <summary>
	/// Bones pulled onto other bones of the final pose by two-bone IK, such as hands onto a weapon's grip bones,
	/// weighted by <see cref="SetLayerWeight"/> with <see cref="BoneLockLayer"/>. Pairs with a bone the skeleton lacks are skipped.
	/// </summary>
	public IReadOnlyList<(string Effector, string Target)> BoneLocks { get; set; } = [];

	public const string BoneLockLayer = "bone_locks";

	public void SetPoseParameter( string name, float value ) => Set( _poses, "pose:", name, value );
	public void SetLayerWeight( string layer, float weight ) => Set( _layerWeights, "layer:", layer, weight );

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
	public bool Finished => _elapsed >= Duration / MathF.Max( _rate, 0.01f );

	/// <summary>
	/// Builds the graph once per model, starting directly in <paramref name="initial"/> so the first clip does not blend in from another.
	/// A model with none of the choices playable is not tried again.
	/// </summary>
	public bool Ensure( SkinnedModelRenderer renderer, IEnumerable<(string Name, bool Loop)> choices, string initial = null )
	{
		if ( !renderer.IsValid() || !renderer.Model.IsValid() || !renderer.Model.AnimationSkeleton().IsValid() ) return false;
		if ( _model == renderer.Model && _graph.IsValid() ) return true;
		if ( _unplayable == renderer.Model ) return false;
		Dispose();
		_model = renderer.Model;
		_renderer = renderer;
		_sequences = SourceModelSequences.Of( _model );
		_eventSounds.Clear();
		_axes.Clear();
		var available = choices.Where( x => !string.IsNullOrWhiteSpace( x.Name ) && HasClip( x.Name ) )
			.DistinctBy( x => x.Name, StringComparer.OrdinalIgnoreCase )
			.OrderBy( x => string.Equals( x.Name, initial, StringComparison.OrdinalIgnoreCase ) ? 0 : 1 ).ToArray();
		if ( available.Length == 0 )
		{
			_unplayable = _model;
			_model = null;
			return false;
		}

		if ( !Application.IsDedicatedServer )
			foreach ( var sound in _sequences.All.SelectMany( s => s.Events ).Where( SourceSounds.IsSoundEvent ).Select( e => e.Options ).Distinct() )
				if ( !string.IsNullOrWhiteSpace( sound ) ) _eventSounds[sound] = SourceSounds.Sound( sound, SourceMount.Of( _model ) );

		var builder = Graph.Builder;
		builder.Skeleton = _model.AnimationSkeleton().Name;
		// Two playback slots let a repeated shot crossfade from the current pose.
		// ResetTime on a shared parameter also rewinds the outgoing clip, causing
		// a visible snap on shots, shell inserts and interrupted reloads.
		var clips = available.SelectMany( x => Enumerable.Range( 0, 2 ).Select( slot => (x.Name, x.Loop, Slot: slot, Key: $"{x.Name}:{slot}") ) ).ToArray();
		var selected = builder.IdParameter( "sequence", clips.Select( x => x.Key ).ToArray() );
		var speeds = Enumerable.Range( 0, 2 ).Select( slot => builder.AddControlParameter( $"rate{slot}", GraphBuilder.NodeKind.Float,
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
		var additive = AdditiveLayers.Select( _sequences.Get ).Where( s => s is { Delta: true } && HasGrid( s ) ).ToArray();
		foreach ( var axis in gridded.Concat( additive ).SelectMany( s => s.BlendAxes ) )
			Parameter( axis );
		var layerWeights = additive.ToDictionary( s => s.Name, s => builder.AddControlParameter( $"layer:{s.Name}", GraphBuilder.NodeKind.Float, new() { DefaultFloat = 0, Min = 0, Max = 1 } ) );
		_gestures = Gestures.Where( x => _sequences.Get( x ) is { Delta: true } && HasClip( x ) ).Distinct().ToArray();
		_gesture = null;
		var gestureKeys = SlotKeys( _gestures );
		var gestureId = _gestures.Length > 0 ? builder.IdParameter( "gesture", gestureKeys ) : null;
		var gestureWeight = _gestures.Length > 0 ? builder.AddControlParameter( "gesture_weight", GraphBuilder.NodeKind.Float, new() { DefaultFloat = 0, Min = 0, Max = 1 } ) : null;
		_overlays.Clear();
		var overlayParameters = new List<(string[] Keys, GraphBuilder.Node Id, GraphBuilder.Node Weight, GraphBuilder.Node[] Rates, Dictionary<string, SourceModelSequenceData[]> Companions)>();
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
				companions) );
		}

		var locks = BoneLocks.Where( x => _model.Bones.GetBone( x.Effector ) is not null && _model.Bones.GetBone( x.Target ) is not null ).ToArray();
		var lockWeight = locks.Length > 0 ? builder.AddControlParameter( $"layer:{BoneLockLayer}", GraphBuilder.NodeKind.Float, new() { DefaultFloat = 0, Min = 0, Max = 1 } ) : null;

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
			var animation = _model.AnimationClips()[clip];
			return animation.FrameCount <= 1
				? builder.Pose( animation, new GraphBuilder.PoseSettings() )
				: builder.Clip( animation, new() { Looping = loop, SampleRootMotion = false } );
		}

		// Bilinear over the grid like bone_setup's CalcPoseSingle: blend along the first axis per row, then across rows.
		GraphBuilder.Node Grid( SourceModelSequenceData sequence )
		{
			var x = sequence.BlendAxes[0];
			GraphBuilder.Node Row( int row )
			{
				var blend = builder.BlendSpace( Parameter( x ), looping: sequence.Looping );
				for ( var i = 0; i < x.Positions.Length; i++ )
					blend.Add( x.Positions[i], Cell( sequence.BlendGrid[i + row * x.Positions.Length], sequence.Looping ) );
				return blend;
			}
			if ( sequence.BlendAxes.Length == 1 )
				return Row( 0 );
			var y = sequence.BlendAxes[1];
			var rows = builder.BlendSpace( Parameter( y ), looping: sequence.Looping );
			for ( var j = 0; j < y.Positions.Length; j++ )
				rows.Add( y.Positions[j], Row( j ) );
			return rows;
		}

		GraphBuilder.Node Playback( string name, bool loop )
		{
			if ( staticBlends.TryGetValue( name, out var sequence ) )
			{
				var blend = builder.BlendSpace( poseParameters[sequence.BlendParameter], looping: false );
				for ( var i = 0; i < sequence.BlendClips.Length; i++ )
					blend.Add( sequence.BlendPositions[i], builder.Pose( _model.AnimationClips()[sequence.BlendClips[i]], new GraphBuilder.PoseSettings() ) );
				return blend;
			}
			var metadata = _sequences.Get( name );
			if ( UseBlendGrids && metadata is { Delta: false } && HasGrid( metadata ) )
				return Grid( metadata );
			return builder.Clip( _model.AnimationClips()[name], new() { Looping = metadata?.Looping ?? loop, SampleRootMotion = false } );
		}

		// One state per key, switched by an id parameter, with a transition from every state to every other.
		GraphBuilder.StateMachineBuilder Selector( GraphBuilder.Node id, IReadOnlyList<string> keys, Func<int, GraphBuilder.Node> state, Func<int, int, float> duration )
		{
			var machine = builder.StateMachine();
			var states = keys.Select( ( _, i ) => machine.AddState( state( i ) ) ).ToArray();
			for ( var to = 0; to < states.Length; to++ )
			{
				var condition = builder.IdCompare( id, keys[to] );
				for ( var from = 0; from < states.Length; from++ )
					if ( from != to )
						machine.AddTransition( states[from], states[to], condition,
							new GraphBuilder.TransitionSettings { Duration = duration( from, to ), ClampDuration = false, CanBeForced = true } );
			}
			return machine;
		}

		var main = Selector( selected, clips.Select( x => x.Key ).ToArray(), i => builder.SpeedScale( Playback( clips[i].Name, clips[i].Loop ), speeds[clips[i].Slot] ), ( from, to ) =>
			// A completed reload still needs its authored fade into idle; clamping to the outgoing clip's remaining time makes that fade zero.
			_sequences.Get( clips[from].Name ) is { } outgoing && _sequences.Get( clips[to].Name ) is { } incoming
				? incoming.Snap ? 0 : MathF.Min( outgoing.FadeOut, incoming.FadeIn )
				: 0.08f );
		var layers = builder.Layers( main.Build() );
		foreach ( var overlay in overlayParameters )
		{
			// A two-slot machine per overlay layer, masked per clip by its sequence's weightlist; looping in the graph either way,
			// as a one-shot fades out before its end and a caller may loop a non-looping clip.
			// The inactive state must share the clips' parent space; ZeroPose is additive.
			var machine = Selector( overlay.Id, overlay.Keys, i => i == 0 ? builder.ReferencePose()
				: builder.SpeedScale( builder.Clip( _model.AnimationClips()[ClipOf( overlay.Keys[i] )], new() { Looping = true, SampleRootMotion = false } ), overlay.Rates[SlotOf( overlay.Keys[i] )] ),
				( _, _ ) => 0.15f );
			layers.AddLayer( machine, new() { BlendMode = GraphBuilder.LayerBlendMode.Overlay, IgnoreEvents = true, Weight = overlay.Weight, BoneMask = OverlayMask( builder, overlay.Keys, overlay.Id ) } );

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
				layers.AddLayer( companions, new() { BlendMode = GraphBuilder.LayerBlendMode.Additive, IgnoreEvents = true, Weight = overlay.Weight } );
			}
		}
		foreach ( var pose in poseLayers )
		{
			var blend = builder.BlendSpace( poseParameters[pose.Parameter], looping: false );
			for ( var i = 0; i < pose.Clips.Length; i++ )
				blend.Add( pose.Positions[i], builder.Pose( _model.AnimationClips()[pose.Clips[i]], new GraphBuilder.PoseSettings() ) );
			layers.AddLayer( blend, new() { BlendMode = GraphBuilder.LayerBlendMode.Additive, IgnoreEvents = true } );
		}
		foreach ( var sequence in additive )
			layers.AddLayer( Grid( sequence ), new() { BlendMode = GraphBuilder.LayerBlendMode.Additive, IgnoreEvents = true, Weight = layerWeights[sequence.Name] } );
		if ( _gestures.Length > 0 )
		{
			// One-shot additive gestures, two slots each so a repeat restarts cleanly.
			var gestures = Selector( gestureId, gestureKeys, i => i == 0 ? builder.ZeroPose()
				: builder.Clip( _model.AnimationClips()[ClipOf( gestureKeys[i] )], new() { Looping = false, SampleRootMotion = false } ), ( _, _ ) => 0.1f );
			layers.AddLayer( gestures, new() { BlendMode = GraphBuilder.LayerBlendMode.Additive, IgnoreEvents = true, Weight = gestureWeight } );
		}
		GraphBuilder.Node root = layers;
		foreach ( var (effector, target) in locks )
			root = builder.TwoBoneIK( root, effector, builder.BoneTarget( target ), new() { WorldSpace = false, Weight = lockWeight, BlendTime = 0 } );
		_graph = builder.Create( root, "HL2 mounted animation" );
		renderer.Animation.Graph = _graph;
		renderer.UseAnimGraph = true;
		// The first state is the graph's default, so the first Play lands on slot 0 without a transition.
		_slot = 1;
		Play( available[0].Name, restart: true );
		return true;
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
	public bool PlayOverlay( string layer, string clip, float rate = 1, bool restart = false, bool? loop = null )
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
			overlay.Looping = loop ?? sequence.Looping;
			overlay.Duration = _model.AnimationClips()[clip].Duration;
			overlay.FadeIn = MathF.Max( sequence.FadeIn, 0.05f );
			overlay.FadeOut = MathF.Max( sequence.FadeOut, 0.05f );
		}

		overlay.Active = true;
		overlay.Rate = Math.Clamp( rate, 0.01f, 4 );
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
			Current = name;
			_elapsed = 0;
			_eventTime = -0.0001f;
			_slot = 1 - _slot;
			_selection = $"{name}:{_slot}";
		}
		_rate = Math.Clamp( rate, 0.01f, 4 );
		_rates[_slot] = _rate;
	}

	public void Tick()
	{
		if ( !IsReady ) return;
		var parameters = _renderer.Animation.Parameters;
		parameters.TrySet( "sequence", _selection );
		parameters.TrySet( "rate0", _rates[0] );
		parameters.TrySet( "rate1", _rates[1] );
		foreach ( var pose in _poses.Values )
			parameters.TrySet( pose.Key, _axes.TryGetValue( pose.Name, out var axis ) ? Wrap( axis, pose.Value ) : pose.Value );
		foreach ( var layer in _layerWeights.Values )
			parameters.TrySet( layer.Key, layer.Value );
		if ( _gestures.Length > 0 )
		{
			var active = _gesture is not null && _gestureEnd > 0;
			parameters.TrySet( "gesture", active ? _gesture : None );
			parameters.TrySet( "gesture_weight", active ? 1f : 0f );
		}
		foreach ( var overlay in _overlays.Values )
		{
			// A one-shot starts fading out so that it is gone as its last frame plays.
			var remaining = overlay.Looping ? float.MaxValue : (overlay.Duration - overlay.Started * overlay.Rate) / overlay.Rate;
			if ( remaining <= 0 )
				overlay.Active = false;
			var target = overlay.Active ? MathF.Min( 1, remaining / overlay.FadeOut ) : 0;
			var fade = target > overlay.Weight ? overlay.FadeIn : overlay.FadeOut;
			overlay.Weight = target > overlay.Weight ? MathF.Min( target, overlay.Weight + Time.Delta / fade ) : MathF.Max( target, overlay.Weight - Time.Delta / fade );
			parameters.TrySet( overlay.IdKey, overlay.Weight > 0 && overlay.Clip is not null ? overlay.Selection : None );
			parameters.TrySet( overlay.WeightKey, overlay.Weight );
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

	void DispatchEvents()
	{
		if ( _sequences?.Get( Current ) is not { } sequence || Duration <= 0 ) return;
		var time = (float)_elapsed * _rate;
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
					if ( SourceSounds.IsSoundEvent( evt ) && evt.Options is { } name && _eventSounds.TryGetValue( name, out var sound ) && sound is not null )
						_renderer.GameObject.PlaySound( sound );
					EventFired?.Invoke( evt );
				}
			}
		_eventTime = end;
	}

	public void Dispose()
	{
		if ( _renderer.IsValid() && _renderer.Animation.Graph == _graph ) _renderer.Animation.Graph = null;
		_graph?.Dispose();
		_graph = null;
		_model = null;
		Current = null;
	}
}
