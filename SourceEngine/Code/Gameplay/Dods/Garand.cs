namespace Source1.Dods;

public sealed class Garand : Rifle
{
	internal override GripPose NpcGripPose => new( "mount://dods/models/player/american_rifleman.mdl.vmdl", "StandIdle_RIFLE", ["Reload_RIFLE"], "StandAim_RIFLE" );

	internal override bool UsesDeployedClips => Sighted;

	public override bool CanReload() => Clip1 == 0 && base.CanReload();

	public override void PrimaryAttack()
	{
		var last = Clip1 == 1;
		base.PrimaryAttack();
		if ( last && Clip1 == 0 ) EjectClip();
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void EjectClip()
	{
		Sounds.PlayWeaponSound( this, "special1" );
		if ( Application.IsDedicatedServer || WeaponModel is not { } model ) return;
		var port = model.ShellEjectGameObject;
		if ( !port.IsValid() && model.Renderer.IsValid() ) port = model.Renderer.GetAttachmentObject( "shell_eject" );
		if ( !port.IsValid() ) return;
		var position = model is ViewModel view ? view.DrawnPosition( port ) : port.WorldPosition;
		WeaponEffects.EjectBrass( "models/shells/garand_clip.mdl", position, Vector3.Up, port.WorldRotation, MountContent.OfWeapon( this ) );
	}
}
