namespace Diesel;

/// <summary>Mass for a single rigid browser prop, not articulated body reconstruction.</summary>
static class PropMass
{
	// Source-space dimensions in meters; determinant handles parent scale/shear while
	// making volume independent of translation and rotation, including reflections.
	public readonly record struct BoxVolume( Vector3 Size, float Scale )
	{
		public double CubicMeters( float shellCentimeters )
		{
			double volume = (double)Size.x * Size.y * Size.z;
			if ( shellCentimeters > 0 )
			{
				double inset = shellCentimeters * 0.02;
				volume -= Math.Max( 0, Size.x - inset ) * Math.Max( 0, Size.y - inset ) * Math.Max( 0, Size.z - inset );
			}
			return volume * Scale;
		}
	}

	public static float Calculate( IReadOnlyDictionary<ulong, Vector3[]> shapes, UnitGeometry geometry,
		IReadOnlyDictionary<string, float> densities, IReadOnlyDictionary<ulong, BoxVolume> boxes = null )
	{
		if ( shapes.Count == 0 ) return 1000f; // Same as ModelBuilder's no-shape default.
		var authored = geometry.CollisionMasses;
		// Keep the previous estimate for wholly unspecified props. For partially authored
		// props, estimate only the remaining shapes instead of overriding known masses.
		if ( !shapes.Keys.Any( name => authored.ContainsKey( name ) || Density( name ).HasValue
			|| geometry.CollisionShells.ContainsKey( name ) || boxes?.ContainsKey( name ) == true ) )
			return Estimate( shapes.Values.SelectMany( p => p ) );
		double total = 0;
		foreach ( var (name, points) in shapes )
			total += authored.TryGetValue( name, out float mass ) ? mass
				: Estimate( points, Density( name ) ?? 500f, geometry.CollisionShells.GetValueOrDefault( name ),
					boxes is not null && boxes.TryGetValue( name, out var box ) ? box : null );
		if ( total > float.MaxValue ) throw new InvalidDataException( "Diesel prop mass exceeds the supported range." );
		return (float)total;

		float? Density( ulong name ) => geometry.CollisionMaterials.TryGetValue( name, out var material )
			&& densities.TryGetValue( material, out float density ) ? density : null;
	}

	static float Estimate( IEnumerable<Vector3> points, float density = 500f, float shellCentimeters = 0, BoxVolume? box = null )
	{
		// Mesh-backed and rounded collision retain the bounded AABB approximation.
		var volumeBox = box ?? new BoxVolume( BBox.FromPoints( points ).Size * 0.0254f, 1 );
		double volume = volumeBox.CubicMeters( shellCentimeters );
		return (float)Math.Clamp( volume * density, 0.1, 50000 );
	}
}
