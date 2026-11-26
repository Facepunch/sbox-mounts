using System;

namespace Overgrowth;

internal sealed class TerrainLoader(string levelPath) : ResourceLoader<GameMount>
{
	TerrainMaterials materials;
	string colorMap;
	ImageSampler terrainColor;

	static TerrainLoader LoaderFor( ModelCollider collider )
	{
		var path = collider?.Model?.ResourcePath;
		if ( path?.StartsWith( "mount://overgrowth/data/levels/", System.StringComparison.OrdinalIgnoreCase ) != true ) return null;
		return Sandbox.Mounting.Directory.Get( "overgrowth" )?.GetByPath( path ) as TerrainLoader;
	}

	internal static Color ContactColor( in SceneTraceResult hit, Vector3? point = null )
	{
		if ( hit.Collider is not ModelCollider collider || LoaderFor( collider ) is not { colorMap: not null, materials: { } terrain } loader ||
			terrain.UvAt( hit.Triangle, collider.WorldTransform.PointToLocal( point ?? hit.EndPosition ) ) is not { } uv ) return Color.White;
		loader.terrainColor ??= new ImageSampler( loader.Host, loader.colorMap );
		var pixel = loader.terrainColor.Sample( uv );
		return new( MathF.Pow( pixel.r / 255f, 2.2f ), MathF.Pow( pixel.g / 255f, 2.2f ), MathF.Pow( pixel.b / 255f, 2.2f ) );
	}

	internal static Surface ContactSurface( in SceneTraceResult hit, Vector3 point )
	{
		if ( hit.Collider is ModelCollider collider && LoaderFor( collider )?.materials is { } terrain )
			return terrain.At( hit.Triangle, collider.WorldTransform.PointToLocal( point ) ) ?? hit.Surface;
		return hit.Surface;
	}

	internal static Surface ContactSurface( ModelCollider collider, Vector3 point, Surface fallback ) =>
		ContactSurface( collider, point, fallback, out _ );

	internal static Surface ContactSurface( ModelCollider collider, Vector3 point, Surface fallback, out SceneTraceResult terrainHit )
	{
		terrainHit = default;
		if ( LoaderFor( collider )?.materials is not { } terrain ) return fallback;
		var span = Vector3.Up * 40000f;
		foreach ( var hit in collider.Scene.Trace.Ray( point + span, point - span ).IgnoreDynamic().RunAll() )
			if ( hit.Collider == collider )
			{
				terrainHit = hit;
				return terrain.At( hit.Triangle, collider.WorldTransform.PointToLocal( hit.EndPosition ) ) ?? fallback;
			}
		return fallback;
	}

