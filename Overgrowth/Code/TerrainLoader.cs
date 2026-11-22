namespace Overgrowth;

// Model::ReadFromFile, as embedded after the six-byte terrain cache header.
internal sealed class TerrainLoader(string levelPath) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var terrain=XmlAsset.Read(Host.FileSystem,levelPath,"Terrain");
		var heightmap=Host.Resolve(terrain.Element("Heightmap").Value);
		var mat=Material.Create(Path+"/terrain.vmat","simple_color");
		if(terrain.Element("ColorMap") is {} color) mat.Set("Color",Host.LoadTexture(color.Value));
		var builder=Model.Builder.WithName(Path);
		if(terrain.Element("ModelOverride") is {Value.Length:>0} replacement)
		{
			var data=ObjLoader.Load(Host.FileSystem,Host.Resolve(replacement.Value),Vector3.Zero);
			return builder.AddMesh(ObjLoader.CreateMesh(data,Path,mat)).AddCollisionMesh(data.Positions,data.Indices).Create();
		}
		using var stream=Host.FileSystem.OpenRead(heightmap+".cache");
		using var reader=new BinaryReader(stream);
		if(reader.ReadUInt16()!=27) throw new InvalidDataException($"Unknown Overgrowth terrain cache version in {heightmap}");
		reader.ReadUInt32(); // heightmap and secondary UV checksums
		int count=reader.ReadInt32();
		if(count<3 || count>(stream.Length-stream.Position)/56) throw new InvalidDataException("Invalid terrain vertex count");
		var positions=new Vector3[count];
		var vertices=new SimpleVertex[count];
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
		var mesh=new Mesh(mat);
		mesh.CreateVertexBuffer(vertices.Length,vertices);
		mesh.CreateIndexBuffer(indices.Length,indices);
		mesh.Bounds=BBox.FromPoints(positions);
		return builder.AddMesh(mesh).AddCollisionMesh(positions,indices).Create();
	}
}
