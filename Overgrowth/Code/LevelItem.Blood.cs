using System;

namespace Overgrowth;

public sealed partial class LevelItem
{
	[RequireComponent] CutMarks BloodMarks { get; set; }

	internal void MarkStab( Vector3 attacker )
	{
		UpdateAttachment();
		var segments = GetLines();
		if ( segments.Length == 0 ) return;
		attacker = WorldTransform.PointToLocal( attacker );
		var tip = segments[0].Start;
		foreach ( var line in segments )
		{
			if ( line.Start.DistanceSquared( attacker ) > tip.DistanceSquared( attacker ) ) tip = line.Start;
			if ( line.End.DistanceSquared( attacker ) > tip.DistanceSquared( attacker ) ) tip = line.End;
		}
		var axis = (segments[^1].End - segments[^1].Start).Normal;
		var side = axis.Cross( BloodScatter() ).Normal;
		MarkBlood( tip, (side + axis * 2f).Normal, 20f );
	}

	internal void MarkCut( Vector3 center, Vector3 head, Vector3 direction )
	{
		UpdateAttachment();
		center = WorldTransform.PointToLocal( center );
		head = WorldTransform.PointToLocal( head );
		var segments = GetLines();
		float nearest = float.PositiveInfinity;
		Vector3 point = default, axis = default;
		foreach ( var line in segments )
		{
			if ( line.Material != "metal" ) continue;
			axis = (line.End - line.Start).Normal;
			var (contact, _) = ItemLine.Closest( (line.Start, line.End), (center, head) );
			float distance = contact.DistanceSquared( head );
			if ( distance >= nearest ) continue;
			nearest = distance;
			point = contact;
		}
		if ( float.IsPositiveInfinity( nearest ) ) return;
		if ( Definition.Element( "label" )?.Value == "knife" ) point = segments[^1].End;
		direction = WorldRotation.Inverse * direction;
		MarkBlood( point, (direction - axis * direction.Dot( axis )).Normal, 20f );
	}

	internal static Vector3 BloodScatter() => new( Random.Shared.Float( -1f, 1f ), Random.Shared.Float( -1f, 1f ), Random.Shared.Float( -1f, 1f ) );

	void MarkBlood( Vector3 point, Vector3 axis, float size )
	{
		if ( RabbitGuard.BloodLevel == 0 || axis.IsNearlyZero() ) return;
		var across = axis.Cross( BloodScatter() ).Normal;
		BloodMarks.DrawItemSpatter( point, across / size, across.Cross( axis ).Normal / size );
	}
}
