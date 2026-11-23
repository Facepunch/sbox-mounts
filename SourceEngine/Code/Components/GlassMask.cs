namespace Source1;

// Source sends one occupancy bit for each of at most 16 by 16 panels.
public readonly record struct GlassMask( ulong A, ulong B, ulong C, ulong D )
{
	public static GlassMask MaxValue => ~default( GlassMask );
	public static GlassMask operator ~( GlassMask m ) => new( ~m.A, ~m.B, ~m.C, ~m.D );
	public static GlassMask operator ^( GlassMask m, GlassMask n ) => new( m.A ^ n.A, m.B ^ n.B, m.C ^ n.C, m.D ^ n.D );
	public bool Has( int x, int y )
	{
		ulong word = (y / 4) switch { 0 => A, 1 => B, 2 => C, _ => D };
		return (word & (1UL << ((y % 4) * 16 + x))) != 0;
	}
	public GlassMask With( int x, int y )
	{
		ulong bit = 1UL << ((y % 4) * 16 + x);
		return (y / 4) switch
		{
			0 => this with { A = A | bit },
			1 => this with { B = B | bit },
			2 => this with { C = C | bit },
			_ => this with { D = D | bit }
		};
	}
}
