using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Otis" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_otis", "otis", MonsterClass.PlayerAlly, 35, 16, 72, 145,
	Mount = "opfor", ArmorGroup = 10, Attack = NpcAttack.Bullet, Damage = 40, Range = 1024, StrikeEvents = new[] { 3 }, Gibs = GibSet.Human, Blood = BloodColor.Red )]
public sealed class Otis : SecurityGuard
{
	internal override string SentencePrefix => "OT";
	internal override NpcSounds Sounds { get; } = new( "!OT_IDLE", "!OT_ATTACK", "barney/ba_pain", "barney/ba_die",
		"weapons/de_shot1.wav", Follow: "!OT_OK", Wait: "!OT_WAIT" );
}
