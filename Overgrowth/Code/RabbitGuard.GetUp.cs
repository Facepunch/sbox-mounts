using System;
using Sandbox.Animation;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	string getUpSlot;
	Transform[] lastPhysicsPose;
	Graph getUpGraph;
	Clip getUpPose;
	internal const string FallRecovery = "fall_recovery";
	float fallBlendAge, fallPoseWeight;

	void CapturePhysicsPose()
	{
		var rig = boundAnimations.Rig;
		lastPhysicsPose ??= new Transform[rig.BoneCount];
		foreach ( var bone in rig.Bones )
		{
			if ( Renderer.TryGetBoneTransform( bone.Name, out var world ) )
				lastPhysicsPose[bone.Index] = world;
			else
				lastPhysicsPose[bone.Index] = Renderer.WorldTransform.ToWorld( bone.Transform );
		}
	}

	Transform[] TakeLocalPhysicsPose()
	{
		var rig = boundAnimations.Rig;
		var frame = new Transform[rig.BoneCount];
		foreach ( var bone in rig.Bones )
		{
			var parent = bone.ParentIndex < 0 ? Renderer.WorldTransform : lastPhysicsPose[bone.ParentIndex];
			frame[bone.Index] = parent.ToLocal( lastPhysicsPose[bone.Index] );
		}

		lastPhysicsPose = null;
		return frame;
	}

	void BeginFallBlend()
	{
		getUpSlot = FallRecovery;
		fallBlendAge = 0f;
		fallPoseWeight = 1f;
		var clip = new ClipBuilder { Skeleton = boundAnimations.Rig };
		clip.AddFrame( TakeLocalPhysicsPose() );
		ConnectRecoveryPose( clip );
		Renderer.Animation.Parameters.Set( "fall_pose_weight", fallPoseWeight );
	}

	void UpdateFallBlend()
	{
		fallBlendAge += Time.Delta;
		// Each source animation update blends the previous result toward the live pose.
		const float fade = 4f;
		fallPoseWeight *= MathF.Pow( Math.Max( 0f, 1f - fallBlendAge * fade ), Time.Delta * 30f );
		Renderer.Animation.Parameters.Set( "fall_pose_weight", fallPoseWeight );
		if ( fallBlendAge >= 1f / fade ) EndGetUpBlend();
	}

	void BeginGetUpBlend()
	{
		var rig = boundAnimations.Rig;
		var frame = TakeLocalPhysicsPose();
		getUpSlot = Stance;
		bool rolling = getUpSlot == Roll;
		var stand = rolling ? RollClip( 0 ) : boundAnimations.Clips[getUpSlot].ForSide( MirroredAction );
		var other = rolling ? RollClip( 1 ) : stand;
		float frequency = rolling ? RollFrequency : 1f / stand.Duration;
		float fadeRate = rolling ? 2f : 1f;
		var duration = Math.Min( 1f / fadeRate, 1f / frequency );
		int steps = (int)MathF.Ceiling( duration * 120f );
		var clip = new ClipBuilder { Skeleton = rig, FrameRate = steps / duration };
		clip.AddFrame( frame );
		// aschar.as blends from the previous result towards the moving stand pose in parent space.
		for ( int step = 1; step <= steps; step++ )
		{
			float time = step * duration / steps;
			for ( int i = 0; i < frame.Length; i++ )
			{
				float phase = time * frequency;
				var target = stand.Animation.GetLocalTransform( phase, i );
				if ( rolling ) target = global::Transform.Lerp( target, other.Animation.GetLocalTransform( phase, i ), RollForwardness, false );
				frame[i] = global::Transform.Lerp( frame[i], target, time * fadeRate, false );
			}
			clip.AddFrame( frame );
		}
		foreach ( var channel in RabbitClip.StatusChannels )
			AddTrack( "status", channel );
		foreach ( var morph in boundAnimations.Morphs )
			AddTrack( morph.Steps == 1 ? "MorphChannel" : "multistep", morph.Name );

		void AddTrack( string set, string channel )
		{
			var weights = new float[steps + 1];
			for ( int step = 1; step <= steps; step++ )
			{
				float time = step * duration / steps;
				stand.Animation.TryGetFloatChannel( time * frequency, set, channel, out var value );
				if ( rolling )
				{
					other.Animation.TryGetFloatChannel( time * frequency, set, channel, out var forward );
					value += (forward - value) * RollForwardness;
				}
				weights[step] = time * fadeRate * value;
			}
			if ( weights.Any( w => w != 0f ) ) clip.AddFloatChannelTrack( set, channel, weights );
		}
		ConnectRecoveryPose( clip );
	}

	void ConnectRecoveryPose( ClipBuilder clip )
	{
		getUpPose = clip.Create();
		var graph = new GraphBuilder().WithSkeleton( boundAnimations.Rig );
		getUpGraph = graph.Create( graph.Clip( getUpPose, new() { Looping = false, SampleRootMotion = false } ) );
		Renderer.Animation.ExternalGraphs.Connect( getUpSlot, getUpGraph );
	}

	void EndGetUpBlend()
	{
		lastPhysicsPose = null;
		if ( getUpGraph is null ) return;
		if ( Renderer.IsValid() )
		{
			if ( getUpSlot == FallRecovery ) Renderer.Animation.Parameters.Set( "fall_pose_weight", 0f );
			if ( Renderer.Animation.ExternalGraphs.IsConnected( getUpSlot ) )
				Renderer.Animation.ExternalGraphs.Disconnect( getUpSlot );
		}
		getUpGraph.Dispose();
		getUpPose.Dispose();
		getUpGraph = null;
		getUpPose = null;
	}

	// ModelPhysics drives only bones with bodies. Every other bone must inherit its parent again.
	void ReleaseProceduralPose()
	{
		if ( Renderer.IsValid() ) Renderer.ClearPhysicsBones();
		armsActive = earsActive = plantedLegs = chestActive = false;
		ResetBodyLean();
	}

	protected override void OnDisabled()
	{
		ReleaseProceduralPose();
		EndGetUpBlend();
		FollowLeader( null );
		base.OnDisabled();
	}
}
