using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Hide]
public abstract class AlliedSidearmGrunt : AlliedGrunt
{
	internal override bool HasShotgun => false;

	internal override void FireBullet( GameObject target, Vector3 origin, Vector3 end )
	{
		if ( Ammo <= 0 || FriendlyInLine( target ) ) return;
		Ammo--;
		PlayGunshot( "weapons/desert_eagle_fire.wav", origin );
		EjectShell( origin, WorldRotation );
		TraceBullets( origin, end, 1, 40, 0.01745f, 1024 );
	}

	internal override void OnGibbed() { }
	internal override NpcCorpse AddCorpseComponent( GameObject go ) => go.AddComponent<NpcCorpse>();

	internal override void HandleActionEvent( int id )
	{
		if ( id == 11 ) return;
		base.HandleActionEvent( id );
	}
}
