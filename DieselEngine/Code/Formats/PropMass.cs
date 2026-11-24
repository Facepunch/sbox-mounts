namespace Diesel;

/// <summary>Mass for a single rigid browser prop, not articulated body reconstruction.</summary>
static class PropMass
{
	public static float Calculate( IReadOnlyDictionary<ulong, Vector3[]> shapes, IReadOnlyDictionary<ulong, float> authored )
	{
		if ( shapes.Count == 0 ) return 1000f; // Same as ModelBuilder's no-shape default.
		// Keep the previous estimate for wholly unspecified props. For partially authored
		// props, estimate only the remaining shapes instead of overriding known masses.
		if ( !shapes.Keys.Any( authored.ContainsKey ) )
			return Estimate( shapes.Values.SelectMany( p => p ) );
		double total = 0;
		foreach ( var (name, points) in shapes )
			total += authored.TryGetValue( name, out float mass ) ? mass : Estimate( points );
		if ( total > float.MaxValue ) throw new InvalidDataException( "Diesel prop mass exceeds the supported range." );
		return (float)total;
	}

	static float Estimate( IEnumerable<Vector3> points )
	{
		var size = BBox.FromPoints( points ).Size * 0.0254f;
		return Math.Clamp( size.x * size.y * size.z * 500f, 0.1f, 50000f );
	}
}
