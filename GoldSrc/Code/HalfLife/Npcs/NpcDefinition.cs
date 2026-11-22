namespace GoldSrc.HalfLife;

internal enum GibSet { None, Human, Alien }
internal enum NpcMovement { Ground, Flying, Swimming, Stationary, Bouncing }
internal enum NpcAttack { None, Melee, Leap, Bullet, Projectile, Shock }
internal enum MonsterClass { None, Machine, Player, HumanPassive, HumanMilitary, AlienMilitary, AlienPassive, AlienMonster, AlienPrey, AlienPredator, Insect, PlayerAlly, PlayerWeapon, AlienWeapon }

[System.AttributeUsage( System.AttributeTargets.Class, Inherited = false )]
internal sealed class NpcDefinitionAttribute : System.Attribute
{
	public NpcDefinitionAttribute( string className, string model, MonsterClass classification, float health, float radius, float height, float speed )
	{
		ClassName = className;
		Model = model;
		Classification = classification;
		Health = health;
		Radius = radius;
		Height = height;
		Speed = speed;
	}

	public string Mount { get; set; } = "hl1";
	public string ClassName { get; }
	public string[] Aliases { get; set; } = [];
	public string Model { get; }
	public MonsterClass Classification { get; }
	public float Health { get; }
	public float Radius { get; }
	public float Height { get; }
	public float Speed { get; }
	public NpcAttack Attack { get; set; }
	public int ArmorGroup { get; set; } = -1;
	public bool MetalBody { get; set; }
	public GibSet Gibs { get; set; }
	public Npc.BloodColor Blood { get; set; }
	public float Damage { get; set; }
	public float Range { get; set; }
	public NpcMovement Movement { get; set; }
	public string AttackSequence { get; set; }
	public int[] StrikeEvents { get; set; }
	public string ProjectileModel { get; set; }
	public string IdleSequence { get; set; }
}
