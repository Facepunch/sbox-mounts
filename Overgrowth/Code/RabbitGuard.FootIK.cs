using System;

namespace Overgrowth;

internal readonly record struct FootPose( Transform Transform, float Weight )
{
	internal static FootPose Blend( FootPose a, FootPose b, float fraction ) =>
		new( Transform.Lerp( a.Transform, b.Transform, fraction, false ), a.Weight + (b.Weight - a.Weight) * fraction );

	internal static FootPose Sample( FootPose[] frames, float phase )
	{
		float index = phase * (frames.Length - 1);
		int lower = (int)index;
		return Blend( frames[lower], frames[Math.Min( lower + 1, frames.Length - 1 )], index - lower );
	}
}

public sealed partial class RabbitGuard
{
	readonly float?[] groundHeight = new float?[2];
	readonly Rotation[] footRotation = [Rotation.Identity, Rotation.Identity];
	readonly Vector3[] plantedPosition = new Vector3[2];
	int steppingFoot;
	float stepProgress, stancePhase;

	void UpdateStanceFeet( bool entering )
	{
		var clip = boundAnimations.Clips[Threat];
		stancePhase = entering ? 0f : (stancePhase + Time.Delta / clip.Duration) % 1f;
		Renderer.Animation.Parameters.Set( "stride_phase", stancePhase );
		var leftPose = FootPose.Sample( clip.Feet[0], stancePhase );
		var rightPose = FootPose.Sample( clip.Feet[1], stancePhase );
		var left = Renderer.WorldTransform.ToWorld( leftPose.Transform );
		var right = Renderer.WorldTransform.ToWorld( rightPose.Transform );
		if ( entering )
		{
			plantedPosition[0] = left.Position;
			plantedPosition[1] = right.Position;
			steppingFoot = -1;
			stepProgress = 0;
		}

		// aschar.as HandleFootStance leads the swinging foot by half a step while the other stays planted.
		var velocity = Animation.MoveVelocity.WithZ( 0 );
		float speed = velocity.Length / 40f;
		float stepSpeed = Math.Max( 2f, speed * 1.5f + 1f );
		float across = (right.Position - left.Position).Normal.Dot( velocity.Normal );
		Vector3 Goal( int foot ) => (foot == 0 ? left.Position : right.Position)
			+ velocity * ((1f + (foot == 0 ? -across : across)) / 120f + 0.5f / stepSpeed);
		bool NeedsStep( int foot ) => (Goal( foot ) - plantedPosition[foot]).WithZ( 0 ).LengthSquared > 4f * 4f;
		if ( steppingFoot < 0 && (NeedsStep( 0 ) || NeedsStep( 1 )) )
			steppingFoot = across > 0 ? 1 : 0;
		if ( steppingFoot >= 0 )
		{
			stepProgress += Time.Delta * stepSpeed;
			if ( stepProgress >= 1f )
			{
				plantedPosition[steppingFoot] = Goal( steppingFoot );
				var floor = Scene.Trace.Ray( plantedPosition[steppingFoot] + Vector3.Up * 8f, plantedPosition[steppingFoot] + Vector3.Down * 24f )
					.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
				if ( floor.Hit && Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount )
				{
					var gait = speed < 2f ? "crouchwalk" : speed < MathF.Sqrt( 20f ) ? "walk" : "run";
					mount.PlayStep( floor.Surface?.ResourceName, (steppingFoot == 0 ? "left" : "right") + gait + "step", floor.EndPosition );
				}
				steppingFoot = NeedsStep( 1 - steppingFoot ) ? 1 - steppingFoot : -1;
				stepProgress = 0;
			}
		}
		for ( int side = 0; side < 2; side++ )
		{
			var foot = side == 0 ? left : right;
			var target = side == steppingFoot ? Vector3.Lerp( plantedPosition[side], Goal( side ), stepProgress ) : plantedPosition[side];
			float lift = side == steppingFoot ? Math.Min( 4f, MathF.Sin( stepProgress * MathF.PI ) * speed * speed * 0.2f ) : 0f;
			foot.Position = target.WithZ( foot.Position.z + lift );
			ApplyFootTarget( side, side == 0 ? "left_leg" : "right_leg", foot, 1f, plant: true );
		}
	}

	void UpdateFoot( int side, string label, RabbitClip first, RabbitClip second, float phase, float blend )
	{
		var pose = FootPose.Blend( FootPose.Sample( first.Feet[side], phase ), FootPose.Sample( second.Feet[side], phase ), blend );
		ApplyFootTarget( side, label, Renderer.WorldTransform.ToWorld( pose.Transform ), pose.Weight );
	}

	void ApplyFootTarget( int side, string label, Transform target, float poseWeight, bool plant = false )
	{
		float weight = 0;
		if ( poseWeight > 0 )
		{
			// CDoFootIKImpl sweeps a 0.05 m sphere from 0.2 m above to 0.6 m below the animated foot.
			var trace = Scene.Trace.Sphere( 2f, target.Position + Vector3.Up * 8f, target.Position + Vector3.Down * 24f )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
			if ( trace.Hit && !trace.StartedSolid )
			{
				float height = trace.EndPosition.z - 0.4f;
				float inertia = MathF.Pow( 0.8f, Time.Delta * 120f );
				if ( groundHeight[side] is { } old )
					height += (Math.Clamp( old, height - 4f, height + 4f ) - height) * inertia;
				groundHeight[side] = height;
				var tilt = Rotation.FromToRotation( Vector3.Up, trace.Normal );
				footRotation[side] = Rotation.Lerp( tilt, footRotation[side], inertia );
				float lift = target.Position.z - WorldPosition.z - 2f;
				target.Position = target.Position.WithZ( height + lift );
				target.Rotation = Rotation.Lerp( Rotation.Identity, footRotation[side], Math.Clamp( 1f - lift / 10f, 0f, 1f ) ) * target.Rotation;
				weight = poseWeight;
			}
		}
		if ( weight == 0 ) groundHeight[side] = null;
		if ( plant )
		{
			SolveLeg( side, Renderer.WorldTransform.ToLocal( target ) );
			return;
		}
		Renderer.Animation.Parameters.Set( label, target );
		Renderer.Animation.Parameters.Set( label + "_weight", weight );
	}
}
