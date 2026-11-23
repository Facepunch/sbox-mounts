using Sandbox;
using Sandbox.Mounting;
using Sandbox.Volumes;
using System;

namespace NS2;

class MapLoader( string filePath ) : SceneLoader<GameMount>
{
	/// <summary>
	/// Path relative to the game's install root
	/// </summary>
	string FilePath { get; } = filePath;

	internal const float Scale = 40.0f;

	readonly List<Model> renderModels = [];
	readonly HashSet<PhysicsGroupDescription> physicsResources = [];

	protected override void OnSceneUnloaded()
	{
		renderModels.Clear();
		physicsResources.Clear();
	}

	protected override void BuildScene()
	{
		var bytes = Host.ReadAllBytes( FilePath );
		var level = LevelFile.Read( bytes );

		var root = new GameObject( true, System.IO.Path.GetFileNameWithoutExtension( FilePath ) );
		// SceneLoader serializes the children of this temporary scene. Activate their physics and rendering on the real load.
		root.Scene.Enabled = false;
		root.Scene.NavMesh.IsEnabled = true;
		root.Scene.NavMesh.GenerateLinks = true;

		var postProcessing = new GameObject( true, "NS2 post processing" ) { Parent = root };
		postProcessing.AddComponent<PostProcessVolume>().SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite };
		var tonemapping = postProcessing.AddComponent<Tonemapping>();
		tonemapping.Mode = Tonemapping.TonemappingMode.Linear;
		tonemapping.AutoExposureEnabled = false;

		var geometry = new StaticGeometry();
		var world = new GameObject( true, "worldspawn" ) { Parent = root, IsStatic = true };
		world.Tags.Add( "world" );
		BuildWorld( level, bytes, world, geometry );
		var decals = new Dictionary<string, DecalDefinition>( StringComparer.OrdinalIgnoreCase );
		var staticProps = new Dictionary<string, ModelLoader.StaticProp>( StringComparer.OrdinalIgnoreCase );

		foreach ( var entity in level.Entities )
		{
			if ( level.Groups.TryGetValue( entity.GroupId, out var group ) )
			{
				if ( LevelGroups.IsInactiveSeasonal( group.Name ) )
					continue;

				if ( entity.Name is "light_point" or "light_spot" or "light_ambient" &&
					group.Name is "Low Lights" or "Minimal Lights" )
					continue;
			}

			if ( entity.Name == "decal" )
				AddDecal( entity, root, decals );
			else
				BuildEntity( entity, root, geometry, staticProps );
		}

