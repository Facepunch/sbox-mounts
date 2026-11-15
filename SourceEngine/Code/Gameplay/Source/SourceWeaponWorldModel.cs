using System;

namespace Source1;

[Title( "Source Weapon World Model" )]
[Category( "Source 1" )]
public sealed partial class SourceWeaponWorldModel : BaseWeaponModel
{
	Model _alignedModel;
	Model _effectsModel;
	BaseCombatWeapon _weapon;
	string _ownFlashType;
	string _muzzleName;
	string[] _alternateMuzzles = [];
	int _shots;
	int _tracerShots;
	bool _pumpPending;
	bool _silencerShown;
	Model _unsilencedModel;
	TimeUntil _pumpAt;
	GameObject _leftHand;

	public override void OnAttack( Vector3? hitPoint = null, Vector3? origin = null )
	{
		base.OnAttack( hitPoint, origin );
		_heldShot++;
		_shotTime = 0;
		if ( _weapon is not SourceBulletWeapon weapon ) return;
		// World models rarely author their own flash; the viewmodel's AE_MUZZLEFLASH names the weapon's.
		var type = _ownFlashType ?? SourceWeaponSetup.FlashType( weapon ) ?? "SMG1";
		var mount = SourceMount.Of( Renderer.Model );
		if ( !weapon.Silenced ) SourceWeaponEffects.MuzzleFlash( type, NextMuzzle(), false, mount, SourceWeaponSetup.FlashScale( weapon ) );
		if ( hitPoint is { } hit && SourceWeaponEffects.HasTracer( type, _tracerShots++ ) )
			SourceWeaponEffects.Tracer( type, origin ?? GetTracerOrigin().Position, hit, mount );
		// weapon_shotgun's Pump plays its sound for everyone; the owner's viewmodel plays it with the pump clip.
		if ( SourceWeaponSetup.PumpDelay( weapon ) is { } delay )
		{
			_pumpPending = true;
			_pumpAt = delay;
		}
		else EjectShell();
	}

	/// <summary>
	/// Counter-Strike's dual Elites flash muzzle_flash_l and muzzle_flash_r in turn (CWeaponElite::GetMuzzleAttachment);
	/// other guns always use their muzzle.
	/// </summary>
	GameObject NextMuzzle()
	{
		if ( _alternateMuzzles.Length == 0 ) return MuzzleGameObject;
		MuzzleGameObject = Renderer.GetAttachmentObject( _alternateMuzzles[_shots++ % _alternateMuzzles.Length] );
		return MuzzleGameObject;
	}

	/// <summary>
	/// A held gun's brass in third person, from the attachment its viewmodel ejects from when the world model has
	/// it (HL2's w_ models do). Counter-Strike's don't: C_CSPlayer::ProcessMuzzleFlashEvent throws it from the
	/// muzzle toward the shooter's right and a little up at 120. NPC volleys don't litter shells, as in sandbox.
	/// </summary>
	void EjectShell()
	{
		if ( _weapon is not SourceBulletWeapon { NpcControlled: false } weapon || SourceWeaponSetup.Brass( weapon ) is not { } brass ) return;
		// A first-person shooter's hidden world model throws none; its viewmodel does.
		if ( Scene.Camera is { } camera && Renderer.GameObject.Tags.HasAny( camera.RenderExcludeTags ) ) return;
		var mount = SourceMount.Of( Renderer.Model );
		var owner = weapon.Owner;
		var inherited = owner.IsValid() ? owner.Velocity : Vector3.Zero;
		var eye = Rotation.LookAt( weapon.AimRay.Forward );
		if ( Renderer.Model.Attachments.All.Any( a => a.Name == brass.Attachment ) && Renderer.GetAttachmentObject( brass.Attachment ) is { } port )
		{
			var axes = port.WorldRotation;
			SourceWeaponEffects.EjectBrass( brass.Model, port.WorldPosition, axes.Forward, axes.Right, axes.Up, brass.Speed, eye, inherited, mount );
			return;
		}
		if ( brass.Speed is null || !MuzzleGameObject.IsValid() ) return;
		var toss = Rotation.LookAt( eye.Right * 100 + eye.Up * 20 );
		SourceWeaponEffects.EjectBrass( brass.Model, MuzzleGameObject.WorldPosition, toss.Forward, toss.Right, toss.Up, 120, eye, inherited, mount );
	}

