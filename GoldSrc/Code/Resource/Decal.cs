using Sandbox.Mounting;
using GoldSrc.Bsp;
using System;
using System.IO;

namespace GoldSrc;

internal sealed class GoldSrcDecalTextureLoader( Wad wad, string name ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var source = Bsp.MipTex.Read( wad.GetLumpData( name ) );
		if ( source is null || !source.HasPixels )
			throw new InvalidDataException( $"{Path}: invalid GoldSrc decal miptex." );
		return Texture.Create( source.Width, source.Height, ImageFormat.RGBA8888_LINEAR )
			.WithData( GoldSrcDecalPixels.Decode( source ) ).WithMips().WithStaticUsage().Finish();
	}
}

internal sealed class GoldSrcDecalLoader( string name ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var texture = Host.GetDecalTexture( name );
		if ( !texture.IsValid || texture.IsError )
			throw new InvalidDataException( $"{Path}: GoldSrc decal texture is invalid." );
		return new DecalDefinition
		{
			ColorTexture = texture,
			Width = texture.Width,
			Height = texture.Height,
			FilterMode = Sandbox.Rendering.FilterMode.Anisotropic
		};
	}
}

internal sealed class GoldSrcDecalPrefabLoader( IReadOnlyList<string> names ) : ResourceLoader<GameMount>
{
	private PrefabFile prefab;

	protected override object Load()
	{
		var definitions = names.Select( Host.GetDecal ).Where( x => x is not null ).ToList();
		if ( definitions.Count == 0 )
			throw new InvalidDataException( $"{Path}: impact decal group has no usable textures." );
		var builder = new PrefabBuilder().WithName( Path );
		using ( builder.Scope() )
		{
			// Keep the template inactive without disabling the serialized prefab root.
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, "GoldSrc impact decal" );
			root.AddComponent<TemporaryEffect>();
			var decal = root.AddComponent<Decal>( false );
			decal.Decals = definitions;
			decal.Transient = true;
			decal.Rotation = 0;
			decal.SortLayer = 1;
			decal.Enabled = true;
		}
		return prefab = builder.Create()
			?? throw new InvalidOperationException( $"{Path}: could not build impact decal prefab." );
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( prefab );
		prefab = null;
		base.Shutdown();
	}
}
