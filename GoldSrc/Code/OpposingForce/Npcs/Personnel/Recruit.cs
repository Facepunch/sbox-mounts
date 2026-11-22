using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Recruit" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_recruit", "recruit", MonsterClass.PlayerAlly, 35, 16, 72, 145,
	Mount = "opfor", Gibs = GibSet.Human, Blood = BloodColor.Red )]
public sealed class Recruit : Personnel
{
	protected override int PainVariants => 3;
	internal override string SentencePrefix => "RC";
	internal override NpcSounds Sounds { get; } = new( "!RC_IDLE", "!RC_ATTACK", "barney/ba_pain", "barney/ba_die",
		"barney/ba_attack2.wav", Follow: "!RC_OK", Wait: "!RC_WAIT" );
}
