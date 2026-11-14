using Sandbox.Mounting;
using System.IO;
using GoldSrc.Bsp;

namespace GoldSrc;

/// <summary>
/// Turns a GoldSrc BSP (Half-Life and friends) into a scene: lightmapped world geometry,
/// brush entities, player starts and lights.
/// </summary>
public partial class MapLoader( string filePath ) : SceneLoader<GameMount>
{
	private readonly string FilePath = filePath;
	private LightStyles _lightStyles;

	protected override void BuildScene()
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
	private string WorldModelName => $"goldsrcmap/{Path}".Replace( '\\', '/' );

	private string BrushModelName( int modelIndex ) => $"{WorldModelName}*{modelIndex}";
}
