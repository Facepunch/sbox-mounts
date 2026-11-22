using System;
using GoldSrc.Bsp;

namespace GoldSrc.Maps;

partial class MapBuilder
{
	private readonly List<ExplosionEffect> _explosions = [];
	private readonly List<BeamEffect> _beams = [];
	private bool _beamAnchorsWired;
	private readonly Dictionary<string, EffectSprite> _effectSprites = new( StringComparer.OrdinalIgnoreCase );

	private bool SpawnEffectEntity( Entity entity )
	{
		if ( entity.ClassName is "env_beam" or "env_lightning" or "env_laser" )
		{
			SpawnBeam( entity );
			return true;
		}
		if ( entity.ClassName == "env_explosion" )
		{
			SpawnExplosion( entity );
			return true;
		}
		if ( entity.ClassName == "env_sound" )
		{
			var roomObject = new GameObject( true, EntityName( entity ) ) { WorldPosition = entity.Origin };
			var room = roomObject.AddComponent<SoundRoom>();
			room.TargetName = entity.TargetName;
			room.RoomType = entity.GetInt( "roomtype" );
			room.Radius = entity.GetFloat( "radius" );
			RegisterTarget( entity, room, null, null, null, nameof( MapEntity.Kill ) );
			_mapState.GameObject.GetOrAddComponent<RoomAcoustics>();
			return true;
		}
		if ( entity.ClassName == "env_funnel" )
		{
			var funnelObject = new GameObject( true, EntityName( entity ) ) { WorldPosition = entity.Origin };
			var funnel = funnelObject.AddComponent<Funnel>();
			funnel.TargetName = entity.TargetName;
			funnel.Reverse = (entity.SpawnFlags & 1) != 0;
			RegisterTarget( entity, funnel );
			return true;
		}
		var unsupported = entity.ClassName switch
		{
			"env_bubbles" => "brush-volume bubble simulation and the temporary-effect renderer are unavailable",
			"env_shooter" or "gibshooter" => "gib body selection, bounce physics, collision sounds and fade lifetime are unavailable",
			"env_blood" => "blood stream/spray temporary effects and player-relative placement are unavailable",
			_ => null
		};
		if ( unsupported is not null )
		{
			if ( _unsupportedEntities.Add( entity.ClassName ) )
				Log.Warning( $"{FilePath}: '{entity.ClassName}' is not implemented: {unsupported}; it will not forward entity I/O." );
			return false;
		}
		if ( entity.ClassName is "env_spark" or "env_debris" )
		{
			var sparkObject = new GameObject( true, EntityName( entity ) );
			sparkObject.WorldPosition = entity.Origin;
			var spark = sparkObject.AddComponent<SparkEffect>();
			spark.TargetName = entity.TargetName;
			spark.Toggleable = (entity.SpawnFlags & 32) != 0;
			spark.StartOn = (entity.SpawnFlags & 64) != 0;
			spark.MaxDelay = entity.GetFloat( "MaxDelay", 1.5f );
			spark.Sounds = Enumerable.Range( 1, 6 ).Select( index => BuildSoundEvent( $"buttons/spark{index}.wav" ) ).ToArray();
			RegisterTarget( entity, spark );
			if ( _unsupportedEntities.Add( "env_spark:particles" ) )
				Log.Warning( $"{FilePath}: spark tracers use reconstructed client emission parameters; exact particle lifetimes, palette, width and distribution are not verified." );
			return true;
		}
		if ( entity.ClassName is not ("env_sprite" or "env_glow") )
			return false;

		var model = entity.GetString( "model" );
		var sprite = string.IsNullOrEmpty( model ) ? null : Host.GetSprite( model );
		if ( sprite is null )
		{
			Log.Warning( $"{FilePath}: {entity.ClassName} '{entity.TargetName}' has missing or unsupported sprite '{model}'." );
			return true;
		}
		var go = new GameObject( true, EntityName( entity ) );
		go.WorldPosition = entity.Origin;
		var angles = entity.Angles;
		if ( entity.ClassName == "env_sprite" && angles.yaw != 0 && angles.roll == 0 )
			angles = new( angles.pitch, 0, angles.yaw );
		if ( sprite.Orientation is 0 or 1 )
			angles = new( angles.pitch, angles.yaw, 0 );
		go.WorldRotation = Rotation.From( angles );
		var effect = go.AddComponent<SpriteEffect>();
		effect.TargetName = entity.TargetName;
		effect.StartOn = string.IsNullOrEmpty( entity.TargetName ) || (entity.SpawnFlags & 1) != 0;
		effect.PlayOnce = (entity.SpawnFlags & 2) != 0;
		effect.Glow = entity.ClassName == "env_glow";
		effect.RandomSync = sprite.SyncType == 1;
		effect.FrameRate = entity.GetFloat( "framerate" );
		effect.Scale = entity.GetFloat( "scale", 1 );
		effect.RenderMode = entity.RenderMode;
		effect.NoDissipation = entity.GetInt( "renderfx" ) == 14;
		effect.RenderAmount = entity.RenderAmt;
		effect.RenderColor = new Color( entity.RenderColor.x / 255f, entity.RenderColor.y / 255f, entity.RenderColor.z / 255f );
		effect.TextureFormat = sprite.TextureFormat;
		var path = Host.FindMountPath( model );
		for ( var i = 0; i < sprite.Frames.Count; i++ )
		{
			var frame = sprite.Frames[i];
			var texture = Texture.Load( $"mount://{Host.Ident}/{path}/frame{i}.vtex" );
			var resource = Sprite.FromTexture( texture );
			resource.Animations[0].Origin = new Vector2( -frame.X / (float)frame.Width, frame.Y / (float)frame.Height );
			resource.EmbeddedResource = new Sandbox.Resources.EmbeddedResource { ResourceCompiler = "embed" };
			effect.Frames.Add( new() { Sprite = resource, Size = new( Math.Max( frame.Width, frame.Height ) ) } );
		}
		foreach ( var group in sprite.Groups )
			effect.Groups.Add( new() { FirstFrame = group.FirstFrame, FrameCount = group.FrameCount, Intervals = group.Intervals } );
		var renderer = go.AddComponent<SpriteRenderer>( false );
		effect.Renderer = renderer;
		renderer.Lighting = false;
		renderer.Shadows = false;
		renderer.IsSorted = true;
		renderer.FogStrength = 0;
		renderer.Billboard = sprite.Orientation switch
		{
			0 or 1 => SpriteRenderer.BillboardMode.YOnly,
			3 => SpriteRenderer.BillboardMode.None,
			_ => SpriteRenderer.BillboardMode.Always
		};
		if ( entity.GetInt( "renderfx" ) is not (0 or 14) )
			Log.Warning( $"{FilePath}: sprite '{model}' renderfx {entity.GetInt( "renderfx" )} is unsupported." );
		if ( sprite.Orientation == 1 )
			Log.Warning( $"{FilePath}: sprite '{model}' facing-upright orientation uses camera-plane upright rendering; per-view facing-position billboards are unavailable." );
		RegisterTarget( entity, effect );
		effect.Initialize();
		return true;
	}

