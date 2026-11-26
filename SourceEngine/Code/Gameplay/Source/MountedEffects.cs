using System;

namespace Source1;

/// <summary>Game-side placement of effects converted by the Source mount.</summary>
static class MountedEffects
{
	static string ParticlePath( string name, string mountName ) => $"mount://{mountName}/particles/{Uri.EscapeDataString( name.ToLowerInvariant() )}.prefab";

	/// <summary>Whether the mount converted a PCF system of this name.</summary>
	public static bool Exists( string name, string mountName ) => !string.IsNullOrWhiteSpace( name )
		&& Sandbox.Mounting.Directory.Get( mountName )?.GetByPath( ParticlePath( name, mountName ) ) is not null;

	public static GameObject Prefab( string name, string mountName ) => Exists( name, mountName ) ? GameObject.GetPrefab( ParticlePath( name, mountName ) ) : null;

	public static GameObject Attach( string name, GameObject parent, string mountName, Transform? transform = null )
	{
		var prefab = Prefab( name, mountName );
		if ( !prefab.IsValid() || !parent.IsValid() ) return null;
		var effect = prefab.Clone( new CloneConfig { Parent = parent, Transform = transform ?? Transform.Zero, StartEnabled = false } );
		effect.NetworkMode = NetworkMode.Never;
		foreach ( var renderer in effect.GetComponentsInChildren<Renderer>( true ) ) UseViewModelLayer( renderer );
		effect.Enabled = true;
		return effect;
	}

	public static GameObject Spawn( string name, Transform transform, string mountName )
	{
		var prefab = Prefab( name, mountName );
		if ( !prefab.IsValid() ) return null;
		var effect = prefab.Clone( new CloneConfig { Transform = transform, StartEnabled = false } );
		effect.NetworkMode = NetworkMode.Never;
		effect.Enabled = true;
		return effect;
	}

	/// <summary>The texture behind a Source sprite material, such as sprites/redglow1.</summary>
	public static Texture SpriteTexture( string name, string mountName ) => SpriteTexture( name, mountName, out _ );

	static Texture SpriteTexture( string name, string mountName, out SourceMaterial material )
	{
		var mount = Sandbox.Mounting.Directory.Get( mountName );
		material = mount is SourceGameMount source && mount.GetByPath( $"mount://{mountName}/materials/{name}.vmt.vmat" ) is not null
			? source.GetSourceMaterial( name ) : null;
		var texture = material is null ? name : material.BaseTexturePath;
		if ( string.IsNullOrWhiteSpace( texture ) ) return null;
		var path = $"mount://{mountName}/{VmtFile.NormalizePath( texture, ".vtf" )}.vtex";
		return mount?.GetByPath( path ) is not null ? Texture.Load( path ) : null;
	}

	/// <summary>
	/// An additive billboard standing in for a code-driven Source sprite. Source draws a
	/// sprite at its texture width times its scale; see <see cref="SetSprite"/>.
	/// </summary>
	public static SpriteRenderer AddSprite( GameObject parent, string name, string mountName )
	{
		var texture = SpriteTexture( name, mountName );
		if ( texture is null || !parent.IsValid() ) return null;
		var go = new GameObject( parent, false, name );
		go.NetworkMode = NetworkMode.Never;
		var sprite = go.AddComponent<SpriteRenderer>();
		sprite.Sprite = Sprite.FromTexture( texture );
		sprite.Additive = true;
		// The sprite shader discards texels below the cutoff even when blending, which cut
		// visible discs out of soft-alpha Source sprites such as muzzleflash2.
		sprite.AlphaCutoff = 0;
		sprite.Size = 0;
		UseViewModelLayer( sprite );
		go.Enabled = true;
		return sprite;
	}

	/// <summary>
	/// Effects under a viewmodel draw in its overlay layer, like Source's viewmodel render group:
	/// sorted with the gun and kept out of the world's bloom.
	/// </summary>
	internal static void UseViewModelLayer( Renderer renderer )
	{
		if ( renderer.GameObject.Components.GetInAncestorsOrSelf<ViewModel>( true ) is null ) return;
		renderer.RenderOptions.Game = false;
		renderer.RenderOptions.Overlay = true;
	}

	/// <summary>Applies a Source sprite scale and 0-255 brightness.</summary>
	public static void SetSprite( SpriteRenderer sprite, float scale, float brightness, Color? color = null )
	{
		if ( !sprite.IsValid() ) return;
		var width = sprite.Sprite?.Animations?.FirstOrDefault()?.Frames.FirstOrDefault()?.Texture?.Width ?? 64;
		sprite.Size = width * scale;
		sprite.Color = ((color ?? Color.White) * (brightness / 255f)).WithAlpha( 1 );
		sprite.Enabled = scale > 0 && brightness > 0;
	}

	/// <summary>An additive textured line standing in for a code-driven Source beam.</summary>
	public static LineRenderer AddBeam( GameObject parent, string name, string mountName )
	{
		var texture = SpriteTexture( name, mountName, out var material );
		if ( texture is null || !parent.IsValid() ) return null;
		var go = new GameObject( parent, false, name );
		go.NetworkMode = NetworkMode.Never;
		var beam = go.AddComponent<LineRenderer>();
		beam.UseVectorPoints = true;
		beam.VectorPoints = [Vector3.Zero, Vector3.Zero];
		beam.Additive = true;
		beam.Opaque = false;
		beam.CastShadows = false;
		beam.Face = SceneLineObject.FaceMode.Camera;
		var texturing = LineTexture( texture );
		texturing.Material = material?.GetBeamMaterial( texture );
		beam.Texturing = texturing;
		go.Enabled = true;
		return beam;
	}

	internal static TrailTextureConfig LineTexture( Texture texture )
	{
#pragma warning disable CS0618
		var texturing = TrailTextureConfig.Default;
		texturing.Texture = texture;
		texturing.WorldSpace = false;
		texturing.Scale = 1;
#pragma warning restore CS0618
		return texturing;
	}

	/// <summary>Places a beam. <paramref name="fadeOut"/> is the share of its length, at the end, over which it fades to nothing.</summary>
	public static void SetBeam( LineRenderer beam, Vector3 start, Vector3 end, float width, float brightness, Color? color = null, float fadeOut = 0 )
	{
		if ( !beam.IsValid() ) return;
		beam.VectorPoints[0] = start;
		beam.VectorPoints[1] = end;
		beam.Width = width;
		var tint = ((color ?? Color.White) * (brightness / 255f)).WithAlpha( 1 );
		beam.Color = fadeOut > 0
			? new Gradient( new Gradient.ColorFrame( 0, tint ), new Gradient.ColorFrame( 1 - fadeOut, tint ), new Gradient.ColorFrame( 1, Color.Black.WithAlpha( 0 ) ) )
			: tint;
		beam.Enabled = width > 0 && brightness > 0;
	}
}
