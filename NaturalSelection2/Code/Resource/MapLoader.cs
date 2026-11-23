using Sandbox;
using Sandbox.Mounting;
using System;

namespace NS2;

class MapLoader( string filePath ) : SceneLoader<GameMount>
{
	/// <summary>
	/// Path relative to the game's install root
	/// </summary>
	string FilePath { get; } = filePath;

	const float Scale = 40.0f;

	protected override void BuildScene()
	{
		var bytes = Host.ReadAllBytes( FilePath );
		var level = LevelFile.Read( bytes );

		var root = new GameObject( true, System.IO.Path.GetFileNameWithoutExtension( FilePath ) );
		root.Scene.NavMesh.IsEnabled = true;
		root.Scene.NavMesh.GenerateLinks = true;

		BuildWorld( level, bytes, root );

		var models = new Dictionary<string, Model>( StringComparer.OrdinalIgnoreCase );
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

			BuildEntity( entity, root, models );
		}
	}

	void BuildWorld( LevelFile level, byte[] bytes, GameObject parent )
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

		var byMaterial = new Dictionary<(int Material, bool ShadowOnly), List<WorldVertex>>();
		var allVertices = new List<Vector3>();
		var allIndices = new List<int>();
		var traceVertices = new List<Vector3>();
		var traceIndices = new List<int>();

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
			var key = (matIndex, shadowOnly);
			List<WorldVertex> list = null;
			if ( !hidden && !byMaterial.TryGetValue( key, out list ) )
				byMaterial[key] = list = new List<WorldVertex>();

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

				if ( Application.IsEditor && !hidden && !shadowOnly )
				{
					traceVertices.Add( worldPos );
					traceIndices.Add( traceIndices.Count );
				}

				if ( !solid )
					continue;

				allVertices.Add( worldPos );
				allIndices.Add( allIndices.Count );
			}
		}

		if ( allVertices.Count == 0 && byMaterial.Count == 0 )
			return;

		var builder = Model.Builder.WithName( $"ns2_world_{System.IO.Path.GetFileNameWithoutExtension( FilePath )}" );
		var shadowBuilder = Model.Builder.WithName( $"ns2_shadows_{System.IO.Path.GetFileNameWithoutExtension( FilePath )}" );

		foreach ( var (key, verts) in byMaterial )
		{
			if ( verts.Count == 0 )
				continue;

			var bounds = new BBox { Mins = float.MaxValue, Maxs = float.MinValue };
			for ( int i = 0; i < verts.Count; i++ )
				bounds = bounds.AddPoint( verts[i].position );

			var material = ResolveMaterial( data.Materials, key.Material );
			var mesh = new Mesh( material ) { Bounds = bounds };

			var indices = new int[verts.Count];
			for ( int i = 0; i < indices.Length; i++ ) indices[i] = i;

			mesh.CreateVertexBuffer( verts.Count, verts );
			mesh.CreateIndexBuffer( indices.Length, indices );
			(key.ShadowOnly ? shadowBuilder : builder).AddMesh( mesh );
		}

		if ( allVertices.Count > 0 )
			builder.AddCollisionMesh( allVertices, allIndices );

		// trace mesh from render geometry so editor traces (selection etc.) hit what you see
		if ( traceVertices.Count > 0 )
			builder.AddTraceMesh( traceVertices, traceIndices );

		bool hasVisibleGeometry = byMaterial.Keys.Any( key => !key.ShadowOnly );
		bool hasWorldModel = hasVisibleGeometry || allVertices.Count > 0;
		var model = hasWorldModel ? builder.Create() : null;
		if ( hasWorldModel && model is null )
			return;

		var go = new GameObject( true, "worldspawn" ) { Parent = parent };
		if ( hasVisibleGeometry )
			go.AddComponent<ModelRenderer>().Model = model;

		if ( allVertices.Count > 0 )
		{
			var collider = go.AddComponent<ModelCollider>();
			collider.Model = model;
			collider.Static = true;
		}

		if ( byMaterial.Keys.Any( key => key.ShadowOnly ) )
		{
			var shadows = new GameObject( true, "NS2 shadow blockers" ) { Parent = go };
			var renderer = shadows.AddComponent<ModelRenderer>();
			renderer.Model = shadowBuilder.Create();
			renderer.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
		}
	}

	Material ResolveMaterial( string[] materials, int index )
	{
		if ( (uint)index >= (uint)materials.Length || string.IsNullOrWhiteSpace( materials[index] ) )
			return Material.Load( "materials/dev/primary_white.vmat" );

		var material = Material.Load( $"mount://{Host.Ident}/ns2/{materials[index]}.vmat" );
		return material is not null && material.IsValid ? material : Material.Load( "materials/dev/primary_white.vmat" );
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

	void BuildEntity( LevelEntity entity, GameObject parent, Dictionary<string, Model> models )
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

		var modelPath = entity.Get( "model" ).AsString();
		if ( !string.IsNullOrWhiteSpace( modelPath ) )
		{
			if ( !models.TryGetValue( modelPath, out var model ) )
				models[modelPath] = model = LoadModel( modelPath );

			if ( model is not null )
				BuildProp( entity, parent, model );
		}
		else if ( name.Equals( "skybox", StringComparison.OrdinalIgnoreCase ) )
		{
			BuildSkybox( entity, parent );
		}
		else if ( name.StartsWith( "light", StringComparison.OrdinalIgnoreCase ) )
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

		var material = SkyBuilder.Build( Host.FileSystem, cinematic, Host.Ident );
		if ( material is null )
			return;

		var go = new GameObject( true, "Skybox" ) { Parent = parent };
		var skybox = go.AddComponent<SkyBox2D>();
		skybox.SkyMaterial = material;

		// Spark lights the level entirely from its own light entities, so letting the sky
		// contribute indirect light on top of those just washes the map out.
		skybox.SkyIndirectLighting = false;
	}

	static void BuildProp( LevelEntity entity, GameObject parent, Model model )
	{
		var go = new GameObject( true, entity.Name )
		{
			Parent = parent,
			WorldPosition = Origin( entity ),
			WorldRotation = Angles( entity ),
			WorldScale = ScaleOf( entity )
		};

		var renderer = go.AddComponent<ModelRenderer>();
		renderer.Model = model;
		if ( entity.Has( "castsShadows" ) && !entity.Get( "castsShadows" ).AsBool() )
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;

		if ( !entity.Has( "collidable" ) || entity.Get( "collidable" ).AsBool() )
		{
			var collider = go.AddComponent<ModelCollider>();
			collider.Model = model;
			collider.Static = true;
		}
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
		// A Spark ambient volume light shades from six directional colours over a radius; s&box has
		// no equivalent, and a global AmbientLight would flood the whole map, so it is dropped.
		if ( entity.Name.Equals( "light_ambient", StringComparison.OrdinalIgnoreCase ) )
			return;

		var color = entity.Has( "color" ) ? ToColor( entity.Get( "color" ).AsVector3() ) : Color.White;
		float intensity = entity.Has( "intensity" ) ? MathF.Max( entity.Get( "intensity" ).AsFloat(), 0f ) : 1f;
		float radius = entity.Has( "distance" ) ? entity.Get( "distance" ).AsFloat() * Scale : 512f;
		if ( radius <= 0f ) radius = 512f;
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
			SpotLight spot;
			if ( string.IsNullOrWhiteSpace( gobo ) )
				spot = go.AddComponent<SpotLight>();
			else
			{
				var projected = go.AddComponent<ProjectedSpotLight>( false );
				projected.TexturePath = $"mount://{Host.Ident}/ns2/{gobo}.vtex";
				projected.Enabled = true;
				spot = projected;
			}
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

	Model LoadModel( string path )
	{
		path = path.Replace( '\\', '/' );
		if ( !path.EndsWith( ".model", StringComparison.OrdinalIgnoreCase ) )
			path += ".model";

		var model = Model.Load( $"mount://{Host.Ident}/ns2/{path}.vmdl" );
		return model is not null && !model.IsError ? model : null;
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
