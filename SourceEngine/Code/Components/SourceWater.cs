using System;
using Sandbox.Rendering;

namespace Source1;

[Title( "Source Water" ), Category( "Source Engine" )]
public sealed class SourceWater : Component, Component.ExecuteInEditor
{
	[Property] public SourceWaterMap Map { get; set; }
	[Property] public ModelRenderer Renderer { get; set; }
	[Property] public Material Material { get; set; }
	[Property] public Fog Atmosphere { get; set; }
	[Property] public float Height { get; set; }
	[Property] public bool AboveWater { get; set; } = true;
	[Property] public bool Reflect { get; set; } = true;
	[Property] public bool Refract { get; set; } = true;
	[Property] public bool ReflectEntities { get; set; }
	[Property] public bool ReflectSky { get; set; } = true;
	[Property] public bool ReflectSkybox3D { get; set; }
	[Property] public float CheapWaterEndDistance { get; set; } = 0.1f;

	readonly List<CameraComponent> _cameras = [];
	CommandList[] _captures;
	int _captureIndex;
	TagSet _reflectionTags;
	TagSet _reflectionExclusions;
	readonly TagSet _refractionExclusions = new( ["source1_water"] );
	readonly TagSet _underwaterExclusions = new( ["source1_water", "skybox", "source1_skybox2d", "source1_skybox3d"] );
	int _filterKey = -1;
	CameraComponent _refractionCamera;
	Model _surfaceModel;
	bool _opaqueSurface;

	protected override void OnEnabled()
	{
		if ( Application.IsDedicatedServer ) return;
		_captures = [new( "Source water captures" ), new( "Source water captures" )];
	}

	protected override void OnPreRender()
	{
		if ( _captures is null || !Renderer.IsValid() || Material is not { IsValid: true } )
			return;

		if ( Renderer.Model != _surfaceModel )
		{
			_surfaceModel = Renderer.Model;
			_opaqueSurface = _surfaceModel.IsValid() && _surfaceModel.Materials.All( m =>
				m.GetVector4( "g_flSourceAlpha" ).x >= 1 && m.GetVector4( "g_flSourceWaterBlend" ).x >= 1 &&
				m.GetFeature( "F_SOURCE_ALPHA_SINE" ) == 0 && m.GetFeature( "F_SOURCE_PROXIES" ) == 0 );
		}

		SetOpaquePass( PrepareCaptures() && _opaqueSurface );
	}

	bool PrepareCaptures()
	{
		CameraComponent camera = null;
		if ( Scene.IsEditor )
		{
			_cameras.Clear();
			Scene.GetAll( _cameras );
			camera = _cameras.FirstOrDefault( x => (x.GameObject.Flags & GameObjectFlags.EditorOnly) != 0 );
			_cameras.Clear();
		}
		camera ??= Scene.Camera;
		var commands = _captures[_captureIndex ^= 1];
		commands.Reset();
		commands.Attributes.Set( "SourceWaterHasReflection", false );
		commands.Attributes.Set( "SourceWaterHasRefraction", false );
		Renderer.ExecuteBefore = commands;
		if ( !camera.IsValid() || camera.Orthographic || camera.CustomProjectionMatrix is not null )
			return false;
		if ( !camera.GetFrustum().IsInside( Renderer.Bounds, partially: true ) )
			return false;
		if ( Map.IsValid() && !Map.CanSeeWater( camera.WorldPosition ) )
			return false;
		var origin = WorldTransform.PointToWorld( new Vector3( 0, 0, Height ) );
		var normal = WorldRotation.Up * (WorldScale.z < 0 ? -1 : 1);
		if ( AboveWater != (Vector3.Dot( camera.WorldPosition - origin, normal ) >= 0) )
			return false;
		if ( !Reflect )
		{
			var eye = WorldTransform.PointToLocal( camera.WorldPosition );
			var bounds = Renderer.Model.Bounds;
			var nearest = new Vector3( Math.Clamp( eye.x, bounds.Mins.x, bounds.Maxs.x ),
				Math.Clamp( eye.y, bounds.Mins.y, bounds.Maxs.y ), Height );
			if ( camera.WorldPosition.Distance( WorldTransform.PointToWorld( nearest ) ) >= CheapWaterEndDistance )
				return false;
		}
		var atmosphere = GetAtmosphere();
		int filterKey = (ReflectEntities ? 1 : 0) | (ReflectSky ? 2 : 0) | (ReflectSkybox3D ? 4 : 0);
		if ( filterKey != _filterKey )
		{
			_filterKey = filterKey;
			_reflectionTags = new TagSet();
			if ( !ReflectEntities )
			{
				_reflectionTags.Add( "world" );
				if ( ReflectSky ) _reflectionTags.Add( "skybox" );
				if ( ReflectSkybox3D ) _reflectionTags.Add( "source1_skybox3d" );
			}
			_reflectionExclusions = new TagSet( ["source1_water"] );
			if ( !ReflectEntities ) _reflectionExclusions.Add( "source1_entity" );
			if ( !ReflectSky ) _reflectionExclusions.Add( "source1_skybox2d" );
			if ( !ReflectSkybox3D ) _reflectionExclusions.Add( "source1_skybox3d" );
		}

		if ( Reflect && AboveWater )
		{
			var target = commands.GetRenderTarget( "Source water reflection", 1, ImageFormat.RGBA16161616F, ImageFormat.None );
			var view = commands.DrawReflection( camera, new Plane( origin, normal ), target, new ReflectionSetup
			{
				// CBaseWorldView::PushView clips reflections two units below the water.
				ClipOffset = 2,
				ViewSetup = new ViewSetup
				{
					EnablePostprocessing = false,
					EnableUI = false,
					EnableDebugOverlays = false,
					RenderTags = _reflectionTags,
					ExcludeTags = _reflectionExclusions,
					Fog = atmosphere,
					ClearColor = Color.Black,
					ViewHash = HashCode.Combine( Id, 0 )
				}
			} );
			commands.Attributes.Set( "SourceWaterReflection", target.ColorTexture );
			commands.Attributes.Set( "SourceWaterReflectionViewport", view.Viewport );
			commands.Attributes.Set( "SourceWaterHasReflection", view.IsValid );
		}

		if ( Refract )
		{
			var target = commands.GetRenderTarget( "Source water refraction", 1, ImageFormat.RGBA16161616F, ImageFormat.None );
			var fogColor = Material.GetColor( "g_vSourceFogColor" ).WithAlpha( 1 );
			var refractionCamera = camera;
			if ( AboveWater )
			{
				if ( !_refractionCamera.IsValid() )
				{
					var capture = new GameObject( GameObject, true, "Water refraction camera" );
					capture.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
					_refractionCamera = capture.AddComponent<CameraComponent>( false );
					_refractionCamera.IsMainCamera = false;
					capture.AddComponent<WaterRefractionFog>().Surface = this;
				}
				refractionCamera = _refractionCamera;
				refractionCamera.ZNear = camera.ZNear;
				refractionCamera.ZFar = camera.ZFar;
				refractionCamera.RenderExcludeTags.SetFrom( camera.RenderExcludeTags );
				refractionCamera.RenderTags.SetFrom( camera.RenderTags );
			}
			var view = commands.DrawRefraction( refractionCamera, new Plane( origin, AboveWater ? -normal : normal ), target, new RefractionSetup
			{
				// DF_FUDGE_UP keeps two units above the surface for the underwater
				// capture; the view from below keeps two units below it instead.
				ClipOffset = -2,
				RenderBehind = true,
				ViewSetup = new ViewSetup
				{
					EnablePostprocessing = false,
					EnableUI = false,
					EnableDebugOverlays = false,
					// CAboveWaterView::CRefractionView draws only the underwater world,
					// while the underwater camera's refraction view includes the sky.
					ExcludeTags = AboveWater ? _underwaterExclusions : _refractionExclusions,
					Fog = AboveWater ? new FogSetup { Enabled = false } : atmosphere,
					ClearColor = AboveWater ? fogColor : Color.Black,
					ViewHash = HashCode.Combine( Id, 1 )
				}
			} );
			commands.Attributes.Set( "SourceWaterRefraction", target.ColorTexture );
			commands.Attributes.Set( "SourceWaterRefractionViewport", view.Viewport );
			commands.Attributes.Set( "SourceWaterHasRefraction", view.IsValid );
			return true;
		}
		return false;
	}

