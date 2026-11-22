using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Drill Sergeant" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_drillsergeant", "drill", MonsterClass.PlayerAlly, 35, 16, 72, 145,
	Mount = "opfor", Gibs = GibSet.Human, Blood = BloodColor.Red )]
public sealed class DrillSergeant : Personnel
{
	protected override int PainVariants => 3;
	internal override string SentencePrefix => "DR";
	internal override NpcSounds Sounds { get; } = new( "!DR_IDLE", "!DR_ATTACK", "barney/ba_pain", "barney/ba_die",
		"barney/ba_attack2.wav", Follow: "!DR_OK", Wait: "!DR_WAIT" );
}
