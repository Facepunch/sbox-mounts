namespace GoldSrc.HalfLife;

[Hide]
public sealed class GrenadeAmmo : AmmoPickup
{
	internal static void Drop( Transform transform, bool toss = false )
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ) return;
		foreach ( var dir in mount.GameDirs )
		{
			var path = $"mount://hl1/{dir}/models/w_argrenade.mdl.vmdl";
			if ( mount.GetByPath( path ) is not ModelLoader ) continue;
			var model = Model.Load( path );
			if ( !model.IsValid() || model.IsError ) continue;

			var go = Create( transform, model, GameResource.Load<BaseAmmoResource>( $"mount://hl1/{Rifle.GrenadeAmmoPath}" ), 2, "AR grenades" );
			WeaponPickupLoader.ApplyDropVelocity( go.GetComponent<Rigidbody>(), toss );
			go.NetworkSpawn( true, null );
			return;
		}
	}

}
