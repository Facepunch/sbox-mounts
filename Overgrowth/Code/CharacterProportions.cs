using static System.MathF;

namespace Overgrowth;

public sealed class CharacterProportions : Component
{
	[Property, Sync, Range( 0f, 1f )] public float Fat { get; set; } = 0.5f;
	[Property, Sync, Range( 0f, 1f )] public float Muscle { get; set; } = 0.5f;
	[Property, Sync, Range( 0f, 3f )] public float EarSize { get; set; } = 1f;

	CharacterLoader cachedLoader;
	Model cachedModel;
	Vector3 cachedSettings;
	Vector4[] shapeData;
	Vector4[] uploadedData;
	GpuBuffer<Vector4> shape;

	internal Vector4[] GetShape( Model model )
	{
		var settings = new Vector3( Fat, Muscle, EarSize );
		if ( settings == new Vector3( 0.5f, 0.5f, 1f ) || model is not { IsError: false } ) return null;
		if ( cachedModel == model && cachedSettings == settings && cachedLoader.BodyShapeRig is not null ) return shapeData;
		if ( CharacterLoader.FromModel( model ) is not { } loader ) return null;

		var rig = loader.BodyShapeRig;
		var scales = GetBoneInflation( rig );
		shapeData = new Vector4[scales.Length * 2];
		for ( int i = 0; i < scales.Length; i++ )
		{
			if ( !loader.AttachmentCenters.TryGetValue( i, out var center ) ) continue;
			var bind = rig.BindPose[i];
			shapeData[2 * i] = new Vector4( bind.PointToWorld( center ), scales[i] - 1f );
			shapeData[2 * i + 1] = new Vector4( bind.Rotation.Forward, 0f );
		}
		cachedLoader = loader;
		cachedModel = model;
		cachedSettings = settings;
		return shapeData;
	}

	protected override void OnPreRender()
	{
		var renderer = GetComponent<SkinnedModelRenderer>();
		var data = GetShape( renderer?.Model );
		if ( uploadedData == data ) return;
		if ( data is null || shape?.ElementCount != data.Length ) ClearShape( renderer );
		if ( data is null ) return;
		shape ??= new( data.Length );
		shape.SetData( data );
		renderer.Attributes.Set( "BodyBoneIds", cachedLoader.BodyShapeIndices );
		renderer.Attributes.Set( "BodyBoneShape", shape );
		renderer.Attributes.Set( "BodyProportions", true );
		uploadedData = data;
	}

	protected override void OnDisabled()
	{
		ClearShape( GetComponent<SkinnedModelRenderer>() );
	}

	void ClearShape( SkinnedModelRenderer renderer )
	{
		if ( renderer.IsValid() )
		{
			renderer.Attributes.Set( "BodyProportions", false );
			renderer.Attributes.Set( "BodyBoneIds", (GpuBuffer)null );
			renderer.Attributes.Set( "BodyBoneShape", (GpuBuffer)null );
		}
		shape?.Dispose();
		shape = null;
		uploadedData = null;
	}

	internal void CopyTo( GameObject corpse )
	{
		var copy = corpse.GetOrAddComponent<CharacterProportions>();
		copy.Fat = Fat;
		copy.Muscle = Muscle;
		copy.EarSize = EarSize;
	}

	internal float[] GetBoneInflation( SkeletonData skeleton )
	{
		var scales = new float[skeleton.Bones.Count];
		System.Array.Fill( scales, 1f );
		float fat = Fat * 2f - 1f, muscle = Muscle * 2f - 1f;

		void Set( string chain, int depth, float scale )
		{
			if ( !skeleton.IKBones.TryGetValue( chain, out var tip ) ) return;
			int bone = tip.BoneId;
			for ( int i = 0; i < depth && bone >= 0; ++i ) bone = skeleton.AnimationParents[bone];
			if ( bone >= 0 ) scales[bone] = scale;
		}

		Set( "torso", 2, Max( 0.7f + muscle * 0.5f, 1f + Max( fat * 0.5f, fat ) ) );
		Set( "torso", 1, Max( 0.5f + muscle * 0.2f, 1f + Max( fat * 0.75f, fat ) ) );
		Set( "torso", 0, 1f + Max( Max( 0f, fat ), muscle * 0.3f ) );
		Set( "head", 1, 1f + fat );
		foreach ( var side in new[] { "left", "right" } )
		{
			string ear = side + "ear", leg = side + "_leg", arm = side + "arm";
			if ( skeleton.IKBones.TryGetValue( ear, out var chain ) )
				for ( int i = 0; i < chain.ChainLength; ++i ) Set( ear, i, (EarSize + 1f) * 0.5f );
			Set( leg, 5, 1f + Max( fat, muscle * 0.4f ) );
			Set( leg, 4, Max( muscle, 1f + fat * 0.5f ) );
			Set( leg, 3, 1f + Max( fat * 0.25f, muscle * 0.6f ) );
			Set( leg, 2, 1f + Max( fat * 0.5f, muscle * 0.3f ) );
			Set( arm, 1, Max( 0.6f, 1f + Max( fat * 0.5f, muscle * 0.5f ) ) );
			Set( arm, 2, Max( 0.6f, 1f + Max( fat * 0.5f, muscle * 0.7f ) ) );
			Set( arm, 3, Max( 0.6f, 1f + Max( fat, muscle ) ) );
			Set( arm, 4, Max( 0.6f, 1f + Max( fat, muscle ) ) );
			Set( arm, 5, Max( 0.6f, 1f + Max( fat * 0.5f, muscle ) ) );
		}
		return scales;
	}
}