	protected override object Load()
	{
		var terrain=XmlAsset.Read(Host.FileSystem,levelPath,"Terrain");
		var heightmap=Host.Resolve(terrain.Element("Heightmap").Value);
		var layers = terrain.Element( "DetailMaps" )?.Elements( "DetailMap" ).Count() ?? 0;
		var detailed = layers == 4 && terrain.Element( "WeightMap" ) is { Value.Length: > 0 };
		var mat = detailed
			? MaterialLoader.CreateMaterial( Path + "/terrain.vmat", true )
			: Material.Create( Path + "/terrain.vmat", "simple_color" );
		if ( terrain.Element( "ColorMap" ) is { Value.Length: > 0 } color )
		{
			colorMap = color.Value;
			mat.Set( detailed ? "g_tColor" : "Color", Host.LoadTexture( color.Value ) );
		}
		if ( detailed )
		{
			mat.Set( "g_flTerrain", 1f );
			mat.Set( "g_flHeightBlend", terrain.Element( "ShaderExtra" )?.Value.Contains( "#HEIGHT_BLEND" ) == true ? 1f : 0f );
			MaterialLoader.ApplyDetailMaps( Host, mat, terrain );
		}
		var builder=Model.Builder.WithName(Path);
		Model Complete( Mesh mesh, Vector3[] positions, int[] indices, Vector2[] uvs )
		{
			builder.AddMesh( mesh );
			if ( detailed )
			{
				var surfaces = terrain.RequireElement( "DetailMaps" ).Elements( "DetailMap" ).Select( layer =>
					GameResource.Load<Surface>( Host.MountedPath( System.IO.Path.ChangeExtension( layer.Attribute( "materialpath" ) ?? "Data/Materials/default.xml", ".surface" ) ) ) ).ToArray();
				materials = new( new ImageSampler( Host, terrain.RequireElement( "WeightMap" ).Value ), surfaces, positions, uvs, indices );
				materials.AddCollision( builder );
			}
			else builder.AddCollisionMesh( positions, indices );
			var model = builder.Create();
			materials?.Bind( model );
			return model;
		}
		if(terrain.Element("ModelOverride") is {Value.Length:>0} replacement)
		{
			if ( detailed ) mat.Set( "g_flTerrain", 2f );
			var data=ObjLoader.Load(Host.FileSystem,Host.Resolve(replacement.Value),Vector3.Zero);
			return Complete( ObjLoader.CreateMesh( data, Path, mat ), data.Positions.ToArray(), data.Indices.ToArray(), data.UVs.ToArray() );
		}
		using var stream=Host.FileSystem.OpenRead(heightmap+".cache");
		using var reader=new BinaryReader(stream);
		if(reader.ReadUInt16()!=27) throw new InvalidDataException($"Unknown Overgrowth terrain cache version in {heightmap}");
		reader.ReadUInt32(); // heightmap and secondary UV checksums
		int count=reader.ReadInt32();
		if(count<3 || count>(stream.Length-stream.Position)/56) throw new InvalidDataException("Invalid terrain vertex count");
		var positions=new Vector3[count];
		var vertices=new TerrainVertex[count];
		Vector3 ReadVector()
		{
			float x=reader.ReadSingle(),y=reader.ReadSingle(),z=reader.ReadSingle();
			return new(z,x,y);
		}
		for(int i=0;i<count;i++) vertices[i].position=positions[i]=ReadVector()*40;
		for(int i=0;i<count;i++) vertices[i].normal=ReadVector();
		for(int i=0;i<count;i++) vertices[i].tangent=ReadVector();
		stream.Position+=count*12L;
		for(int i=0;i<count;i++) vertices[i].texcoord=new(reader.ReadSingle(),1-reader.ReadSingle());
		int faces=reader.ReadInt32();
		if(faces<1 || faces>(stream.Length-stream.Position)/12)throw new InvalidDataException("Invalid terrain triangle count");
		var indices=new int[checked(faces*3)];
		for(int i=0;i<indices.Length;i++)
		{
			int index=reader.ReadInt32();
			if((uint)index>=(uint)count) throw new InvalidDataException("Invalid terrain index");
			indices[i]=index;
		}
		// Face normals and the two vertex reorder tables precede the secondary UV stream.
		stream.Position += faces * 12L + sizeof(int);
		for ( int table = 0; table < 2; table++ )
		{
			var entries = reader.ReadInt32();
			if ( entries < 0 || entries > (stream.Length - stream.Position) / 4 )
				throw new InvalidDataException( "Invalid terrain reorder table" );
			stream.Position += entries * 4L;
		}
		int detailCount = reader.ReadInt32();
		// Some retail caches allocate three floats per vertex for this two-float stream.
		if ( detailCount != 0 && (detailCount < count || detailCount > (stream.Length - stream.Position) / 8) )
			throw new InvalidDataException( "Invalid terrain detail UV count" );
		for ( int i = 0; i < count; i++ )
		{
			var uv = detailCount > 0 ? new Vector2( reader.ReadSingle(), reader.ReadSingle() )
				: new Vector2( positions[i].y, positions[i].x ) / 40f;
			vertices[i].detailUv = new Vector2( uv.x * 0.1f, 1f - uv.y * 0.1f );
		}
		var mesh=new Mesh(mat);
		mesh.CreateVertexBuffer(vertices.Length,vertices);
		mesh.CreateIndexBuffer(indices.Length,indices);
		mesh.Bounds=BBox.FromPoints(positions);
		return Complete( mesh, positions, indices, detailed ? vertices.Select( v => v.texcoord ).ToArray() : null );
	}

	struct TerrainVertex
	{
		[VertexLayout.Position] public Vector3 position;
		[VertexLayout.Normal] public Vector3 normal;
		[VertexLayout.Tangent] public Vector3 tangent;
		[VertexLayout.TexCoord] public Vector2 texcoord;
		[VertexLayout.TexCoord( 1 )] public Vector2 detailUv;
	}
}