		geometry.Build( world, FilePath, renderModels );
		AddPhysics( world, geometry.CreatePhysics( FilePath ) );
	}

	void BuildWorld( LevelFile level, byte[] bytes, GameObject go, StaticGeometry geometry )
	{
		if ( level.WorldMesh.Length <= 0 )
			return;

		var data = LevelMesh.Read( bytes, level.Version, level.WorldMesh.Offset, level.WorldMesh.Length, level.Groups, level.Layers );
		if ( data.Triangles.Length == 0 || data.Positions.Length == 0 )
			return;

		int faceCount = data.FaceMaterial.Length;
		var faceNormal = new Vector3[faceCount];
		var faceHasNormal = new bool[faceCount];
		var faceUAxis = new Vector3[faceCount];
		var faceVAxis = new Vector3[faceCount];
		var faceOffset = new Vector2[faceCount];

		foreach ( var tri in data.Triangles )
		{
			if ( (uint)tri.Face >= (uint)faceCount || faceHasNormal[tri.Face] )
				continue;
			if ( !InRange( tri, data.Positions.Length ) )
				continue;

			var p0 = data.Positions[tri.V0];
			var p1 = data.Positions[tri.V1];
			var p2 = data.Positions[tri.V2];
			var n = Vector3.Cross( p2 - p1, p0 - p1 ).Normal;

			var mapping = data.TextureMappings.TryGetValue( data.FaceMappingGroup[tri.Face], out var groupMapping )
				? groupMapping
				: new LevelMesh.TextureMapping( data.FaceAngle[tri.Face], data.FaceScaleU[tri.Face],
					data.FaceScaleV[tri.Face], data.FaceOffsetU[tri.Face], data.FaceOffsetV[tri.Face], n );
			var p = GetPerpendicular( mapping.Normal );
			var q = Vector3.Cross( mapping.Normal, p );
			float c = MathF.Cos( mapping.Angle );
			float s = MathF.Sin( mapping.Angle );

			faceNormal[tri.Face] = n;
			faceUAxis[tri.Face] = (-p * c + q * s) * mapping.ScaleU;
			faceVAxis[tri.Face] = (p * s + q * c) * mapping.ScaleV;
			faceOffset[tri.Face] = new Vector2( mapping.OffsetU, mapping.OffsetV );
			faceHasNormal[tri.Face] = true;
		}

		var byMaterial = new Dictionary<(int Material, bool ShadowOnly, Vector3Int Cell), List<WorldVertex>>();
		var allVertices = new List<Vector3>();
		var collisionVertex = new int[data.Positions.Length];
		Array.Fill( collisionVertex, -1 );
		var allIndices = new List<uint>();
		var triangleSurfaces = new List<byte>();
		var collisionMaterials = new Dictionary<int, byte>();

		var materials = new Dictionary<int, Material>();
		foreach ( var tri in data.Triangles )
		{
			if ( (uint)tri.Face >= (uint)faceCount || !faceHasNormal[tri.Face] || !InRange( tri, data.Positions.Length ) )
				continue;

			bool hidden = tri.Face < data.FaceHidden.Length && data.FaceHidden[tri.Face];
			bool solid = !(tri.Face < data.FaceNonSolid.Length && data.FaceNonSolid[tri.Face]);
			bool shadowOnly = data.FaceShadowOnly[tri.Face];

			if ( hidden && !solid )
				continue;

			int matIndex = data.FaceMaterial[tri.Face];
			List<WorldVertex> list = null;
			if ( !hidden )
			{
				if ( !materials.TryGetValue( matIndex, out var material ) )
					materials[matIndex] = material = ResolveMaterial( data.Materials, matIndex );

				Vector3Int cell = default;
				if ( !shadowOnly && !material.Flags.IsTranslucent )
				{
					var center = (data.Positions[tri.V0] + data.Positions[tri.V1] + data.Positions[tri.V2]) / 3f;
					cell = StaticGeometry.Cell( ConvertPosition( center ) );
				}
				var key = (matIndex, shadowOnly, cell);
				if ( !byMaterial.TryGetValue( key, out list ) )
					byMaterial[key] = list = [];
			}

			if ( solid )
			{
				if ( !collisionMaterials.TryGetValue( matIndex, out var index ) )
				{
					var path = (uint)matIndex < (uint)data.Materials.Length ? data.Materials[matIndex] : null;
					collisionMaterials[matIndex] = index = geometry.SurfaceIndex( Host.MaterialSurface( path ) );
				}
				triangleSurfaces.Add( index );
			}

			var n = faceNormal[tri.Face];
			var tangent = Swizzle( faceUAxis[tri.Face].Normal );
			float offU = faceOffset[tri.Face].x;
			float offV = faceOffset[tri.Face].y;
			var uAxis = faceUAxis[tri.Face];
			var vAxis = faceVAxis[tri.Face];

			for ( int corner = 0; corner < 3; corner++ )
			{
				int index = corner switch { 0 => tri.V0, 1 => tri.V1, _ => tri.V2 };
				int normalIndex = corner switch { 0 => tri.N0, 1 => tri.N1, _ => tri.N2 };
				var normal = (uint)normalIndex < (uint)data.Normals.Length ? data.Normals[normalIndex] : n;
				var pos = data.Positions[index];
				var uv = new Vector2( Vector3.Dot( uAxis, pos ) + offU, Vector3.Dot( vAxis, pos ) + offV );
				var worldPos = Swizzle( pos ) * Scale;
				list?.Add( new WorldVertex( worldPos, Swizzle( normal ), tangent, uv ) );

				if ( !solid )
					continue;

				if ( collisionVertex[index] < 0 )
				{
					collisionVertex[index] = allVertices.Count;
					allVertices.Add( worldPos );
				}
				allIndices.Add( (uint)collisionVertex[index] );
			}
		}

		if ( allVertices.Count == 0 && byMaterial.Count == 0 )
			return;

		var builder = Model.Builder.WithName( $"ns2_world_{System.IO.Path.GetFileNameWithoutExtension( FilePath )}" );
		var shadowBuilder = Model.Builder.WithName( $"ns2_shadows_{System.IO.Path.GetFileNameWithoutExtension( FilePath )}" );

		bool hasSurfaces = false;
		bool hasBlockers = false;
		foreach ( var (key, verts) in byMaterial )
		{
			if ( verts.Count == 0 )
				continue;

			var bounds = new BBox { Mins = float.MaxValue, Maxs = float.MinValue };
			for ( int i = 0; i < verts.Count; i++ )
				bounds = bounds.AddPoint( verts[i].position );

			var material = materials[key.Material];
			var indices = new int[verts.Count];
			for ( int i = 0; i < indices.Length; i++ ) indices[i] = i;

			if ( !key.ShadowOnly && !material.Flags.IsTranslucent )
			{
				geometry.Add( new( material, verts.ToArray(), indices, bounds ), Transform.Zero, true );
				continue;
			}

			hasBlockers |= key.ShadowOnly;
			hasSurfaces |= !key.ShadowOnly;
			var mesh = new Mesh( material ) { Bounds = bounds };
			mesh.CreateVertexBuffer( verts.Count, verts );
			mesh.CreateIndexBuffer( indices.Length, indices );
			(key.ShadowOnly ? shadowBuilder : builder).AddMesh( mesh );
		}

		if ( hasSurfaces )
		{
			var model = builder.Create();
			renderModels.Add( model );
			go.AddComponent<ModelRenderer>().Model = model;
		}

		if ( allIndices.Count > 0 )
			geometry.AddCollisionMesh( allVertices, allIndices, triangleSurfaces );

		if ( hasBlockers )
		{
			var shadows = new GameObject( true, "NS2 shadow blockers" ) { Parent = go, IsStatic = true };
			shadows.Tags.Add( "world" );
			var renderer = shadows.AddComponent<ModelRenderer>();
			renderer.Model = shadowBuilder.Create();
			renderModels.Add( renderer.Model );
			renderer.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
		}
	}

	void AddPhysics( GameObject go, PhysicsGroupDescription physics )
	{
		if ( physics is null || physics.Parts.Count == 0 ) return;
		physicsResources.Add( physics );
		var collider = go.AddComponent<PhysicsCollider>( false );
		collider.Physics = physics;
		collider.Static = true;
		collider.Enabled = true;
	}

	Material ResolveMaterial( string[] materials, int index )
	{
		if ( (uint)index < (uint)materials.Length && GameMount.TryResolve( Host.FileSystem, materials[index], out var path ) )
		{
			var material = Material.Load( $"mount://{Host.Ident}/{path}.vmat" );
			if ( material is { IsValid: true, IsError: false } ) return material;
		}

		return Material.Load( "materials/ns2/missing_world.vmat" );
	}

	static bool InRange( LevelMesh.Tri tri, int count ) =>
		(uint)tri.V0 < (uint)count && (uint)tri.V1 < (uint)count && (uint)tri.V2 < (uint)count;

	static Vector3 GetPerpendicular( Vector3 v )
	{
		if ( MathF.Abs( v.y ) <= 0.9999f )
		{
			float inv = 1f / MathF.Sqrt( v.x * v.x + v.z * v.z );
			return new Vector3( -inv * v.z, 0f, inv * v.x );
		}

		float inv2 = 1f / MathF.Sqrt( v.y * v.y + v.z * v.z );
		return new Vector3( 0f, inv2 * v.z, -inv2 * v.y );
	}

	void BuildEntity( LevelEntity entity, GameObject parent, StaticGeometry geometry, Dictionary<string, ModelLoader.StaticProp> staticProps )
	{
		var name = entity.Name;

		if ( name == "ready_room_start" )
		{
			var spawn = new GameObject( true, name )
			{
				Parent = parent,
				WorldPosition = Origin( entity ),
				WorldRotation = Rotation.FromYaw( Angles( entity ).Yaw() )
			};
			spawn.AddComponent<SpawnPoint>();
			return;
		}

		if ( name == "ambient_sound" )
		{
			var go = new GameObject( true, name ) { Parent = parent, WorldPosition = Origin( entity ) };
			var sound = go.AddComponent<MapAmbientSound>();
			sound.EventName = entity.Get( "eventName" ).AsString();
			sound.Radius = Math.Clamp( entity.Get( "radius" ).AsFloat(), 0, 1000 ) * Scale;
			sound.MinDistance = Math.Clamp( entity.Get( "minFalloff" ).AsFloat(), 0, 1000 ) * Scale;
			sound.MaxDistance = Math.Clamp( entity.Get( "maxFalloff" ).AsFloat(), 0, 1000 ) * Scale;
			sound.Linear = entity.Get( "falloffType" ).AsInt() >= 2;
			sound.Positional = entity.Get( "positioning" ).AsInt() < 2;
			sound.Volume = entity.Has( "volume" ) ? Math.Clamp( entity.Get( "volume" ).AsFloat(), 0, 1 ) : 1;
			sound.Pitch = Math.Clamp( entity.Get( "pitch" ).AsFloat(), -4, 4 );
			sound.StartsOn = entity.Get( "startsOn" ).AsBool();
			sound.OffOnExit = entity.Get( "offOnExit" ).AsBool();
			return;
		}

		if ( name == "ladder" )
		{
			// TriggerMixin:SetBox takes half-extents in metres and raises the center
			// along world up, even when the trigger itself is tilted.
			var halfSize = ConvertScale( entity.Get( "scale" ).AsVector3() ) * (0.2395f * Scale);
			var ladder = new GameObject( true, name )
			{
				Parent = parent,
				WorldPosition = Origin( entity ) + Vector3.Up * halfSize.z,
				WorldRotation = Angles( entity )
			};
			ladder.Tags.Add( "ladder" );
			var trigger = ladder.AddComponent<BoxCollider>();
			trigger.Scale = halfSize * 2;
			trigger.IsTrigger = true;
			trigger.Static = true;
			return;
		}
		if ( name == "door" )
		{
			var skin = entity.Get( "clean" ).AsBool() ? "door_clean" : "door";
			var model = Model.Load( ModelPath( $"models/misc/door/{skin}.model" ) );
			if ( model is null || model.IsError ) return;

			var go = new GameObject( true, name ) { Parent = parent, WorldTransform = new Transform( Origin( entity ), Angles( entity ) ) };
			var renderer = go.AddComponent<SkinnedModelRenderer>();
			renderer.Model = model;
			renderer.UseAnimGraph = false;
			renderer.Sequence.Name = "opened";
			renderer.Sequence.Looping = false;
			go.Tags.Add( "world" );
			AddAnimatedMapCollision( renderer );
			go.AddComponent<MapDoor>().Renderer = renderer;
			return;
		}

		// ResourcePoint.lua supplies this model at spawn. The level only stores the placement.
		var modelPath = name == "resource_point"
			? "models/misc/resource_nozzle/resource_nozzle.model" : entity.Get( "model" ).AsString();
		if ( !string.IsNullOrWhiteSpace( modelPath ) )
		{
			if ( name is "prop_static" or "resource_point" )
			{
				if ( !staticProps.TryGetValue( modelPath, out var prop ) )
				{
					var loader = Host.GetByPath( ModelPath( modelPath ) ) as ModelLoader;
					try
					{
						prop = loader?.BuildStaticProp();
					}
					catch ( Exception e )
					{
						Log.Warning( e, $"Could not bake static prop '{modelPath}' in '{FilePath}'." );
					}
					staticProps[modelPath] = prop;
					if ( prop?.Translucent is not null ) renderModels.Add( prop.Translucent );
				}
				if ( prop is not null )
					BuildProp( entity, parent, prop.Translucent, geometry, prop );
				return;
			}

			var path = ModelPath( modelPath );
			var model = Model.Load( path );

			if ( model is null || model.IsError ) return;
			if ( name == "prop_dynamic" && Host.GetByPath( path ) is ModelLoader dynamicLoader )
				BuildDynamicProp( entity, parent, model, dynamicLoader );
			else
				BuildProp( entity, parent, model, geometry );
		}
		else if ( name.Equals( "skybox", StringComparison.OrdinalIgnoreCase ) )
		{
			BuildSkybox( entity, parent );
		}
		else if ( name is "light_point" or "light_spot" )
		{
			BuildLight( entity, parent );
		}
	}

	/// <summary>
	/// Client.lua turns a "skybox" entity into a cinematic in RenderScene.Zone_SkyBox. Every shipped
	/// map has exactly one, naming a maps/skyboxes/*.cinematic whose "Sky Box" node lists the six
	/// face materials. <see cref="SkyBuilder"/> folds those into a cubemap.
	/// </summary>
	void BuildSkybox( LevelEntity entity, GameObject parent )
	{
		if ( !Graphics.IsAvailable )
			return;

		var cinematic = entity.Get( "cinematicName" ).AsString();
		if ( string.IsNullOrWhiteSpace( cinematic ) )
			return;

		var material = SkyBuilder.Build( Host, cinematic );
		if ( material is null )
			return;

		var go = new GameObject( true, "Skybox" ) { Parent = parent };
		var skybox = go.AddComponent<SkyBox2D>();
		skybox.SkyMaterial = material;

		// Spark lights the level entirely from its own light entities, so letting the sky
		// contribute indirect light on top of those just washes the map out.
		skybox.SkyIndirectLighting = false;
	}

	static void BuildDynamicProp( LevelEntity entity, GameObject parent, Model model, ModelLoader loader )
	{
		// PropDynamic.lua stores the editor scale but never applies it to the model.
		var go = new GameObject( true, entity.Name ) { Parent = parent, WorldTransform = new Transform( Origin( entity ), Angles( entity ) ) };
		var renderer = go.AddComponent<SkinnedModelRenderer>();
		renderer.Model = model;
		renderer.UseAnimGraph = false;
		var (clips, loop) = loader.PropGraph.Resolve( entity.Get( "animation" ).AsString() );
		for ( int i = 0; i < clips.Length; i++ )
			clips[i] = loader.ResolveSequenceName( clips[i] );

		var moving = entity.Get( "dynamic" ).AsBool();
		if ( !moving ) go.Tags.Add( "world" );

		if ( clips.Length > 0 )
		{
			renderer.Sequence.Name = clips[0];
			renderer.Sequence.Looping = clips.Length == 1;
			if ( clips.Length > 1 )
			{
				var sequence = go.AddComponent<PropAnimationSequence>();
				sequence.Renderer = renderer;
				sequence.Clips = clips;
				sequence.LoopStart = loop;
			}
		}
		else renderer.PlaybackRate = 0;

		// The shipped Lua enables physics only when collidable is explicitly false.
		if ( !entity.Has( "collidable" ) || entity.Get( "collidable" ).AsBool() || model.Physics is null ) return;
		if ( model.Physics.Parts.Any( p => model.Bones.GetBone( p.BoneName ) is not null ) )
		{
			if ( !moving )
			{
				AddAnimatedMapCollision( renderer );
				return;
			}

			var physics = go.AddComponent<ModelPhysics>();
			physics.Model = model;
			physics.Renderer = renderer;
			physics.MotionEnabled = true;
		}
		else
		{
			var collider = go.AddComponent<PhysicsCollider>();
			collider.Physics = model.Physics;
			collider.Static = !moving;
			if ( moving ) go.AddComponent<Rigidbody>();
		}
	}

	// These hulls follow the animation. Giving the bones rigidbodies would let the physgun unfreeze them.
	static void AddAnimatedMapCollision( SkinnedModelRenderer renderer )
	{
		var model = renderer.Model;
		if ( model.Physics is null ) return;
		renderer.CreateBoneObjects = true;
		var bones = model.CreateBoneObjects( renderer.GameObject );
		foreach ( var part in model.Physics.Parts )
		{
			var bone = model.Bones.GetBone( part.BoneName );
			var target = bone is null ? renderer.GameObject : bones[bone];
			foreach ( var hull in part.Hulls )
			{
				var collider = target.AddComponent<HullCollider>();
				collider.Type = HullCollider.PrimitiveType.Points;
				collider.Points = hull.GetPoints().ToList();
				collider.Surface = hull.Surface;
			}
		}
	}

	void BuildProp( LevelEntity entity, GameObject parent, Model model, StaticGeometry geometry, ModelLoader.StaticProp prop = null )
	{
		bool isStatic = prop is not null;
		bool collidable = !entity.Has( "collidable" ) || entity.Get( "collidable" ).AsBool();
		bool shadows = !entity.Has( "castsShadows" ) || entity.Get( "castsShadows" ).AsBool();
		var transform = new Transform( Origin( entity ), Angles( entity ), ScaleOf( entity ) );
		if ( isStatic )
		{
			if ( collidable ) geometry.AddHulls( prop.Hulls, transform, prop.Surface );
			foreach ( var part in prop.Parts )
				geometry.Add( part, transform, shadows );
		}

		if ( model is null ) return;

		var go = new GameObject( true, entity.Name ) { Parent = parent, WorldTransform = transform, IsStatic = isStatic };
		if ( isStatic || (entity.Name == "prop_dynamic" && !entity.Get( "dynamic" ).AsBool()) ) go.Tags.Add( "world" );
		var renderer = go.AddComponent<ModelRenderer>();
		renderer.Model = model;
		if ( !shadows ) renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		if ( !isStatic && collidable ) AddPhysics( go, model.Physics );
	}

	// Spark attenuates point/spot lights as saturate( 1 - d/R )^2 (renderer/Lighting.hlsl,
	// GetDistanceAttenuation). It is deliberately non-physical so light fills the sphere evenly and
	// never blows out near the source.
	//
	// s&box inherits Source 2's saturate( 1/(A1*d + A2*d^2) - truncation ), where truncation is
	// 1/(A1*R + A2*R^2) so it reaches zero at the radius, and A0/A1 are zero for our lights. That
	// reduces to saturate( (R^2/d^2 - 1) / k ) with k = A2 * R^2, and SceneLight scales the exposed
	// Attenuation property by 1/10000, so k = Attenuation * R^2 / 10000.
	//
	// The two curves can't be made equal, but a least-squares fit over the lit sphere (weighted by
	// shell area, so distant pixels count more) lands on k = 7.4 with the colour scaled by 0.55.
	// Leaving Attenuation at its default of 1 gives k ~= 5 at typical NS2 radii, which clips the
	// light to full brightness across the inner 40% of its radius and runs ~3x hot through the
	// midrange - that is what made converted maps look blown out.
	const float FalloffK = 7.4f;
	const float FalloffGain = 0.55f;

	static float AttenuationFor( float radius ) => FalloffK * 10000f / (radius * radius);

	void BuildLight( LevelEntity entity, GameObject parent )
	{
		var color = entity.Has( "color" ) ? ToColor( entity.Get( "color" ).AsVector3() ) : Color.White;
		float intensity = entity.Has( "intensity" ) ? MathF.Max( entity.Get( "intensity" ).AsFloat(), 0f ) : 1f;
		float radius = (entity.Has( "distance" ) ? entity.Get( "distance" ).AsFloat() : 2f) * Scale;
		if ( radius <= 0f || intensity <= 0f ) return;
		bool shadows = entity.Has( "casts_shadows" ) && entity.Get( "casts_shadows" ).AsBool();
		bool specular = !entity.Has( "specular" ) || entity.Get( "specular" ).AsBool();

		var radiance = (color * (intensity * FalloffGain)).WithAlpha( 1f );
		float attenuation = AttenuationFor( radius );

		var contribution = Light.LightContribution.Diffuse | Light.LightContribution.Transmissive;
		if ( specular )
			contribution |= Light.LightContribution.Specular;

		var go = new GameObject( true, entity.Name )
		{
			Parent = parent,
			WorldPosition = Origin( entity ),
			WorldRotation = Angles( entity )
		};

		if ( entity.Name.Equals( "light_spot", StringComparison.OrdinalIgnoreCase ) )
		{
			var gobo = entity.Get( "gobo_texture" ).AsString();
			var spot = go.AddComponent<ProjectedSpotLight>( false );
			if ( GameMount.TryResolve( Host.FileSystem, gobo, out var texturePath ) )
				spot.TexturePath = $"mount://{Host.Ident}/{texturePath}.vtex";
			spot.LightColor = radiance;
			spot.Radius = radius;
			spot.Attenuation = attenuation;
			spot.Shadows = shadows;
			spot.Contribution = contribution;

			// Spark cones are half-angles measured from the light's forward axis, same as s&box.
			float outer = entity.Has( "outerAngle" )
				? Math.Clamp( entity.Get( "outerAngle" ).AsFloat().RadianToDegree(), 0f, 90f )
				: 45f;
			float inner = entity.Has( "innerAngle" )
				? Math.Clamp( entity.Get( "innerAngle" ).AsFloat().RadianToDegree(), 0f, outer )
				: outer;

			spot.ConeOuter = outer;
			spot.ConeInner = inner;
			spot.Enabled = true;
		}
		else
		{
			var point = go.AddComponent<PointLight>();
			point.LightColor = radiance;
			point.Radius = radius;
			point.Attenuation = attenuation;
			point.Shadows = shadows;
			point.Contribution = contribution;
		}
	}

	string ModelPath( string path )
	{
		path = path.Replace( '\\', '/' );
		if ( !path.EndsWith( ".model", StringComparison.OrdinalIgnoreCase ) )
			path += ".model";

		return $"mount://{Host.Ident}/ns2/{path}.vmdl";
	}

	void AddDecal( LevelEntity entity, GameObject parent, Dictionary<string, DecalDefinition> definitions )
	{
		if ( !Graphics.IsAvailable ) return;

		var material = entity.Get( "material" ).AsString();
		var size = entity.Get( "extents" ).AsVector3() * (2 * Scale);
		if ( size.x <= 0 || size.y <= 0 || size.z <= 0 ) return;
		if ( string.IsNullOrEmpty( material ) ) return;
		if ( !definitions.TryGetValue( material, out var definition ) )
		{
			definition = GameMount.TryResolve( Host.FileSystem, material, out var path )
				? GameResource.Load<DecalDefinition>( $"mount://{Host.Ident}/{path}.vmat/projected.decal" ) : null;
			definitions[material] = definition;
		}
		if ( definition?.ColorTexture is null ) return;

		// Spark projects along -Y and maps X/Z to increasing U/V.
		var go = new GameObject( true, "decal" )
		{
			Parent = parent,
			WorldPosition = Origin( entity ),
			WorldRotation = Angles( entity ) * Rotation.LookAt( Vector3.Down, Vector3.Backward )
		};
		var decal = go.AddComponent<Decal>();
		decal.Decals = [definition];
		decal.Size = new Vector2( size.x, size.z );
		decal.Depth = size.y;
		decal.Rotation = 0;
	}

	static Vector3 Origin( LevelEntity entity )
	{
		if ( !entity.Has( "origin" ) ) return Vector3.Zero;
		return ConvertPosition( entity.Get( "origin" ).AsVector3() );
	}

	static Vector3 ScaleOf( LevelEntity entity )
	{
		if ( !entity.Has( "scale" ) ) return Vector3.One;
		var v = entity.Get( "scale" );
		if ( v.Components < 3 )
		{
			float s = v.AsFloat();
			return s == 0f ? Vector3.One : new Vector3( s );
		}
		return ConvertScale( v.AsVector3() );
	}

	static Rotation Angles( LevelEntity entity )
	{
		if ( !entity.Has( "angles" ) ) return Rotation.Identity;
		var a = entity.Get( "angles" ).AsVector3();
		return Rotation.From( new Angles( a.y.RadianToDegree(), a.z.RadianToDegree(), a.x.RadianToDegree() ) );
	}

	static Vector3 Swizzle( Vector3 v ) => new( v.z, v.x, v.y );
	static Vector3 ConvertPosition( Vector3 v ) => Swizzle( v ) * Scale;
	static Vector3 ConvertScale( Vector3 v ) => Swizzle( v );

	static Color ToColor( Vector3 rgb ) => new( rgb.x, rgb.y, rgb.z );
}

[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential )]
internal struct WorldVertex
{
	[VertexLayout.Position] public Vector3 position;
	[VertexLayout.Normal] public Vector3 normal;
	[VertexLayout.Tangent] public Vector3 tangent;
	[VertexLayout.TexCoord] public Vector2 texcoord;

	// Spark level geometry carries no per-vertex colour - the .level mesh chunks
	// only store positions, materials, face UV params and triangles - so Spark
	// feeds white into Material_Input.color for world surfaces.
	//
	// This has to be supplied explicitly, because several vfx surface shaders
	// modulate everything they output by input.color. window_rain, for example,
	// scales both emissive layers and its refraction by color.r/g/b, so with no
	// colour stream the vertex colour reads as zero and the whole material
	// multiplies away to nothing.
	[VertexLayout.Color] public Color32 color;

	public WorldVertex( Vector3 position, Vector3 normal, Vector3 tangent, Vector2 texcoord )
	{
		this.position = position;
		this.normal = normal;
		this.tangent = tangent;
		this.texcoord = texcoord;
		this.color = Color32.White;
	}
}
