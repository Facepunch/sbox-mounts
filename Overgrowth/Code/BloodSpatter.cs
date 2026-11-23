using System;

namespace Overgrowth;

internal sealed class BloodSpatter : Component
{
	Decal decal, wetLayer;
	Color bloodColor;
	TimeSince age;

	// Zero roughness, nonmetal, full ambient occlusion. The decal mask controls coverage.
	internal static DecalDefinition WetSurface( Texture mask ) => new()
	{
		ColorTexture = mask,
		ColorMix = 0f,
		RoughMetalOcclusionTexture = Texture.Create( 1, 1 ).WithData( new byte[] { 0, 0, 255, 255 } ).Finish(),
		Width = 1f,
		Height = 1f
	};

	internal void Initialize( Decal target, DecalDefinition wetSurface, Color color )
	{
		decal = target;
		bloodColor = color;
		// Share the footprint and roll so roughness and albedo use the same texels.
		target.Rotation = Random.Shared.Float( 0f, 360f );
		var layer = GameObject.AddComponent<Decal>();
		layer.Decals = [wetSurface];
		layer.Size = target.Size;
		layer.Depth = target.Depth;
		layer.Rotation = target.Rotation;
		target.OnComponentDestroy += () => { if ( layer.IsValid() ) layer.Destroy(); };
		wetLayer = layer;
		age = 0;
		SetWetness( 1f );
	}

	protected override void OnUpdate()
	{
		if ( decal.IsValid() )
		{
			float wetness = Math.Clamp( 1f - age / 10f, 0f, 1f );
			SetWetness( wetness );
			if ( wetness > 0f ) return;
		}
		Destroy();
	}

	void SetWetness( float wetness )
	{
		// decals.glsl scales the albedo while preserving the texture's coverage.
		if ( wetLayer.IsValid() ) wetLayer.ColorTint = Color.White.WithAlpha( bloodColor.a * (0.5f + 0.5f * wetness) );
		decal.ColorTint = (bloodColor * (0.15f + 0.25f * wetness)).WithAlpha( bloodColor.a );
	}
}
