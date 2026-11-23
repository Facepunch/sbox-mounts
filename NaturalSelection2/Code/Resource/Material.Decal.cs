using Sandbox.Mounting;
using System;

namespace NS2;

public partial class MaterialLoader
{
	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = ResourceType.GameResource;
		return name == "projected.decal" ? new ProjectedDecalLoader() : null;
	}

	sealed class ProjectedDecalLoader : ResourceLoader<GameMount>
	{
		protected override object Load()
		{
			var material = (MaterialLoader)Parent;
			var properties = Parse( Host.ReadAllBytes( material.FilePath ) );
			var shader = System.IO.Path.GetFileNameWithoutExtension( Find( properties, "shader" ).String )?.ToLowerInvariant();
			var wet = shader is "wetmap" or "wetmapdecal";
			var lettering = shader == "name_decals";
			var emissive = ReadTexture( "emissiveMap" );
			var color = ReadTexture( "albedoMap" );
			var mask = ReadTexture( wet ? "wetnessMap" : "opacityMap" );
			var definition = new DecalDefinition { Width = 1, Height = 1 };

			if ( wet && mask is null || !wet && color is null && emissive is null )
				return definition;

			if ( shader == "emissive" )
				definition.ColorTexture = emissive;
			else
				PackTextures( definition, color, mask, ReadTexture( "specularMap" ), shader );
			definition.NormalTexture = ReadTexture( "normalMap" );
			definition.EmissiveTexture = emissive;
			definition.EmissionEnergy = Number( "emissiveMod", 1 );
			if ( wet )
			{
				definition.ColorMix = 0;
				definition.Tint = Color.White.WithAlpha( Number( "wetnessMult", 1 ) );
				definition.RoughMetalOcclusionTexture = Texture.Create( 1, 1 ).WithData<Color32>( [new Color32( 0, 0, 255 )] ).Finish();
			}
			else if ( lettering && Find( properties, "decalColor" ) is { Key: not null, Vector: var tint } )
			{
				definition.Tint = new Color( tint.x, tint.y, tint.z );
			}
			else if ( shader == "emissive" )
			{
				definition.ColorMix = 0;
			}
			return definition;

			float Number( string key, float fallback ) => Find( properties, key ) is { Key: not null } value ? value.Number : fallback;

			Texture ReadTexture( string key )
			{
				if ( !GameMount.TryResolve( Host.FileSystem, Find( properties, key ).String, out var path ) ) return null;
				var texture = Texture.Load( $"mount://{Host.Ident}/{path}.vtex", false );
				return texture is { IsError: false } ? texture : null;
			}
		}

		// Match the gloss and metalness conversion in ns2.hlsl.
		static readonly byte[] Roughness = Enumerable.Range( 0, 256 )
			.Select( g => (byte)MathF.Round( 255 * MathF.Pow( 2 / (g * (256f / 255f) + 2), 0.25f ) ) ).ToArray();

		static byte Metalness( byte diffuse, float specular )
		{
			static float Linear( float v ) => v * (v * (v * 0.305306f + 0.682171f) + 0.012523f);
			float s = Linear( specular / 255 );
			if ( s <= 0.04f ) return 0;
			float b = Linear( diffuse / 255f ) * (1 - s) / 0.96f + s - 0.08f;
			return (byte)Math.Clamp( MathF.Round( 255 * (MathF.Sqrt( b * b + 0.16f * (s - 0.04f) ) - b) / 0.08f ), 0, 255 );
		}

		// Spark stores opacity separately from albedo. The projector reads both from a single RGBA texture.
		static void PackTextures( DecalDefinition definition, Texture color, Texture mask, Texture specular, string shader )
		{
			if ( color is null && mask is null ) return;
			var lettering = shader == "name_decals";
			var level = shader is "level" or "level_alpha" or "level_emissive";
			var spec = specular?.GetPixels();
			var rgb = color?.GetPixels();
			var alpha = mask?.GetPixels();
			var width = Math.Max( color?.Width ?? 0, mask?.Width ?? 0 );
			var height = Math.Max( color?.Height ?? 0, mask?.Height ?? 0 );
			var pixels = new Color32[width * height];
			var rmo = spec is not null || level || lettering ? new Color32[pixels.Length] : null;
			for ( var y = 0; y < height; y++ )
			{
				var rgbRow = color is null ? 0 : (y * color.Height / height) * color.Width;
				var specRow = specular is null ? 0 : (y * specular.Height / height) * specular.Width;
				var maskRow = mask is null ? 0 : (y * mask.Height / height) * mask.Width;
				for ( var x = 0; x < width; x++ )
				{
					var c = rgb is null ? Color32.White : rgb[rgbRow + x * color.Width / width];
					if ( rmo is not null )
					{
						var diffuse = lettering ? c.r : Math.Max( c.r, Math.Max( c.g, c.b ) );
						float shine = lettering ? c.g * (c.b / 255f + 0.2f) * 0.3f : Math.Min( 255, diffuse * c.a * (20f / 255f) );
						byte gloss = 20;
						if ( spec is not null )
						{
							var s = spec[specRow + x * specular.Width / width];
							shine = Math.Max( s.r, Math.Max( s.g, s.b ) );
							gloss = s.a;
						}
						rmo[y * width + x] = new Color32( Roughness[gloss], Metalness( diffuse, shine ), 255 );
					}
					c.a = alpha is null ? (byte)255 : alpha[maskRow + x * mask.Width / width].r;
					pixels[y * width + x] = lettering ? new Color32( c.r, c.r, c.r, c.g ) : c;
				}
			}
			definition.ColorTexture = MakeTexture( pixels );
			if ( rmo is not null ) definition.RoughMetalOcclusionTexture = MakeTexture( rmo );

			Texture MakeTexture( Color32[] data ) => Texture.Create( width, height ).WithData<Color32>( data ).WithMips().Finish();
		}
	}
}
