using IdTech3.Formats;
using Sandbox.Rendering;

namespace IdTech3;

/// <summary>
/// RTCW / ET sky portals. A props_skyportal (misc_skyportal in ET) sits in a sealed room of the same BSP, and the
/// game draws that room from the portal's origin, turned with the player's view but not moved with it, wherever
/// the world shows a sky surface. Here the room becomes a separate "_skybox" scene drawn by a Skybox3D.
/// </summary>
partial class MapLoader
{
	/// <summary>
	/// Main-world units per sky portal unit. The game's portal camera doesn't move with the player at all, so the
	/// skybox is scaled up far enough that crossing a whole map moves its camera by a unit or two.
	/// </summary>
	const float SkyPortalScale = 4096;

	sealed class SkyPortal
	{
		public Entity Entity;
		/// <summary>Surfaces of the portal room: everything in the PVS of the portal's cluster.</summary>
		public HashSet<int> Surfaces;
	}

	SkyPortal FindSkyPortal()
	{
		if ( !IsQuakeStyle ) return null;

		var entity = _bsp.Entities.FirstOrDefault( e => e.ClassName is "props_skyportal" or "misc_skyportal" );
		if ( entity is null ) return null;

		var surfaces = _bsp.SurfacesVisibleFrom( entity.Origin );
		if ( surfaces is null || surfaces.Count == 0 )
		{
			Log.Warning( $"{_path}: sky portal has no visibility data; drawing the sky without it" );
			return null;
		}

		// the room must be sealed off from the playable map, or hiding it would hide the map too
		foreach ( var e in _bsp.Entities )
		{
			if ( e.ClassName is null || !IsSpawnClass( e.ClassName ) ) continue;
			int leaf = _bsp.LeafAt( e.Origin );
			if ( leaf < 0 ) continue;
			foreach ( int si in LeafSurfaces( leaf ) )
			{
				if ( !surfaces.Contains( si ) ) continue;
				Log.Warning( $"{_path}: sky portal can see the playable map; drawing the sky without it" );
				return null;
			}
		}

		return new SkyPortal { Entity = entity, Surfaces = surfaces };
	}

	IEnumerable<int> LeafSurfaces( int leaf )
	{
		var l = _bsp.Leaves[leaf];
		for ( int i = 0; i < l.NumLeafSurfaces; i++ )
		{
			int ls = l.FirstLeafSurface + i;
			if ( ls >= 0 && ls < _bsp.LeafSurfaces.Length ) yield return _bsp.LeafSurfaces[ls];
		}
	}

	bool IsSkySurface( int si )
	{
		var s = _bsp.Surfaces[si];
		if ( s.Shader < 0 || s.Shader >= _bsp.Shaders.Length ) return false;
		var shader = _bsp.Shaders[s.Shader];
		return (shader.SurfaceFlags & BspFile.SurfSky) != 0 || _game.Shaders.Find( shader.Name )?.IsSky == true;
	}

	/// <summary>The "_skybox" scene: just the portal room, without collision.</summary>
	void BuildSkyPortalScene( SkyPortal portal )
	{
		if ( portal is null ) return;

		BuildLightmaps();

		var room = new GameObject( true, "sky portal" ) { IsStatic = true };
		room.Tags.Add( "skybox" );
		var model = BuildBspModel( 0, Vector3.Zero, WorldModelName, portal.Surfaces.Contains, collision: false );
		if ( model is not null )
			room.AddComponent<ModelRenderer>().Model = model;
	}

	/// <summary>Skybox3D in the main map drawing the "_skybox" scene from the portal's origin.</summary>
	void BuildSkyPortal( GameObject world, SkyPortal portal )
	{
		var path = $"mount://{_game.Ident}/{_game.Dir}/{_path}_skybox.scene";
		var scene = SceneFile.Load( path );
		if ( scene is null )
		{
			Log.Warning( $"{_path}: could not load sky portal scene '{path}'" );
			return;
		}

		var origin =  portal.Entity.Origin ;
		var go = new GameObject( false, "sky portal" )
		{
			Parent = world,
			LocalPosition = -origin * SkyPortalScale,
			LocalScale = new Vector3( SkyPortalScale ),
		};

		var skybox = go.AddComponent<Skybox3D>();
		skybox.FogOverride = CreateSkyPortalFog( go, portal.Entity );
		skybox.SkyboxScene = scene;
		go.Enabled = true;
	}

	/// <summary>
	/// The portal's linear fog (fogcolor, fognear, fogfar, in portal units). The Fog component stays disabled so it
	/// only fogs the skybox.
	/// </summary>
	static Fog CreateSkyPortalFog( GameObject parent, Entity e )
	{
		if ( !e.Has( "fogcolor" ) || e.Float( "fogfar" ) <= 0 ) return null;

		var go = new GameObject( false, "sky portal fog" ) { Parent = parent };
		var fog = go.AddComponent<Fog>();
		fog.Enabled = false;
		fog.FogEnabled = true;
		fog.Mode = FogMode.Distance;
		fog.DistanceMetric = FogDistanceMetric.ViewDepth;

		// authored in main-world units, which are SkyPortalScale portal units
		float near = MathF.Max( 0, e.Float( "fognear" ) );
		float far = MathF.Max( near + 1, e.Float( "fogfar" ) );
		fog.DistanceRange = new Vector2( near, far ) * SkyPortalScale;
		fog.FalloffExponent = 1;
		fog.MaximumOpacity = 1;

		var c = e.Vector( "fogcolor" );
		fog.ColorSource = FogColorSource.Constant;
		fog.Color = new Color32( ToByte( c.x ), ToByte( c.y ), ToByte( c.z ) ).ToColor( true );
		fog.SunLightingEnabled = false;
		fog.SkyAffect = 0;
		go.Enabled = true;
		return fog;
	}

	static byte ToByte( float v ) => (byte)Math.Clamp( (int)MathF.Round( v * 255 ), 0, 255 );
}
