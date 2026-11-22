using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Dead Zombie Soldier" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_zombie_soldier_dead", "zombie_soldier", MonsterClass.AlienMonster, 8, 0, 0, 0,
	Mount = "opfor", Gibs = GibSet.Alien, Blood = Npc.BloodColor.Yellow )]
public sealed class DeadZombieSoldier : PosedCorpse
{
	static readonly string[] Poses = ["dead_on_stomach", "dead_on_back"];
	[Property, Range( 0, 1 )] public int Pose { get; set; }
	internal override string PoseName => Poses[Pose.Clamp( 0, Poses.Length - 1 )];
}
