using GoldSrc.HalfLife;

namespace GoldSrc.DeathmatchClassic;

[Title( "Lightning Gun" ), Category( "Deathmatch Classic" )]
[WeaponPickup( "light", "cells", 100, Mount = "dmc", ClassName = "lightning", PickupModel = "g_light", Weight = 8 )]
public sealed class LightningGun : ClassicWeapon
{
	TimeUntil _nextSound;

	internal override void Configure()
	{
		base.Configure();
		StartingAmmo = 15;
		PrimaryDelay = 0.1f;
	}

	internal override bool Release( Ray ray )
	{
		if ( WaterBody.Find( Scene, Owner.EyePosition ).IsValid() )
		{
			int cells = System.Math.Min( Ammo1, Definition.Limit );
			if ( cells > 1 ) TakePrimaryAmmo( cells - 1 );
			ClassicExplosion.Damage( GameObject, Owner.GameObject, Owner.WorldPosition, cells * 35 );
			return true;
		}
		var end = ray.Position + ray.Forward * 600;
		var wall = Scene.Trace.Ray( ray.Position, end ).IgnoreGameObjectHierarchy( Owner.GameObject )
			.WithoutTags( "npc", "player", "playercontroller", "trigger" ).Run();
		var offset = Rotation.LookAt( ray.Forward ).Right * 16;
		var victims = new HashSet<IDamageable>();
		for ( int i = -1; i <= 1; i++ )
		{
			var hit = Trace( ray.Position + offset * i, wall.EndPosition + offset * i );
			if ( hit.GameObject?.GetComponentInParent<IDamageable>() is { } victim && victims.Add( victim ) )
				Hit( hit, 30, "shock" );
		}
		ShowBeam( ray.Position, wall.EndPosition );
		return true;
	}

	internal override void PlayLaunchSound()
	{
		if ( _nextSound > 0 ) return;
		PlaySample( "weapons/lhit.wav" );
		_nextSound = 0.6f;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ShowBeam( Vector3 start, Vector3 end ) =>
		EnergyBeam.Show( start, end, Color.White, "laserbeam", "Lightning gun", 3, 0.12f, ClassicExplosion.Mount );

}
