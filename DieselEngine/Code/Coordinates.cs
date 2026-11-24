namespace Diesel;

static class Coordinates
{
	// Diesel: centimetres, +Y forward, +Z up. Source: inches, +X forward, +Y left.
	public const float Scale = 1 / 2.54f;
	public static Vector3 Position( Vector3 p ) => new Vector3( p.y, -p.x, p.z ) * Scale;
	public static Rotation Rotation( Rotation r ) => new( r.y, -r.x, r.z, r.w );
}
