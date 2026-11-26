using System;
using System.Runtime.CompilerServices;

namespace Halo2;

internal sealed class BlamRandom
{
	static readonly ConditionalWeakTable<Scene, BlamRandom> scenes = new();
	uint gameplay = (uint)Random.Shared.Next();
	uint presentation = (uint)Random.Shared.Next();

	// Firing, melee selection and inventory drops advance the same simulation sequence.
	static BlamRandom For( Scene scene ) => scenes.GetValue( scene, static _ => new BlamRandom() );
	internal static ref uint GameplayState( Scene scene ) => ref For( scene ).gameplay;
	internal static ref uint PresentationState( Scene scene ) => ref For( scene ).presentation;

	internal static uint Next( ref uint state )
	{
		state = unchecked( state * 1664525u + 1013904223u );
		return state >> 16;
	}
}

internal static class Spread
{
	static readonly Vector3[] directions = BuildDirections();

	internal static Vector3 RandomDirection( ref uint state ) => directions[(BlamRandom.Next( ref state ) * (uint)directions.Length) >> 16];

	internal static Vector3 Deviate( Vector3 forward, float minimum, float maximum, ref uint state )
	{
		var axis = RandomDirection( ref state );
		var normal = Vector3.Cross( forward, axis );
		float length = normal.Length;
		if ( length < 0.0001f ) return forward;
		normal /= length;

		double fraction = BlamRandom.Next( ref state ) * (double)(1f / 65535f);
		double angle = minimum + fraction * ((double)maximum - minimum);
		float sine = (float)Math.Sin( angle ), cosine = (float)Math.Cos( angle );
		return forward * cosine + normal * (Vector3.Dot( forward, normal ) * (1 - cosine))
			- Vector3.Cross( forward, normal ) * sine;
	}

	// The retail table enumerates a 16-division octahedron, with shared edges stored once.
	static Vector3[] BuildDirections()
	{
		var top = Vector3.Up;
		var x = Vector3.Forward;
		var y = Vector3.Left;
		var result = new List<Vector3>( 1026 ) { top, y, x, -y, -x, -top };

		Edge( top, x );
		Edge( top, y );
		Face( top, x, y );
		Edge( y, x );
		Edge( top, -y );
		Face( top, -y, x );
		Edge( x, -y );
		Edge( top, -x );
		Face( top, -x, -y );
		Edge( -y, -x );
		Face( top, y, -x );
		Edge( y, -x );

		Edge( -x, -top );
		Edge( y, -top );
		Face( -top, -x, y );
		Edge( -y, -top );
		Face( -top, -y, -x );
		Edge( x, -top );
		Face( -top, x, -y );
		Face( -top, y, x );
		return result.ToArray();

		void Edge( Vector3 a, Vector3 b )
		{
			for ( int i = 1; i < 16; i++ )
				result.Add( (a * (16 - i) + b * i).Normal );
		}

		void Face( Vector3 pole, Vector3 a, Vector3 b )
		{
			for ( int row = 2; row < 16; row++ )
			for ( int i = 1; i < row; i++ )
				result.Add( (pole * (16 - row) + a * (row - i) + b * i).Normal );
		}
	}
}
