using System;
using Sandbox.Rendering;
using Sandbox.Volumes;

namespace Source1;

partial class MapLoader
{
	private readonly record struct WaterCaptureSettings( bool Above, bool Reflect, bool Refract, bool Entities, bool Sky, bool Skybox3D, Color ClearColor );
	private readonly List<(Model Model, Material Material, float Height, WaterCaptureSettings Capture, bool Skybox)> _waterModels = [];
	private void BuildWater( BspFile bsp, GameObject geometry, Fog atmosphere = null, bool skybox = false )
	{
		var map = BuildWaterVolumes( bsp, geometry, skybox );
		var lod = bsp.Entities.FirstOrDefault( entity => entity.Get( "classname" ) == "water_lod_control" );
		foreach ( var water in _waterModels )
		{
			if ( water.Skybox != skybox )
				continue;
			var child = new GameObject( false, water.Capture.Above ? "water" : "water underside" )
			{
				Parent = geometry,
				IsStatic = true
			};
			child.Tags.Add( "world" );
			child.Tags.Add( "source1_water" );
			var renderer = child.AddComponent<ModelRenderer>();
			renderer.Model = water.Model;
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
			// CSkyboxView draws water with the sky world, without reflection/refraction views.
			// D_SKYBOX selects the cubemap water path in the shader, in sky-camera units.
			if ( skybox )
			{
				child.Enabled = true;
				continue;
			}
			var surface = child.AddComponent<SourceWater>();
			surface.Renderer = renderer;
			surface.Map = map;
			surface.Material = water.Material;
			surface.Height = water.Height;
			surface.AboveWater = water.Capture.Above;
			surface.Atmosphere = atmosphere;
			surface.Reflect = water.Capture.Reflect;
			surface.Refract = water.Capture.Refract;
			surface.ReflectEntities = water.Capture.Entities;
			surface.ReflectSky = water.Capture.Sky;
			surface.ReflectSkybox3D = water.Capture.Skybox3D;
			surface.CheapWaterEndDistance = lod?.Number( "cheapwaterenddistance", 2000 ) ?? 0.1f;
			child.Enabled = true;
		}
	}

	private void BuildWaterModels( Dictionary<(int Texture, float Height), MeshGroup> groups, bool skybox )
	{
		foreach ( var batch in groups.GroupBy( entry => (entry.Key.Height, Capture: GetWaterCaptureSettings( entry.Value.Material )) ) )
		{
			var first = batch.First();
			var builder = Model.Builder;
			var bounds = new BBox( first.Value.Vertices[0].Position, first.Value.Vertices[0].Position );
			foreach ( var (_, group) in batch )
			{
				var (meshBounds, _) = BuildWorldMesh( builder, group.Material, [group] );
				bounds = bounds.AddBBox( meshBounds );
			}
			var model = CreateWorldModel( builder.WithViewBounds( bounds ), $"{(skybox ? "skybox_" : "")}water_{first.Key.Texture}_{_waterModels.Count}.vmdl" );
			_waterModels.Add( (model, first.Value.Material, batch.Key.Height, batch.Key.Capture, skybox) );
		}
	}

	WaterCaptureSettings GetWaterCaptureSettings( Material material )
	{
		var parameters = WaterParameters( material );
		var cheap = parameters.Boolean( "$forcecheap" );
		var above = parameters.Boolean( "$abovewater", true );
		var reflect = !cheap && parameters.Boolean( "$forceexpensive", true )
			&& !string.IsNullOrWhiteSpace( parameters.String( "$reflecttexture" ) );
		var refract = !cheap && !string.IsNullOrWhiteSpace( parameters.String( "$refracttexture" ) );
		return new( above, reflect, refract, parameters.Boolean( "$reflectentities" ) || parameters.Contains( "$reflectonlymarkedentities" ), parameters.Boolean( "$reflect2dskybox", true ),
			parameters.Boolean( "$reflect3dskybox" ), above && refract ? material.GetColor( "g_vSourceFogColor" ).WithAlpha( 1 ) : Color.Black );
	}

