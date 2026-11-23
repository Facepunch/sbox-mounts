using System;

namespace Overgrowth;

internal sealed class BloodSpatter : Component
{
	Decal decal;
	Color bloodColor;
	TimeSince age;

	internal static void Apply( Decal target, float width, Color color )
	{
		if ( color == RabbitGuard.DefaultBloodColor && Surface.FindByName( "flesh" )?.Splatter.Decals is { Count: > 0 } variants )
		{
			var variant = variants[Random.Shared.Next( variants.Count )];
			target.Decals = [variant];
			target.Size = new Vector2( width / variant.Width, width / variant.Height );
			return;
		}

		target.GameObject.AddComponent<BloodSpatter>().Initialize( target, color );
	}

	void Initialize( Decal target, Color color )
	{
		decal = target;
		bloodColor = color;
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
		decal.ColorTint = (bloodColor * (0.15f + 0.25f * wetness)).WithAlpha( bloodColor.a );
	}
}