	// Captured water writes surface depth and can draw before translucent spray and smoke.
	void SetOpaquePass( bool opaque )
	{
		var surface = Renderer.IsValid() ? Renderer.SceneObject : null;
		if ( !surface.IsValid() || surface.Flags.IsOpaque == opaque && surface.Flags.IsTranslucent != opaque ) return;
		surface.Flags.IsOpaque = opaque;
		surface.Flags.IsTranslucent = !opaque;
	}

	FogSetup GetAtmosphere()
	{
		if ( !Atmosphere.IsValid() || !Atmosphere.Active )
			return new FogSetup { Enabled = false };

		return new FogSetup
		{
			Enabled = Atmosphere.FogEnabled,
			DistanceEnabled = Atmosphere.DistanceEnabled,
			Mode = FogMode.Distance,
			DistanceMetric = Atmosphere.DistanceMetric,
			Color = Atmosphere.Color.WithAlpha( 1 ),
			DirectionalColorEnabled = Atmosphere.DirectionalColorEnabled,
			SecondaryColor = Atmosphere.SecondaryColor.WithAlpha( 1 ),
			ColorDirection = Atmosphere.WorldRotation * Atmosphere.ColorDirection,
			ColorScale = Atmosphere.ColorScale,
			StartDistance = Atmosphere.DistanceRange.x,
			EndDistance = Atmosphere.DistanceRange.y,
			FalloffExponent = Atmosphere.FalloffExponent,
			MaximumOpacity = Atmosphere.MaximumOpacity,
			SunLightingEnabled = false,
			SkyAffect = Atmosphere.SkyAffect,
			MaxDistance = Atmosphere.MaxDistance
		};
	}

	protected override void OnDisabled()
	{
		if ( _refractionCamera.IsValid() ) _refractionCamera.GameObject.Destroy();
		SetOpaquePass( false );
		if ( Renderer.IsValid() ) Renderer.ExecuteBefore = null;
		_captures = null;
		_cameras.Clear();
	}
}

[Hide]
public sealed class WaterRefractionFog : BasePostProcess
{
	internal SourceWater Surface { get; set; }
	public override bool IsCameraEffect => true;

	public override void Render()
	{
		if ( !Surface.IsValid() || Surface.Material is not { IsValid: true } material ) return;
		var normal = Surface.WorldRotation.Up * (Surface.WorldScale.z < 0 ? -1 : 1);
		var point = Surface.WorldTransform.PointToWorld( new Vector3( 0, 0, Surface.Height ) );
		TargetCamera.Attributes.Set( "SourceWaterFogPlane", new Vector4( normal, Vector3.Dot( normal, point ) ) );
		float start = material.GetVector4( "g_flSourceFogStart" ).x;
		float end = material.GetVector4( "g_flSourceFogEnd" ).x;
		var color = material.GetVector4( "g_vSourceFogColor" );
		TargetCamera.Attributes.Set( "SourceWaterFog", new Vector4( color.x, color.y, color.z, 1 / Math.Max( end - start, 0.001f ) ) );
	}
}