	private EffectSprite LoadEffectSprite( string model )
	{
		if ( _effectSprites.TryGetValue( model, out var cached ) ) return cached;
		var sprite = Host.GetSprite( model );
		if ( sprite is null )
		{
			Log.Warning( $"{FilePath}: missing effect sprite '{model}'." );
			return null;
		}
		var result = new EffectSprite { TextureFormat = sprite.TextureFormat };
		var path = Host.FindMountPath( model );
		for ( var i = 0; i < sprite.Frames.Count; i++ )
		{
			var frame = sprite.Frames[i];
			var resource = Sprite.FromTexture( Texture.Load( $"mount://{Host.Ident}/{path}/frame{i}.vtex" ) );
			resource.Animations[0].Origin = new Vector2( -frame.X / (float)frame.Width, frame.Y / (float)frame.Height );
			resource.EmbeddedResource = new Sandbox.Resources.EmbeddedResource { ResourceCompiler = "embed" };
			result.Frames.Add( new() { Sprite = resource, Size = new( Math.Max( frame.Width, frame.Height ) ) } );
		}
		foreach ( var group in sprite.Groups )
			result.Groups.Add( new() { FirstFrame = group.FirstFrame, FrameCount = group.FrameCount, Intervals = group.Intervals } );
		_effectSprites.Add( model, result );
		return result;
	}

