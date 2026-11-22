using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	Transform[] lastPhysicsPose;
	Transform[] animatedPose;
	IDisposable getUpHook;
	TimeSince sinceGetUp;

	void CapturePhysicsPose()
	{
		var model = Renderer.Model;
		if ( lastPhysicsPose?.Length != model.BoneCount )
			lastPhysicsPose = new Transform[model.BoneCount];
		for ( int i = 0; i < lastPhysicsPose.Length; i++ )
			lastPhysicsPose[i] = Renderer.SceneModel.GetBoneWorldTransform( i );
	}

	(int Index, int Parent)[] hierarchy;

	void BeginGetUpBlend()
	{
		// Build parent-first order once so a frame can process every bone without allocating.
		var order = new List<(int, int)>( lastPhysicsPose.Length );
		void Visit( BoneCollection.Bone bone )
		{
			order.Add( (bone.Index, bone.Parent?.Index ?? -1) );
			foreach ( var child in bone.Children ) Visit( child );
		}
		foreach ( var bone in Renderer.Model.Bones.AllBones )
			if ( bone.Parent is null ) Visit( bone );

		hierarchy = order.ToArray();
		animatedPose = new Transform[lastPhysicsPose.Length];
		sinceGetUp = 0;
		getUpHook = Scene.AddHook( GameObjectSystem.Stage.UpdateBones, 1, BlendGetUpPose, nameof( RabbitGuard ), "Blend get-up pose" );
	}

	void BlendGetUpPose()
	{
		float weight = sinceGetUp; // Overgrowth's standing wake uses a fade speed of 1/s.
		if ( weight >= 1f || !Renderer.IsValid() || Health <= 0 )
		{
			EndGetUpBlend();
			return;
		}

		var sceneModel = Renderer.SceneModel;
		for ( int i = 0; i < animatedPose.Length; i++ )
			animatedPose[i] = sceneModel.GetBoneWorldTransform( i );

		// Convert children before their parents, while both poses are still in world space.
		for ( int j = hierarchy.Length - 1; j >= 0; j-- )
		{
			var (i, parent) = hierarchy[j];
			if ( parent < 0 ) continue;
			lastPhysicsPose[i] = lastPhysicsPose[parent].ToLocal( lastPhysicsPose[i] );
			animatedPose[i] = animatedPose[parent].ToLocal( animatedPose[i] );
		}
		foreach ( var (i, parent) in hierarchy )
		{
			var blended = global::Transform.Lerp( lastPhysicsPose[i], animatedPose[i], weight, false );
			lastPhysicsPose[i] = parent < 0 ? blended : lastPhysicsPose[parent].ToWorld( blended );
			sceneModel.SetBoneWorldTransform( i, lastPhysicsPose[i] );
		}
	}

	void EndGetUpBlend()
	{
		getUpHook?.Dispose();
		getUpHook = null;
		lastPhysicsPose = null;
		animatedPose = null;
		hierarchy = null;
	}

	protected override void OnDisabled()
	{
		EndGetUpBlend();
		base.OnDisabled();
	}
}
