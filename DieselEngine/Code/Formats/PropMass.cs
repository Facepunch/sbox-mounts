namespace Diesel;

/// <summary>Mass for a single rigid browser prop, not articulated body reconstruction.</summary>
static class PropMass
{
	public static float Calculate( IReadOnlyDictionary<ulong, Vector3[]> shapes, UnitGeometry geometry,
		IReadOnlyDictionary<string, float> densities )
	{
		if ( shapes.Count == 0 ) return 1000f; // Same as ModelBuilder's no-shape default.
		var authored = geometry.CollisionMasses;
		// Keep the previous estimate for wholly unspecified props. For partially authored
		// props, estimate only the remaining shapes instead of overriding known masses.
		if ( !shapes.Keys.Any( name => authored.ContainsKey( name ) || Density( name ).HasValue || geometry.CollisionShells.ContainsKey( name ) ) )
			return Estimate( shapes.Values.SelectMany( p => p ) );
		double total = 0;
		foreach ( var (name, points) in shapes )
			total += authored.TryGetValue( name, out float mass ) ? mass
				: Estimate( points, Density( name ) ?? 500f, geometry.CollisionShells.GetValueOrDefault( name ) );
		if ( total > float.MaxValue ) throw new InvalidDataException( "Diesel prop mass exceeds the supported range." );
		return (float)total;

		float? Density( ulong name ) => geometry.CollisionMaterials.TryGetValue( name, out var material )
			&& densities.TryGetValue( material, out float density ) ? density : null;
	}

	static float Estimate( IEnumerable<Vector3> points, float density = 500f, float shellCentimeters = 0 )
	{
		var size = BBox.FromPoints( points ).Size * 0.0254f;
		double volume = (double)size.x * size.y * size.z;
		if ( shellCentimeters > 0 )
		{
			// Diesel distances are centimeters. A hollow bounding box is only a mass
			// approximation: it does not alter the imported collision shape.
			double inset = shellCentimeters * 0.02;
			volume -= Math.Max( 0, size.x - inset ) * Math.Max( 0, size.y - inset ) * Math.Max( 0, size.z - inset );
		}
		return (float)Math.Clamp( volume * density, 0.1, 50000 );
	}
}
