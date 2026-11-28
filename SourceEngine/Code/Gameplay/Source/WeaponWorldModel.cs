using System;

namespace Source1;

[Title( "Source Weapon World Model" )]
[Category( "Source 1" )]
[Alias( "Source1.SourceWeaponWorldModel", "SourceWeaponWorldModel" )]
public sealed class WeaponWorldModel : BaseWeaponModel
{
	Model _alignedModel;
	Model _effectsModel;
	BaseCombatWeapon _weapon;
	string _ownFlashType;
	string _muzzleName;
	string[] _alternateMuzzles = [];
	(string Type, string Attachment)[] _projectileFlashes = [];
	int _shots;
	bool _pumpPending;
	bool _silencerShown;
	Model _unsilencedModel;
	TimeUntil _pumpAt;
	GameObject _leftHand;

	public override void OnAttack( Vector3? hitPoint = null, Vector3? origin = null )
	{
		Attack( hitPoint, origin, false );
	}

	internal void OnDoubleShot( Vector3? hitPoint ) => Attack( hitPoint, null, true );

	void Attack( Vector3? hitPoint, Vector3? origin, bool alternate )
	{
		base.OnAttack( hitPoint, origin );
		if ( _weapon is not BulletWeapon weapon )
		{
			foreach ( var (flashType, attachment) in _projectileFlashes )
				WeaponEffects.MuzzleFlash( flashType, Renderer.GetAttachmentObject( attachment ), false, MountContent.Of( Renderer.Model ) );
			return;
		}
		// World models rarely author their own flash; the viewmodel's AE_MUZZLEFLASH names the weapon's.
		var type = _ownFlashType ?? weapon.Setup.FlashType ?? "SMG1";
		var mount = MountContent.Of( Renderer.Model );
		if ( !weapon.Silenced ) WeaponEffects.MuzzleFlash( type, NextMuzzle(), false, mount, weapon.Setup.FlashScale );
		if ( alternate && weapon.AlternateProjectile == Projectile.Kinds.ContactGrenade ) return;
		// weapon_shotgun's Pump plays its sound for everyone; the owner's viewmodel plays it with the pump clip.
		if ( weapon.Setup.PumpDelay is { } delay )
		{
			_pumpPending = true;
			_pumpAt = alternate ? weapon.AlternatePumpDelay : delay;
		}
		else EjectShell();
	}

	internal void PlayPump()
	{
		_pumpPending = false;
		Sounds.PlayWeaponSound( _weapon, "special1" );
		EjectShell();
	}

	/// <summary>
	/// Counter-Strike's dual Elites flash muzzle_flash_l and muzzle_flash_r in turn,
	/// other guns always use their muzzle.
	/// </summary>
	GameObject NextMuzzle()
	{
		if ( _weapon is Weapon { WorldMuzzleAttachment: { } name } && Renderer.GetAttachmentObject( name ) is { } muzzle )
			return MuzzleGameObject = muzzle;
		if ( _alternateMuzzles.Length == 0 ) return MuzzleGameObject;
		MuzzleGameObject = Renderer.GetAttachmentObject( _alternateMuzzles[_shots++ % _alternateMuzzles.Length] );
		return MuzzleGameObject;
	}

	/// <summary>
	/// A held gun's brass in third person, from the attachment its viewmodel ejects from when the world model has
	/// it (HL2's w_ models do). Counter-Strike models eject from the
	/// muzzle toward the shooter's right and a little up at 120. NPC volleys don't litter shells, as in sandbox.
	/// </summary>
	void EjectShell()
	{
		if ( _weapon is not BulletWeapon { NpcControlled: false } weapon || weapon.Setup.Brass is not { } brass ) return;
		// A first-person shooter's hidden world model throws none; its viewmodel does.
		if ( Scene.Camera is { } camera && Renderer.GameObject.Tags.HasAny( camera.RenderExcludeTags ) ) return;
		var mount = MountContent.Of( Renderer.Model );
		var owner = weapon.Owner;
		var inherited = owner.IsValid() ? owner.Velocity : Vector3.Zero;
		var eye = Rotation.LookAt( weapon.AimRay.Forward );
		if ( Renderer.Model.Attachments.All.Any( a => a.Name == brass.Attachment ) && Renderer.GetAttachmentObject( brass.Attachment ) is { } port )
			brass.Eject( port, eye, inherited, mount );
		else if ( MuzzleGameObject.IsValid() )
			brass.EjectFromMuzzle( MuzzleGameObject, eye, inherited, mount );
	}

	/// <summary>
	/// Held world models are cloned onto the holder's hand, outside the weapon's own hierarchy, but
	/// inside the holder's.
	/// </summary>
	BaseCombatWeapon FindWeapon() => GetComponentInParent<BaseCombatWeapon>()
		?? GameObject.Root.GetComponentsInChildren<BaseCombatWeapon>( true )
			.FirstOrDefault( w => w.WorldModel.IsValid() && w.WorldModel.GetComponentInChildren<WeaponWorldModel>() == this );

	protected override void OnUpdate()
	{
		if ( !Renderer.IsValid() || !Renderer.Model.IsValid() || Renderer.Model.IsError ) return;
		UpdateSilencer();
		if ( _effectsModel != Renderer.Model ) Configure();
		if ( _pumpPending && _pumpAt <= 0 )
			PlayPump();
		MuzzleGameObject = _muzzleName is null ? null : Renderer.GetAttachmentObject( _muzzleName );
		if ( Renderer.BoneMergeTarget.IsValid() || _weapon?.Holder is Sandbox.Npcs.BaseNpc )
		{
			Renderer.LocalTransform = global::Transform.Zero;
			_alignedModel = null;
			ReleaseLeftHand();
			return;
		}
		if ( _alignedModel != Renderer.Model ) AlignToHand();
		FollowLeftHand();
	}