	private void SpawnExplosion( Entity entity )
	{
		var go = new GameObject( true, EntityName( entity ) );
		go.WorldPosition = entity.Origin;
		var explosion = go.AddComponent<ExplosionEffect>();
		explosion.TargetName = entity.TargetName;
		explosion.Magnitude = entity.GetInt( "iMagnitude" );
		explosion.NoDamage = (entity.SpawnFlags & 1) != 0;
		explosion.Repeatable = (entity.SpawnFlags & 2) != 0;
		explosion.NoFireball = (entity.SpawnFlags & 4) != 0;
		explosion.NoSmoke = (entity.SpawnFlags & 8) != 0;
		explosion.NoDecal = (entity.SpawnFlags & 16) != 0;
		explosion.NoSparks = (entity.SpawnFlags & 32) != 0;
		if ( !explosion.NoFireball ) explosion.Fireball = LoadEffectSprite( "sprites/zerogxplode.spr" );
		if ( !explosion.NoSmoke ) explosion.Smoke = LoadEffectSprite( "sprites/steam1.spr" );
		explosion.ExplosionSounds = Enumerable.Range( 3, 3 ).Select( i => BuildSoundEvent( $"weapons/explode{i}.wav", 1, 0.3f ) ).ToArray();
		if ( !explosion.NoDecal ) explosion.Scorches = [Host.GetDecal( "{scorch1" ), Host.GetDecal( "{scorch2" )];
		RegisterTarget( entity, explosion );
		_explosions.Add( explosion );
		if ( _unsupportedEntities.Add( "env_explosion:client-effects" ) )
			Log.Warning( $"{FilePath}: env_explosion uses decoded fireball/smoke sprites; client explosion lights and secondary particles are not implemented, and smoke motion/color and spark tracer parameters are reconstructed rather than verified 1:1." );
		if ( !explosion.NoDamage && _unsupportedEntities.Add( "env_explosion:host-damage" ) )
			Log.Warning( $"{FilePath}: explosion damage reaches IDamageable receivers; health, armor and hitgroup responses belong to the host. IGoldSrcBlastTarget supplies exact BodyTarget/waterlevel; other receivers use bounds and BSP liquid sampling." );
	}

	private DecalSurfaces PrepareExplosions( BspFile bsp )
	{
		if ( _explosions.Count == 0 && !_beams.Any( x => x.Decals ) ) return null;
		var surfaces = _explosions.Any( x => !x.NoDecal ) || _beams.Any( x => x.Decals ) ? BuildDecalSurfaces( bsp ) : null;
		ExplosionEnvironment environment = null;
		if ( _explosions.Any( x => !x.NoDamage ) )
		{
			environment = _mapState.GameObject.AddComponent<ExplosionEnvironment>();
			environment.Root = checked((int)bsp.Models[0].HeadNode0);
			foreach ( var node in bsp.Nodes )
			{
				var plane = bsp.Planes[node.PlaneNum];
				environment.Nodes.Add( new() { Normal = plane.Normal, Distance = plane.Distance, Front = node.Child0, Back = node.Child1 } );
			}
			environment.Leaves = bsp.Leaves.Select( x => x.Contents ).ToArray();
		}
		foreach ( var explosion in _explosions )
		{
			explosion.Environment = environment;
			explosion.Surfaces = surfaces;
		}
		foreach ( var beam in _beams ) beam.Surfaces = surfaces;
		return surfaces;
	}

