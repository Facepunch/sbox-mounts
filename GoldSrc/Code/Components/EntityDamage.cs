namespace GoldSrc.Maps;

public sealed class EntityDamage : DamageInfo
{
	public int Types { get; }

	private static readonly string[] TypeTags =
	[
		"crush", "bullet", "slash", "burn", "freeze", "fall", "explosion", "club",
		"shock", "sonic", "energybeam", null, "nevergib", "alwaysgib", "drown", "paralyze",
		"nervegas", "poison", "radiation", "drownrecover", "acid", "slowburn", "slowfreeze", "mortar"
	];

	public EntityDamage( float amount, int types, GameObject source, Vector3 position )
	{
		Damage = amount;
		Types = types;
		Attacker = source;
		Weapon = source;
		Origin = source.WorldPosition;
		Position = position;
		for ( int i = 0; i < TypeTags.Length; i++ )
			if ( (types & (1 << i)) != 0 && TypeTags[i] is { } tag )
				Tags.Add( tag );
	}
}
