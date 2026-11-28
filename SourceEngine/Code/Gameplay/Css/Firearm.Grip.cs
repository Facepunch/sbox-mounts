namespace Source1.Css;

public partial class Firearm
{
	internal override (string Rig, string Sequence, string Reload)? NpcGripPose => Setup.Data?.PlayerAnimationExtension is { } extension && HoldType != WeaponHolds.DualPistols
		? ("mount://css/models/player/t_phoenix.mdl.vmdl", $"Idle_Upper_{extension}", $"Idle_Reload_{extension}") : null;

	Vector3? _leftGrip;
	SkinnedModelRenderer _gripBody;
	SkinnedModelRenderer _heldModel;
	Rotation _leftWrist;

	internal override async ValueTask<Vector3?> GetGripPosition( Model worldModel )
	{
		var weaponBone = worldModel.Bones.GetBone( "ValveBiped.weapon_bone" );
		var extension = Setup.Data?.PlayerAnimationExtension ?? worldModel.GetData<SourceModelWeaponData[]>()?.FirstOrDefault()?.PlayerAnimationExtension;
		if ( weaponBone is null || string.IsNullOrEmpty( extension ) ) return null;

		var clip = await Sandbox.Animation.Clip.LoadAsync( $"mount://css/models/player/t_phoenix.mdl.vmdl/idle_upper_{extension.ToLowerInvariant()}.anim" );
		var bones = clip?.Skeleton?.Bones;
		var right = bones?.FirstOrDefault( b => b.Name == "ValveBiped.weapon_bone_RHand" );
		if ( right is null || right.ParentIndex < 0 || bones[right.ParentIndex].Name != weaponBone.Name ) return null;

		var grip = weaponBone.LocalTransform.ToWorld( clip.GetLocalTransform( 0, right.Index ) );
		if ( HoldType == WeaponHolds.DualPistols && bones.FirstOrDefault( b => b.Name == "ValveBiped.weapon_bone_LHand" ) is { } left && left.ParentIndex == right.Index )
			_leftGrip = grip.PointToWorld( clip.GetLocalTransform( 0, left.Index ).Position );
		return grip.Position;
	}

	protected override void UpdateBodyAnimations( SkinnedModelRenderer body )
	{
		base.UpdateBodyAnimations( body );
		if ( _leftGrip is not { } target || !body.Animation.IsValid || !WorldModel.IsValid() ) return;
		if ( !_heldModel.IsValid() || _gripBody != body )
		{
			if ( body.Model.Bones.GetBone( "hold_L" ) is not { Parent: { } hand } hold ) return;
			_heldModel = WorldModel.GetComponentInChildren<SkinnedModelRenderer>();
			if ( !_heldModel.IsValid() ) return;
			_gripBody = body;
			_leftWrist = hold.LocalTransform.ToLocal( hand.LocalTransform ).Rotation;
		}

		var wrist = _heldModel.WorldTransform.ToWorld( new Transform( target, _leftWrist ) );
		var parameters = body.Animation.Parameters;
		if ( parameters.TrySet( "ik.hand_left", body.WorldTransform.ToLocal( wrist ) ) )
			parameters.TrySet( "ik.hand_left.enabled", true );
	}

	void ReleaseGrip()
	{
		if ( _gripBody.IsValid() && _gripBody.Animation.IsValid )
			_gripBody.Animation.Parameters.TrySet( "ik.hand_left.enabled", false );
		_gripBody = null;
		_heldModel = null;
	}
}
