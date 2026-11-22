using System;
using Sandbox.Animation;

namespace GoldSrc;

internal sealed record StudioSequence( string Name, Mdl.SequenceDesc Description, Mdl.Event[] Events, Transform[] Frames, Transform[] PitchFrames )
{
	internal float Duration => MathF.Max( (Description.NumFrames - 1) / Description.Fps, 0.1f );
	internal float GroundSpeed => Description.LinearMovement.Length / Duration;
	internal string TimeParameter { get; } = $"time_{Name}";
}

internal sealed record StudioController( string Bone, int Type, float Start, float End )
{
	internal float Fraction( float value ) => End == Start ? 0 : ((value - Start) / (End - Start)).Clamp( 0, 1 );
	internal Vector3 TranslationAxis => (Type & 7) switch { 1 => Vector3.Forward, 2 => Vector3.Left, 4 => Vector3.Up, _ => Vector3.Zero };
	internal Vector3 RotationAxis => (Type & 0x38) switch { 8 => Vector3.Forward, 16 => Vector3.Left, 32 => Vector3.Up, _ => Vector3.Zero };
}

/// <summary>Shares procedural clips and graphs between NPCs using the same mounted model.</summary>
internal sealed class StudioAnimationGraphs( Skeleton skeleton, string modelPath )
{
	readonly Dictionary<string, Clip> _clips = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, Graph> _graphs = new( StringComparer.OrdinalIgnoreCase );

	internal const string StateParameter = "state";

	internal const string ExtensionParameter = "extension";
	internal const string HeadParameter = "head_turn";
	internal const string MouthParameter = "mouth_open";

	internal Skeleton Skeleton => skeleton;
	internal Clip GetClip( StudioSequence sequence ) => GetClip( sequence.Name, sequence.Frames, sequence.Description.Fps );

	internal Graph GetGraph( params StudioSequence[] sequences ) => GetGraph( sequences, null, [] );

	internal Graph GetGraph( StudioSequence[] sequences, StudioController[] controllers, string[] parameters )
	{
		var available = sequences.Where( x => x is not null ).DistinctBy( x => x.Name, StringComparer.OrdinalIgnoreCase ).ToArray();
		var key = (string.Join( ",", parameters ) + ":") + string.Join( "|", available.Select( x => x.Name ) );
		if ( _graphs.TryGetValue( key, out var graph ) )
			return graph;

		var builder = new GraphBuilder().WithSkeleton( skeleton );
		var state = builder.IdParameter( StateParameter, available.Select( x => x.Name ).ToArray() );
		var times = available.Select( x => builder.FloatParameter( x.TimeParameter, 0, 1 ) ).ToArray();
		var pitch = available.Any( x => x.PitchFrames is not null ) ? builder.FloatParameter( "aim_pitch", 0, 1 ) : null;
		var controlTimes = parameters.Select( (name, slot) => name is not null && controllers?.ElementAtOrDefault( slot ) is not null
			? builder.FloatParameter( name, 0, 1 ) : null ).ToArray();
		var machine = builder.StateMachine();
		var states = available.Select( (sequence, index) =>
		{
			var pose = builder.TimeControlledClip( GetClip( sequence ), times[index], new() { SampleRootMotion = false } );
			if ( sequence.PitchFrames is not null )
			{
				var high = GetClip( $"{sequence.Name}_blend1", sequence.PitchFrames, sequence.Description.Fps );
				var upperPose = builder.TimeControlledClip( high, times[index], new() { SampleRootMotion = false } );
				pose = builder.Blend( pose, upperPose, pitch );
			}
			return machine.AddState( pose );
		} ).ToArray();

		for ( var target = 0; target < states.Length; target++ )
		{
			var condition = builder.IdCompare( state, available[target].Name );
			for ( var source = 0; source < states.Length; source++ )
			{
				if ( source != target )
					machine.AddTransition( states[source], states[target], condition, new GraphBuilder.TransitionSettings { Duration = 0.2f } );
			}
		}

		var output = machine.Build();
		if ( parameters.Length > 0 )
		{
			var layers = builder.Layers( output );
			for ( int slot = 0; slot < parameters.Length; slot++ )
			{
				if ( parameters[slot] is not { } name || controllers?.ElementAtOrDefault( slot ) is not { } controller ||
					(controller.RotationAxis == Vector3.Zero && controller.TranslationAxis == Vector3.Zero) ||
					skeleton.Bones.FirstOrDefault( x => x.Name == controller.Bone ) is not { } bone ) continue;

				var layer = builder.TimeControlledClip( GetControllerClip( name, controller, bone.Index ), controlTimes[slot], new() { SampleRootMotion = false } );
				layers.AddLayer( layer, new() { BlendMode = GraphBuilder.LayerBlendMode.Additive } );
			}
			output = layers;
		}
		graph = builder.Create( output, $"{modelPath}/npc_{_graphs.Count}.ag" );
		_graphs.Add( key, graph );
		return graph;
	}

	Clip GetControllerClip( string name, StudioController controller, int bone )
	{
		if ( _clips.TryGetValue( name, out var cached ) ) return cached;
		var clip = new ClipBuilder { Skeleton = skeleton, IsAdditive = true };
		var frame = Enumerable.Repeat( Transform.Zero.WithScale( 0f ), skeleton.BoneCount ).ToArray();
		var axis = controller.RotationAxis;
		// GoldSrc adds jaw Z rotation in parent space. Additive clips apply it in bone space.
		if ( name == MouthParameter && axis == Vector3.Up )
			axis = skeleton.Bones[bone].LocalTransform.Rotation.Inverse * axis;

		foreach ( var value in new[] { controller.Start, (controller.Start + controller.End) * 0.5f, controller.End } )
		{
			frame[bone] = controller.TranslationAxis != Vector3.Zero
				? frame[bone].WithPosition( controller.TranslationAxis * value )
				: frame[bone].WithRotation( Rotation.FromAxis( axis, value ) );
			clip.AddFrame( frame );
		}
		return _clips[name] = clip.Create( $"{modelPath}/{name}.anim" );
	}

	Clip GetClip( string name, Transform[] samples, float fps )
	{
		if ( _clips.TryGetValue( name, out var clip ) )
			return clip;

		var builder = new ClipBuilder { Skeleton = skeleton, FrameRate = fps };
		var frames = samples.AsSpan();
		for ( var offset = 0; offset < frames.Length; offset += skeleton.BoneCount )
			builder.AddFrame( frames.Slice( offset, skeleton.BoneCount ) );

		// A one-frame studio sequence is a static pose. Give it two identical samples.
		if ( builder.FrameCount == 1 )
			builder.AddFrame( frames );
		clip = builder.Create( $"{modelPath}/{name}.anim" );
		_clips.Add( name, clip );
		return clip;
	}
}
