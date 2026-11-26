namespace NS2;

public sealed partial class Skulk
{
	const float MaxArmor = 10;
	[Sync( SyncFlags.FromHost )] public float Armor { get; private set; } = MaxArmor;
}