	VmtParameters WaterParameters( Material material )
	{
		var source = Host.GetMaterialSource( material )
			?? throw new InvalidDataException( $"{FullPath}: water material '{material.Name}' has no Source parameters." );
		return new VmtParameters( VmtFile.SelectParameters( source.Source ), FullPath );
	}

	Material WaterBottomMaterial( Material material )
	{
		var path = WaterParameters( material ).String( "$bottommaterial" );
		if ( string.IsNullOrWhiteSpace( path ) ) return material;
		var root = Host.GetMaterialSource( material ).ContentRoot;
		path = VmtFile.NormalizePath( path, ".vmt" );
		if ( Host.GetContentPath( path, root ) is null )
		{
			Log.Warning( $"{FullPath}: water bottom material '{path}' is missing; retaining the surface material." );
			return material;
		}
		return Host.LoadContentMaterial( path, root );
	}

	SourceWaterMap BuildWaterVolumes( BspFile bsp, GameObject geometry, bool skybox )
	{
		var (nodes, root) = bsp.BuildWaterTree( skybox );
		if ( root is -1 or SourceWaterMap.NoVisibleWater ) return null;
		var map = geometry.AddComponent<SourceWaterMap>();
		map.Nodes = nodes;
		map.Root = root;
		if ( !skybox && Host.GetSurfaceSound( "Player.AmbientUnderWater" ) is { } ambient )
		{
			var audio = geometry.AddComponent<SourceWaterAudio>();
			audio.Map = map;
			audio.Ambient = ambient;
		}
		var indices = nodes.SelectMany( node => new[] { node.Front, node.Back } ).Append( root )
			.Where( index => index < -1 && index != SourceWaterMap.NoVisibleWater ).Select( index => -index - 2 ).Distinct();
		foreach ( var index in indices )
		{
			var data = bsp.Water[index];
			var material = LoadWorldMaterial( bsp, bsp.TexInfos[data.TexInfo].TexData, -1, Material.Load( "materials/error.vmat" ) );
			if ( Host.GetMaterialSource( material ) is null )
			{
				Log.Warning( $"{FullPath}: water volume {index} has no valid Source material; its fog was not imported." );
				continue;
			}
			material = WaterBottomMaterial( material );
			var child = new GameObject( false, "water fog" ) { Parent = geometry, IsStatic = true };
			var volume = child.AddComponent<SourceWaterVolume>();
			volume.Map = map;
			volume.Water = index;
			volume.Priority = 10;
			volume.BlendDistance = 0;
			volume.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite };
			var fog = child.AddComponent<Fog>();
			fog.FogEnabled = WaterParameters( material ).Boolean( "$fogenable" );
			fog.Mode = FogMode.Distance;
			fog.DistanceMetric = FogDistanceMetric.ViewDepth;
			var start = material.GetVector4( "g_flSourceFogStart" ).x;
			var end = material.GetVector4( "g_flSourceFogEnd" ).x;
			fog.DistanceRange = new Vector2( start, start == end ? start + 1 : end );
			// BlendPixelFog uses a linear factor for height (water) fog, unlike range fog.
			fog.FalloffExponent = 1;
			fog.ColorSource = FogColorSource.Constant;
			fog.Color = material.GetColor( "g_vSourceFogColor" ).WithAlpha( 1 );
			fog.SunLightingEnabled = false;
			fog.SkyAffect = 1;
			var overlayPath = WaterParameters( material ).String( "$underwateroverlay" );
			if ( !string.IsNullOrWhiteSpace( overlayPath ) )
			{
				try
				{
					var overlay = Host.LoadContentMaterial( overlayPath, Host.GetMaterialSource( material ).ContentRoot );
					var source = Host.GetMaterialSource( overlay )
						?? throw new InvalidDataException( $"Underwater overlay '{overlayPath}' has no Source material." );
					child.AddComponent<SourceUnderwater>().Overlay = source.CreateMaterial(
						$"{overlay.Name}_underwater.vmat", screenOverlay: true );
				}
				catch ( Exception e ) when ( e is FileNotFoundException or InvalidDataException or NotSupportedException )
				{
					Log.Warning( $"{FullPath}: underwater overlay '{overlayPath}' was not imported: {e.Message}" );
				}
			}
			child.Enabled = true;
		}
		return map;
	}
}