	private void SpawnBeam( Entity entity )
	{
		var model = entity.GetString( "texture", entity.ClassName == "env_laser" ? entity.GetString( "model" ) : null );
		var texture = string.IsNullOrEmpty( model ) ? null : LoadEffectSprite( model );
		if ( texture is null )
		{
			Log.Warning( $"{FilePath}: {entity.ClassName} '{entity.TargetName}' has no usable beam texture '{model}'." );
			return;
		}
		var go = new GameObject( true, EntityName( entity ) );
		go.WorldPosition = entity.Origin;
		var beam = go.AddComponent<BeamEffect>();
		beam.TargetName = entity.TargetName;
		beam.Laser = entity.ClassName == "env_laser";
		beam.StartTarget = entity.GetString( "LightningStart" );
		beam.EndTarget = entity.GetString( beam.Laser ? "LaserTarget" : "LightningEnd" );
		beam.StartOn = (entity.SpawnFlags & 1) != 0;
		beam.Toggleable = (entity.SpawnFlags & 2) != 0;
		beam.RandomRestrike = (entity.SpawnFlags & 4) != 0;
		beam.Ring = (entity.SpawnFlags & 8) != 0;
		beam.SparkStart = (entity.SpawnFlags & 16) != 0;
		beam.SparkEnd = (entity.SpawnFlags & 32) != 0;
		beam.Decals = (entity.SpawnFlags & 64) != 0;
		beam.ShadeIn = (entity.SpawnFlags & 128) != 0;
		beam.ShadeOut = (entity.SpawnFlags & 256) != 0;
		beam.Life = entity.GetFloat( "life" );
		beam.Restrike = entity.GetFloat( "StrikeTime" );
		beam.Radius = entity.GetFloat( "Radius" );
		beam.Width = beam.Laser ? (int)entity.GetFloat( "width" ) : entity.GetInt( "BoltWidth" );
		beam.NoiseAmplitude = entity.GetInt( "NoiseAmplitude" );
		beam.TextureScroll = entity.GetInt( "TextureScroll" );
		beam.FrameStart = entity.GetInt( "framestart" );
		beam.FrameRate = entity.GetFloat( "framerate" );
		beam.Damage = entity.GetFloat( "damage" );
		beam.RenderAmount = entity.RenderAmt;
		beam.RenderColor = new Color( entity.RenderColor.x / 255f, entity.RenderColor.y / 255f, entity.RenderColor.z / 255f );
		beam.Texture = texture;
		var endSprite = entity.GetString( "EndSprite" );
		if ( beam.Laser && !string.IsNullOrEmpty( endSprite ) ) beam.EndSprite = LoadEffectSprite( endSprite );
		if ( beam.Decals ) beam.ImpactDecals = Enumerable.Range( 1, 5 ).Select( i => Host.GetDecal( $"{{bigshot{i}" ) ).ToArray();
		RegisterTarget( entity, beam );
		_beams.Add( beam );
		if ( !_beamAnchorsWired )
		{
			_beamAnchorsWired = true;
			_wiring.Add( () =>
			{
				foreach ( var (source, component) in _entities )
				{
					var anchor = component.GameObject.GetComponent<BeamAnchor>() ?? component.GameObject.AddComponent<BeamAnchor>();
					anchor.PointEntity = source.ClassName is "info_target" or "info_landmark" or "path_corner"
						|| (string.IsNullOrEmpty( source.GetString( "model" ) ) && component is not (SpriteEffect or BeamEffect or Monster));
				}
			} );
		}
		if ( _unsupportedEntities.Add( "env_beam:client-rendering" ) )
			Log.Warning( $"{FilePath}: beams use LineRenderer; client texture phase/tiling, per-view culling and ring tessellation are reconstructed rather than verified 1:1." );
		if ( beam.NoiseAmplitude != 0 && _unsupportedEntities.Add( "env_beam:noise" ) )
			Log.Warning( $"{FilePath}: beam noise displacement is not reproduced; the rendered centerline remains straight." );
		if ( beam.EndSprite is not null && _unsupportedEntities.Add( "env_laser:end-glow" ) )
			Log.Warning( $"{FilePath}: laser endpoint glow is depth-tested and world-sized; view-dependent glow occlusion/scaling is unavailable." );
		if ( (beam.SparkStart || beam.SparkEnd) && _unsupportedEntities.Add( "env_spark:particles" ) )
			Log.Warning( $"{FilePath}: beam sparks use reconstructed client tracer parameters, not verified 1:1 particle emission." );
		if ( beam.Damage != 0 && _unsupportedEntities.Add( "env_beam:host-damage" ) )
			Log.Warning( $"{FilePath}: beam damage reaches IDamageable with DMG_ENERGYBEAM; host health, armor and hitgroup responses are not supplied by the mount." );
	}
}
