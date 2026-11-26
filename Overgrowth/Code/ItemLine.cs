using System;

namespace Overgrowth;

internal sealed record ItemLine( Vector3 Start, Vector3 End, string Material )
{
	internal static ItemLine[] Read( XmlAsset item, Model model )
	{
		var lines = new List<ItemLine>();
		foreach ( var line in item.Element( "lines" )?.Children ?? [] )
		{
			if ( model.Attachments.GetTransform( line.Attribute( "start" ) ) is { } start && model.Attachments.GetTransform( line.Attribute( "end" ) ) is { } end )
				lines.Add( new( start.Position, end.Position, line.Name ) );
		}
		return lines.ToArray();
	}

	internal (Vector3 Start, Vector3 End) ToWorld( Transform pose ) => (pose.PointToWorld( Start ), pose.PointToWorld( End ));

	internal static (Vector3, Vector3) Closest( (Vector3 Start, Vector3 End) a, (Vector3 Start, Vector3 End) b )
	{
		var u = (a.End - a.Start) / 40f;
		var v = (b.End - b.Start) / 40f;
		var offset = (a.Start - b.Start) / 40f;
		var uu = u.Dot( u );
		var vv = v.Dot( v );
		var uv = u.Dot( v );
		var denominator = uu * vv - uv * uv;
		if ( MathF.Abs( denominator ) < 0.000001f ) return (a.Start, b.Start);
		var projection = offset.Dot( v );
		var t = (projection * uv - offset.Dot( u ) * vv) / denominator;
		var s = (projection + uv * t) / vv;
		return (a.Start + (a.End - a.Start) * Math.Clamp( t, 0f, 1f ), b.Start + (b.End - b.Start) * Math.Clamp( s, 0f, 1f ));
	}
}