	/// <summary>Fit an authored wrist to the holder's grip frame. A ValveBiped hand uses the opposite roll axis.</summary>
	void AlignToHand()
	{
		_alignedModel = Renderer.Model;
		ReleaseLeftHand();
		Renderer.LocalTransform = global::Transform.Zero;
		// Counter-Strike's dual Elites carry their second gun on the left hand bone.
		if ( Renderer.Model.Bones.GetBone( "ValveBiped.Bip01_L_Hand" ) is { } left && Renderer.GetBoneObject( left ) is { } leftObject )
		{
			leftObject.Flags |= GameObjectFlags.ProceduralBone;
			_leftHand = leftObject;
		}
		var hand = Renderer.Model.Bones.GetBone( "ValveBiped.Bip01_R_Hand" );
		if ( WristOffset( GameObject.Parent ) is not { } wrist ) return;
		if ( hand is null )
		{
			if ( _weapon is Weapon weapon ) _ = AlignGrip( weapon, Renderer.Model, wrist );
			return;
		}
		var grip = new Transform( wrist, Rotation.FromRoll( 180 ) );
		Renderer.LocalTransform = grip.ToWorld( hand.LocalTransform.ToLocal( global::Transform.Zero ) );
	}

	async Task AlignGrip( Weapon weapon, Model model, Vector3 wrist )
	{
		var offset = await weapon.GetGripPosition( model );
		if ( offset.HasValue && Renderer.IsValid() && Renderer.Model == model && !Renderer.BoneMergeTarget.IsValid() )
			Renderer.LocalPosition = wrist - offset.Value;
	}

	void ReleaseLeftHand()
	{
		if ( _leftHand.IsValid() ) _leftHand.Flags &= ~GameObjectFlags.ProceduralBone;
		_leftHand = null;
	}

	/// <summary>The left hand's gun follows the holder's left hold bone, a grip frame like the right one but not mirrored.</summary>
	void FollowLeftHand()
	{
		if ( !_leftHand.IsValid() ) return;
		var hold = GameObject.Parent?.Components.GetInAncestors<SkinnedModelRenderer>()?.GetBoneObject( "hold_L" );
		if ( hold is null || WristOffset( hold ) is not { } wrist ) return;
		_leftHand.WorldTransform = hold.WorldTransform.ToWorld( new Transform( wrist ) );
	}

	/// <summary>The holder's wrist, the hold bone's parent, relative to a hold bone object.</summary>
	static Vector3? WristOffset( GameObject hold )
	{
		var holder = hold?.Components.GetInAncestors<SkinnedModelRenderer>();
		var bone = holder?.Model?.Bones.GetBone( hold.Name );
		if ( bone?.Parent is null ) return null;
		return bone.LocalTransform.ToLocal( bone.Parent.LocalTransform ).Position;
	}

	/// <summary>Counter-Strike swaps the held model for the script's SilencerModel while the silencer is on.</summary>
	void UpdateSilencer()
	{
		var silencer = _weapon is BulletWeapon { Silenced: true } gun ? gun.Setup.Data?.SilencerModel : null;
		var silenced = silencer is not null;
		if ( silenced == _silencerShown ) return;
		_silencerShown = silenced;
		var mount = MountContent.Of( Renderer.Model );
		if ( silenced ) _unsilencedModel = Renderer.Model;
		var path = silenced ? $"mount://{mount}/{silencer}.vmdl" : null;
		var model = path is not null && Sandbox.Mounting.Directory.Get( mount )?.GetByPath( path ) is not null ? Model.Load( path ) : _unsilencedModel;
		if ( model.IsValid() && !model.IsError ) Renderer.Model = model;
	}

	void Configure()
	{
		var model = Renderer.Model;
		_effectsModel = model;
		// AE_NPC_MUZZLEFLASH names the world model's flash; the flash itself is rebuilt from HL2's code.
		MuzzleEffect = null;
		TracerEffect = null;
		_weapon = FindWeapon();
		var sequences = ModelSequences.Of( model ).All;
		_projectileFlashes = _weapon is ProjectileWeapon && sequences.FirstOrDefault( s => s.Activity == "ACT_VM_PRIMARYATTACK" ) is { } attack
			? attack.Events.Where( e => e.Name is "AE_NPC_MUZZLEFLASH" or "AE_MUZZLEFLASH" )
				.Select( e => e.Options?.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) )
				.Where( parts => parts is { Length: >= 2 } ).Select( parts => (parts[0], parts[1]) ).ToArray()
			: [];
		var flash = sequences.SelectMany( s => s.Events )
			.FirstOrDefault( e => e.Name is "AE_NPC_MUZZLEFLASH" or "AE_MUZZLEFLASH" )?.Options?.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		_ownFlashType = _weapon.IsValid() ? WeaponEffects.FlashType( flash?.FirstOrDefault() ) : null;
		// Options are "<weapon type> <attachment>"; otherwise Source's default muzzle is attachment 1.
		_muzzleName = flash?.ElementAtOrDefault( 1 ) ?? model.Attachments.All.FirstOrDefault()?.Name;
		var muzzles = model.Attachments.All.Where( a => a.Name.StartsWith( "muzzle_flash_", StringComparison.OrdinalIgnoreCase ) ).Select( a => a.Name ).Order().ToArray();
		_alternateMuzzles = muzzles.Length > 1 ? muzzles : [];
	}
}
