namespace Renderware;

/// <summary>
/// A 2dfx light attached to a model: a corona sprite, and a real light when it has a range.
/// Position is in model space, GTA units. III and VC list them in .ide files, San Andreas inside the .dff.
/// </summary>
public readonly record struct LightEffect
{
	/// <summary>The model's id (IDE lights only).</summary>
	public int ObjectId { get; init; }
	public Vector3 Position { get; init; }
	public Color32 Color { get; init; }

	/// <summary>Corona sprite texture, e.g. "coronastar".</summary>
	public string Corona { get; init; }

	/// <summary>Ground light pool texture, e.g. "shad_exp".</summary>
	public string Shadow { get; init; }

	/// <summary>Corona draw distance, GTA units.</summary>
	public float Distance { get; init; }

	/// <summary>Point light radius, GTA units - 0 for a corona with no light.</summary>
	public float Range { get; init; }

	/// <summary>Corona size (radius), GTA units.</summary>
	public float Size { get; init; }

	public float ShadowSize { get; init; }

	/// <summary>When it shows: 0 always, 1 at night, others flicker / flash variants.</summary>
	public int LightType { get; init; }

	/// <summary>
	/// An IDE 2dfx line (III / VC), false if it isn't a light. Layout as the game reads it:
	/// id, x, y, z, r, g, b, a, type, corona, shadow, distance, range, size, shadow size, shadow intensity,
	/// light type, road reflection, flare, flags.
	/// </summary>
	public static bool TryRead( TextScript.Fields f, out LightEffect light )
	{
		light = default;
		if ( f.Count < 20 )
			return false;

		int id = f.Int();
		var position = f.Vector3();
		var color = new Color32( (byte)f.Int(), (byte)f.Int(), (byte)f.Int(), 255 );
		f.Skip(); // alpha, unused by the game

		if ( f.Int() != 0 )
			return false; // particles, ped attractors...

		light = new LightEffect
		{
			ObjectId = id,
			Position = position,
			Color = color,
			Corona = f.String(),
			Shadow = f.String(),
			Distance = f.Float(),
			Range = f.Float(),
			Size = f.Float(),
			ShadowSize = f.Float(),
		};

		f.Skip(); // shadow intensity
		light = light with { LightType = f.Int() };
		return true;
	}

	/// <summary>
	/// A light from San Andreas' 2d effect plugin, positioned just after its type and size:
	/// rgba, corona distance, range, corona size, shadow size, show mode, reflection, flare, shadow multiplier,
	/// flags, corona texture[24], shadow texture[24], ...
	/// </summary>
	public static LightEffect Read( BinaryReader br, Vector3 position )
	{
		var color = new Color32( br.ReadByte(), br.ReadByte(), br.ReadByte(), 255 );
		br.ReadByte(); // alpha

		var light = new LightEffect
		{
			Position = position,
			Color = color,
			Distance = br.ReadSingle(),
			Range = br.ReadSingle(),
			Size = br.ReadSingle(),
			ShadowSize = br.ReadSingle(),
			LightType = br.ReadByte(),
		};

		br.ReadBytes( 4 ); // reflection, flare, shadow multiplier, flags
		return light with { Corona = br.ReadFixedString( 24 ), Shadow = br.ReadFixedString( 24 ) };
	}
}
