using GoldSrc.HalfLife;

namespace GoldSrc.DeathmatchClassic;

[Title( "Axe" ), Category( "Deathmatch Classic" )]
[WeaponPickup( "crowbar", null, 0, Mount = "dmc", ClassName = "axe", PickupModel = "p_crowbar", Weight = 1 )]
public sealed class Axe : ClassicWeapon
{
	internal override string DeploySequence => "idle1";
	internal override string ShotSequence => "attack1";
	internal override string ShotSound => "weapons/ax1.wav";
	TimeUntil _impact;
	bool _swinging;

	internal override void Configure()
	{
		base.Configure();
		HoldType = WeaponHolds.Melee;
		PrimaryDelay = 0.5f;
	}

	internal override bool Release( Ray ray )
	{
		_impact = 0.3f;
		_swinging = true;
		return true;
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( !_swinging || _impact > 0 ) return;
		_swinging = false;
		if ( !IsActive || !Owner.IsValid() ) return;
		var aim = Owner.EyeTransform.ForwardRay;
		var hit = Trace( aim.Position, aim.Position + aim.Forward * 64 );
		if ( hit.Hit ) Hit( hit, 20, "club" );
	}
}