	/// <summary>
	/// Held world models are cloned onto the holder's hand, outside the weapon's own hierarchy, but
	/// inside the holder's.
	/// </summary>
	BaseCombatWeapon FindWeapon() => GetComponentInParent<BaseCombatWeapon>()
		?? GameObject.Root.GetComponentsInChildren<BaseCombatWeapon>( true )
			.FirstOrDefault( w => w.WorldModel.IsValid() && w.WorldModel.GetComponentInChildren<SourceWeaponWorldModel>() == this );

	protected override void OnUpdate()
	{
		if ( !Renderer.IsValid() || !Renderer.Model.IsValid() || Renderer.Model.IsError ) return;
		UpdateSilencer();
		if ( _effectsModel != Renderer.Model ) Configure();
		if ( _pumpPending && _pumpAt <= 0 )
		{
			_pumpPending = false;
			if ( SourceSounds.WeaponSound( _weapon, "special1" ) is { } pump ) GameObject.PlaySound( pump );
			EjectShell();
		}
		MuzzleGameObject = _muzzleName is null ? null : Renderer.GetAttachmentObject( _muzzleName );
		if ( Renderer.BoneMergeTarget.IsValid() )
		{
			StopHold();
			Renderer.LocalTransform = global::Transform.Zero;
			_alignedModel = null;
			ReleaseLeftHand();
			return;
		}
		if ( UpdateHold() ) return;
		if ( _alignedModel != Renderer.Model ) AlignToHand();
		FollowLeftHand();
	}

	/// <summary>
	/// The hold when the mounted game has no pose for the weapon, see <see cref="UpdateHold"/>: the holder's hold type poses the arms.
	/// Holds the model the way an NPC's bonemerge would: its ValveBiped.Bip01_R_Hand goes to the holder's wrist,
	/// the hold bone's parent. Source's hand bone points down the barrel with its up axis along the grip, the hold
	/// bone's grip frame rolled half a turn. Models without the bone (props such as HL2's w_357 and w_crossbow,
	/// Counter-Strike's weapon_bone models) are held by their origin, X forward and Z up, as Source authors them.
	/// </summary>
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
		if ( hand is null || WristOffset( GameObject.Parent ) is not { } wrist ) return;
		var grip = new Transform( wrist, Rotation.FromRoll( 180 ) );
		Renderer.LocalTransform = grip.ToWorld( hand.LocalTransform.ToLocal( global::Transform.Zero ) );
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
		var silenced = _weapon is SourceBulletWeapon { Silenced: true } && SourceWeaponSetup.WeaponData( _weapon )?.SilencerModel is not null;
		if ( silenced == _silencerShown ) return;
		_silencerShown = silenced;
		var mount = SourceMount.Of( Renderer.Model );
		if ( silenced ) _unsilencedModel = Renderer.Model;
		var path = silenced ? $"mount://{mount}/{SourceWeaponSetup.WeaponData( _weapon ).SilencerModel}.vmdl" : null;
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
		var flash = (model.GetData<SourceModelSequenceData[]>() ?? []).SelectMany( s => s.Events )
			.FirstOrDefault( e => e.Name is "AE_NPC_MUZZLEFLASH" or "AE_MUZZLEFLASH" )?.Options?.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		_ownFlashType = _weapon.IsValid() ? SourceWeaponEffects.FlashType( flash?.FirstOrDefault() ) : null;
		// Options are "<weapon type> <attachment>"; otherwise Source's default muzzle is attachment 1.
		_muzzleName = flash?.ElementAtOrDefault( 1 ) ?? model.Attachments.All.FirstOrDefault()?.Name;
		var muzzles = model.Attachments.All.Where( a => a.Name.StartsWith( "muzzle_flash_", StringComparison.OrdinalIgnoreCase ) ).Select( a => a.Name ).Order().ToArray();
		_alternateMuzzles = muzzles.Length > 1 ? muzzles : [];
	}
}
