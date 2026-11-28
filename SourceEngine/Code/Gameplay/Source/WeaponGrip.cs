using Sandbox.Animation;

namespace Source1;

/// <summary>Weapon and support-hand transforms expressed in the driving wrist's space.</summary>
internal sealed record WeaponGrip( Transform Weapon, Transform SupportHand, Clip Fingers, Clip Reload, string Helper, string Driver, SourceModelSequenceData.LayerRamp? ReloadBlend )
{
	internal const string Right = "ValveBiped.Bip01_R_Hand";
	internal const string Left = "ValveBiped.Bip01_L_Hand";

	internal static WeaponGrip From( Model body, Model weapon, (string Rig, string Sequence, string Reload)? pose )
	{
		var anchor = weapon?.Bones.GetBone( "ValveBiped.weapon_bone" ) ?? weapon?.Bones.GetBone( "ValveBiped.weapon_bone_left" );
		if ( anchor is null || body.Bones.GetBone( anchor.Name ) is not null || pose is not { } hold ) return null;

		var rig = Model.Load( hold.Rig );
		if ( !rig.IsValid() || rig.IsError || ModelSequences.Of( rig ).Get( hold.Sequence ) is not { } sequence
			|| !rig.AnimationClips().TryGetValue( sequence.Name, out var clip ) || !clip.IsBound ) return null;
		var bones = clip.Skeleton.Bones;
		Transform? Sample( string name )
		{
			var target = sequence.IkTargets.FirstOrDefault( x => x.Bone == name )?.TargetBone ?? name;
			var bone = bones.FirstOrDefault( x => x.Name == target );
			if ( bone is null ) return null;
			var pose = clip.GetLocalTransform( 0, bone.Index );
			for ( var i = bone.ParentIndex; i >= 0; i = bones[i].ParentIndex )
				pose = clip.GetLocalTransform( 0, i ).ToWorld( pose );
			return pose;
		}

		if ( Sample( Right ) is not { } right || Sample( Left ) is not { } left || Sample( anchor.Name ) is not { } mount ) return null;
		var origin = mount.ToWorld( anchor.LocalTransform.ToLocal( Transform.Zero ) );
		var clips = body.AnimationClips();
		var helper = clips.Skeleton.Bones.FirstOrDefault( b => b.ParentIndex >= 0 && clips.Skeleton.Bones[b.ParentIndex].Name == Right && IsSupportTarget( b.Name ) );
		var driver = clips.Skeleton.Bones.FirstOrDefault( b => IsDrivingTarget( b.Name ) );
		var reload = ModelSequences.Of( rig ).Get( hold.Reload );
		var motion = helper is not null && driver is not null && reload is not null && rig.AnimationClips().TryGetValue( reload.Name, out var action )
			? clips.ReloadGrip( action, reload, helper.Index, driver.Index ) : null;
		var support = right.ToLocal( left );
		return new( right.ToLocal( origin ), support, clips.FingerPose( clip, helper?.Index, support ), motion, helper?.Name, driver?.Name, reload?.PoseBlend );
	}

	internal static bool IsSupportTarget( string name ) => name.StartsWith( "ik_target:" ) && name.EndsWith( ":" + Right );

	internal static bool IsArm( string name ) => SourceAnimations.IsFinger( name ) || name is Right or Left
		or "ValveBiped.Bip01_R_Clavicle" or "ValveBiped.Bip01_L_Clavicle"
		or "ValveBiped.Bip01_R_UpperArm" or "ValveBiped.Bip01_L_UpperArm"
		or "ValveBiped.Bip01_R_Forearm" or "ValveBiped.Bip01_L_Forearm";

	internal static bool IsDrivingTarget( string name ) => name == "ik_target:rhand:ValveBiped.Bip01_Spine2";

	internal GraphBuilder.Node Apply( GraphBuilder builder, GraphBuilder.Node pose, GraphBuilder.Node weight, GraphBuilder.Node phase )
	{
		var layers = builder.Layers( pose );
		var mask = builder.BoneMask( SourceAnimations.FingerMask );
		layers.AddLayer( builder.Pose( Fingers, new GraphBuilder.PoseSettings() ), new() { BlendMode = GraphBuilder.LayerBlendMode.Overlay, BoneMask = mask } );
		var support = Helper is null
			? builder.TargetOffset( builder.BoneTarget( Right ), SupportHand.Position, SupportHand.Rotation, true, null, true )
			: builder.BoneTarget( Helper );
		var arms = builder.TwoBoneIK( layers, Left, support, new() { WorldSpace = false, BlendTime = 0 } );

		// If the support arm reaches full extension, bring the rifle to the hand using the same grip in reverse.
		var inverse = SupportHand.ToLocal( Transform.Zero );
		var drive = builder.TargetOffset( builder.BoneTarget( Left ), inverse.Position, inverse.Rotation, true, null, true );
		arms = builder.TwoBoneIK( arms, Right, drive, new() { WorldSpace = false, BlendTime = 0 } );
		if ( Reload is null ) return arms;

		var action = builder.Layers( arms );
		action.AddLayer( builder.Pose( Reload, phase ), new() { BlendMode = GraphBuilder.LayerBlendMode.Overlay, BoneMask = builder.BoneMask( SourceAnimations.GripMask ) } );
		var drivingHand = builder.TwoBoneIK( action, Right, builder.BoneTarget( Driver ), new() { WorldSpace = false, BlendTime = 0 } );
		return builder.TwoBoneIK( builder.Blend( arms, drivingHand, weight ), Left, support, new() { WorldSpace = false, BlendTime = 0 } );
	}
}
