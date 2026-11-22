using Sandbox.Mounting;
using GoldSrc.Bsp;

namespace GoldSrc.Maps;

/// <summary>
/// Turns a GoldSrc BSP (Half-Life and friends) into a scene: lightmapped world geometry,
/// brush entities, player starts and lights.
/// </summary>
internal sealed class MapLoader( string filePath ) : SceneLoader<GameMount>
{
	// The serialized scene refers to generated resources by name. Keep them alive for its lifetime.
	private MapBuilder builder;

	protected override Texture GetThumbnail()
	{
		var map = System.IO.Path.GetFileNameWithoutExtension( filePath );
		var overview = Host.FindFile( $"overviews/{map}.bmp" ) ?? Host.FindFile( $"overviews/{map}.tga" );
		if ( overview is null ) return null;
		using var bitmap = Bitmap.CreateFromBytes( Host.FileSystem.ReadAllBytes( overview ).ToArray() );
		return bitmap?.ToTexture();
	}

	protected override void BuildScene()
	{
		builder = new MapBuilder( Host, filePath, Path );
		builder.Build();
	}

	protected override void OnSceneUnloaded() => builder = null;
}

internal sealed partial class MapBuilder( GameMount host, string filePath, string resourcePath )
{
	private GameMount Host => host;
	private string FilePath => filePath;
	private LightStyles _lightStyles;
	private MapState _mapState;

	internal void Build()
	{
		using var stream = Host.FileSystem.OpenRead( FilePath );
		var bsp = new BspFile( stream );

		BuildTextures( bsp );
		CreateSurfaces( bsp );

		var geometry = CreateGeometry( bsp, bsp.Models[0], WorldModelName, aggregate: true );
		if ( geometry.IsEmpty )
			return;

		var world = new GameObject( true, "worldspawn" ) { IsStatic = true };
		_lightStyles = world.AddComponent<LightStyles>();
		_mapState = world.AddComponent<MapState>();
		AddRenderers( world, geometry, Color.White );
		AddCollider( world, geometry.Physics, true );
		_decalReceivers.Add( (bsp.Models[0], world, Vector3.Zero) );

		SpawnSkyBox( bsp );
		SpawnEntities( bsp );
		CreateStaticRenderers( world );
		SpawnLightProbes( bsp );
	}

	/// <summary>
	/// Stable prefix for the map's generated render models and physics resources.
	/// </summary>
	private string WorldModelName => $"goldsrcmap/{resourcePath}".Replace( '\\', '/' );

	private string BrushModelName( int modelIndex ) => $"{WorldModelName}*{modelIndex}";
}
