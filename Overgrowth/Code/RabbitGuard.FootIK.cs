using System;

namespace Overgrowth;

internal readonly record struct FootPose( Transform Transform, float Weight, bool Present )
{
	internal static FootPose Blend( FootPose a, FootPose b, float fraction ) =>
		new( !a.Present ? b.Transform : !b.Present ? a.Transform : Transform.Lerp( a.Transform, b.Transform, fraction, false ),
			a.Weight + (b.Weight - a.Weight) * fraction, a.Present || b.Present );

	internal static FootPose Sample( FootPose[] frames, float phase )
	{
		if ( frames is null ) return default;
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
	[Sync] float CloseAmount { get; set; }
	[Sync] float ThreatAmount { get; set; }
	[Sync] float CrouchAmount { get; set; }
	float threatVelocity;

	void UpdateStanceAmounts()
	{
		if ( !Networking.IsHost ) return;
		CloseAmount = PrimaryItem.IsValid() && enemy.IsValid() && enemy.GetComponent<RabbitGuard>() is not { Unconscious: true }
			? Math.Clamp( (60f - CharacterCenter( GameObject ).Distance( CharacterCenter( enemy ) )) / 28f, 0f, 1f ) : 0f;
		bool movement = Stance is Idle or Ready or Watch or Locomotion or Threat;
		if ( !movement && strike is null ) ThreatAmount = 0;
		float target = (movement || strike is not null) && HasCombatTarget && startled <= 0 && tactic is CombatTactic.Rush or CombatTactic.Wait ? 1f : 0f;
		float acceleration = target == 0 && ThreatAmount < 0 ? 100f : 200f;
		threatVelocity += (target - ThreatAmount) * Time.Delta * acceleration;
		threatVelocity *= MathF.Pow( 0.89f, Time.Delta * 120f );
		ThreatAmount += threatVelocity * Time.Delta;
		CrouchAmount = duckAmount;
	}

	void UpdateStanceFeet( bool entering )
	{
		float threat = Math.Clamp( ThreatAmount, 0, 1 );
		float crouch = Math.Clamp( CrouchAmount, 0, 1 );
		float frequency = boundAnimations.StanceIdle.Evaluate( new( 0f, 1f - crouch, threat, CloseAmount ), stancePhase, false ).Frequency;
		stancePhase = entering ? 0f : (stancePhase + Time.Delta * frequency) % 1f;
		Renderer.Animation.Parameters.Set( "stride_phase", stancePhase );
		Renderer.Animation.Parameters.Set( "threat_coord", threat );
		Renderer.Animation.Parameters.Set( "tall_coord", 1f - crouch );
		if ( !TryGetAnimatedBone( Renderer.Model.Bones.GetBone( boundAnimations.Legs[0][0].Bone ), out var left ) ||
			!TryGetAnimatedBone( Renderer.Model.Bones.GetBone( boundAnimations.Legs[1][0].Bone ), out var right ) ) return;
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
				if ( Networking.IsHost && floor.Hit )
				{
					var gait = speed < 2f ? "crouchwalk" : speed < MathF.Sqrt( 20f ) ? "walk" : "run";
					var step = (steppingFoot == 0 ? "left" : "right") + gait + "step";
					ReportFootstep( step, floor.EndPosition );
					PlayMaterialEvent( step, floor.EndPosition, TerrainLoader.ContactSurface( floor, floor.EndPosition )?.ResourceName, speed < 2f ? 3f : 5f );
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
			PlantFoot( side, foot );
		}
		SolveStance();
	}

	void PlantFoot( int side, Transform foot ) =>
		ApplyFootTarget( side, foot, foot, 1f, foot.Position.z - WorldPosition.z );

	void ApplyFootTarget( int side, Transform animated, Transform target, float weight, float authoredHeight )
	{
		var result = global::Transform.Lerp( animated, target, weight, false );
		bool contact = false;
		if ( weight > 0f )
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
				float lift = authoredHeight - 2f;
				result.Position += Vector3.Up * ((height + lift - target.Position.z) * weight);
				result.Rotation = Rotation.Lerp( Rotation.Identity, footRotation[side], weight * Math.Max( 0f, 1f - lift / 10f ) ) * result.Rotation;
				contact = true;
			}
		}
		if ( !contact ) groundHeight[side] = null;
		stanceFoot[side] = Renderer.WorldTransform.ToLocal( result );
	}
}
