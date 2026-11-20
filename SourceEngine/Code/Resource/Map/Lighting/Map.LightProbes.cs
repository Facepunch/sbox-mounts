using System;

namespace Source1;

partial class MapLoader
{
	private BspProbeTextures _probeTextures;
	internal int ProbeTextureCount => _probeTextures.TextureCount;
	internal long ProbeTextureBytes => _probeTextures.ByteCount;

	private void BuildLightProbes( BspFile bsp, GameObject world, bool skybox = false )
	{
		_probeTextures = new BspProbeTextures( ResourcePrefix );
		new MapLightingBuilder( bsp, this, _lighting ).BuildLightProbes( world, skybox );
		_probeTextures.Finish();
	}

	internal BspProbeTextures.AtlasHandle CreateProbeTexture( Vector3Int counts, int depth, ImageFormat format, ReadOnlySpan<byte> data ) =>
		_probeTextures.GetOrCreate( counts, depth, format, data );

	internal Texture GetProbeTexture( BspProbeTextures.AtlasHandle handle ) => _probeTextures.GetTexture( handle );
	internal float GetProbeAtlasZScale( BspProbeTextures.AtlasHandle handle ) => _probeTextures.GetAtlasZScale( handle );
	internal float GetProbeAtlasZOffset( BspProbeTextures.AtlasHandle handle ) => _probeTextures.GetAtlasZOffset( handle );
}
