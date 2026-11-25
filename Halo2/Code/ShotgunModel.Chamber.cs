using Sandbox.Animation;

namespace Halo2;

[Sandbox.ModelEditor.GameData( "weapon_chamber" )]
public sealed class WeaponChamber
{
	public string IdlePath { get; set; }
	public string ChamberPath { get; set; }
	public int Frames { get; set; }

	internal ChamberAnimation Load()
	{
		var idle = Clip.Load( IdlePath );
		var chamber = Clip.Load( ChamberPath );
		return idle.IsValid() && chamber.IsValid() ? new( idle, chamber, Frames ) : null;
	}
}

internal sealed record ChamberAnimation( Clip Idle, Clip Clip, int Frames )
{
	readonly Dictionary<string, (int Index, Transform IdlePose)> tracks = Clip.Skeleton.Bones
		.ToDictionary( b => b.Name, b => (b.Index, Idle.GetLocalTransform( 0, b.Index )) );

	public Transform SampleMarker( ModelAttachments.Attachment marker, float cycle, float weight )
	{
		var pose = marker.LocalTransform;
		for ( var bone = marker.Bone; bone is not null; bone = bone.Parent )
		{
			var (index, rest) = tracks[bone.Name];
			var pump = Clip.GetLocalTransform( cycle, index );
			pose = Transform.Lerp( rest, pump, weight, false ).ToWorld( pose );
		}
		return pose;
	}
}

public partial class ShotgunModel
{
	internal ChamberAnimation Chamber { get; set; }
	double? pumpStart;
	double pumpEnd;
	static readonly float PumpBlendTime = new GraphBuilder.TransitionSettings().Duration;

	internal void StartChamber( double start, double end )
	{
		pumpStart = start;
		pumpEnd = end;
	}

	bool ChamberSample( out float cycle, out float weight )
	{
		cycle = weight = 0;
		if ( pumpStart is not {} start || Chamber is null ) return false;
		double now = Time.NowDouble;
		float elapsed = (float)(now - start);
		// A looping mounted clip can contain a closing sample. The pump is a one-shot.
		cycle = Chamber.Clip.FrameCount < 2 ? 0 : (elapsed / Chamber.Clip.Duration).Clamp( 0, (Chamber.Frames - 1f) / (Chamber.Clip.FrameCount - 1) );
		weight = (System.Math.Min( elapsed, (float)(pumpEnd + PumpBlendTime - now) ) / PumpBlendTime).Clamp( 0, 1 );
		return now < pumpEnd + PumpBlendTime;
	}

	internal void UpdateChamberPose()
	{
		if ( !pumpStart.HasValue || !Renderer.IsValid() || !Renderer.Animation.IsValid ) return;
		bool playing = ChamberSample( out float cycle, out float weight );
		Renderer.Animation.Parameters.TrySet( "chamber_cycle", playing ? cycle : -1f );
		Renderer.Animation.Parameters.TrySet( "chamber_weight", weight );
		if ( !playing ) pumpStart = null;
	}

	internal Transform? ChamberMarker( string name )
	{
		if ( !ChamberSample( out float cycle, out float weight ) || Renderer.Model.Attachments.Get( name ) is not {} marker ) return null;
		return Renderer.WorldTransform.ToWorld( Chamber.SampleMarker( marker, cycle, weight ) );
	}
}
