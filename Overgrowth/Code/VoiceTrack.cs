using System;
using System.Globalization;

namespace Overgrowth;

internal sealed record VoiceTrack( string[] Shapes, VoiceTrack.Frame[] Frames )
{
	internal readonly record struct Frame( float Time, float[] Weights );

	internal static Dictionary<string, int> ReadGroups( string text )
	{
		var ids = new Dictionary<string, int>();
		int depth = 0, group = -1;
		for ( int i = 0; i < text.Length; i++ )
		{
			if ( text[i] == '[' ) { if ( depth++ == 0 ) group++; }
			else if ( text[i] == ']' ) depth--;
			else if ( char.IsLetter( text[i] ) )
			{
				int start = i;
				while ( i + 1 < text.Length && char.IsLetter( text[i + 1] ) ) i++;
				ids[text[start..(i + 1)]] = group;
			}
		}
		return ids;
	}

	internal static VoiceTrack Read( string text, Dictionary<string, int> phonemes, XmlAsset visemes )
	{
		var shapes = visemes.Children.Select( x => x.Value ).Distinct().ToArray();
		var groups = new Dictionary<int, int>();
		foreach ( var v in visemes.Children.OrderBy( x => x.Name, StringComparer.Ordinal ) )
			if ( phonemes.TryGetValue( v.Name, out int id ) ) groups[id] = Array.IndexOf( shapes, v.Value );
		var frames = new List<Frame>();
		foreach ( var line in text.Split( '\n' ) )
		{
			if ( !line.StartsWith( "phn_vis ", StringComparison.Ordinal ) ) continue;
			var fields = line.Split( (char[])null, StringSplitOptions.RemoveEmptyEntries );
			var weights = new float[shapes.Length];
			int count = int.Parse( fields[3], CultureInfo.InvariantCulture );
			for ( int i = 0; i < count; i++ )
			{
				if ( phonemes.TryGetValue( fields[4 + i * 2], out var group ) && groups.TryGetValue( group, out var shape ) )
					weights[shape] += float.Parse( fields[5 + i * 2], CultureInfo.InvariantCulture );
			}
			frames.Add( new( (float.Parse( fields[1], CultureInfo.InvariantCulture ) - 50f) / 970f, weights ) );
		}
		return frames.Count > 0 ? new( shapes, frames.ToArray() ) : null;
	}
}
