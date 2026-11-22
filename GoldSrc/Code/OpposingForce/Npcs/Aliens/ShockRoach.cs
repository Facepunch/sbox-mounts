using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Shock Roach" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_shockroach", "w_shock_rifle", MonsterClass.AlienPrey, 10, 12, 24, 90,
	Mount = "opfor", Attack = NpcAttack.Leap, Damage = 10, Range = 256, Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed class ShockRoach : Headcrab
{
	internal override NpcSounds Sounds { get; } = new( "shockroach/shock_idle", "shockroach/shock_angry.wav", "shockroach/shock_flinch.wav", "shockroach/shock_die.wav", "shockroach/shock_jump", "shockroach/shock_bite.wav" );
	internal override string LeapCall( int variation ) => $"shockroach/shock_jump{(variation == 1 ? 1 : 2)}.wav";
	TimeSince _age;

	protected override void OnStart()
	{
		_age = 0;
		base.OnStart();
	}

	protected override void Think()
	{
		if ( _age >= 10 ) Health = 0;
		else base.Think();
	}

	internal override void ModifyDamage( Component.IDamageable.DamageEvent hit )
	{
		base.ModifyDamage( hit );
		if ( _age < 2 ) hit.AcceptedDamage = 0;
	}
}
