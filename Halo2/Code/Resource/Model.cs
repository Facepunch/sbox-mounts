using Halo2.Blam;
using Sandbox.Mounting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Halo2;

/// <summary>
/// Loads a Halo 2 render_model (mode) tag and converts it to a Model.
/// References: Gravemind2401/Reclaimer - Halo2/Halo2Common.cs (ReadPcRenderModelMeshData)
/// </summary>
public partial class Halo2ModelLoader( MapFile mapFile, TagEntry tag, TagEntry phmoTag = null, TagEntry biped = null, TagEntry graph = null,
	TagEntry collision = null, TagEntry firstPersonWeapon = null, TagEntry collisionModel = null, string[] materialNames = null ) : ResourceLoader<Halo2Mount>
{
	WeakTarget[] ReadWeakTargets()
	{
		int id = mapFile.ReadInt32( mapFile.VaToOffset( (int)biped.DataVirtualAddress ) + 0x38 );
		if ( mapFile.GetTag( id & 0xffff ) is not { PrimaryClass: "tmlh", DataSize: >= 0x70 } definition ) return [];
		return Rows( mapFile.VaToOffset( (int)definition.DataVirtualAddress ) + 0x68, 28 )
			.Where( row => (mapFile.ReadInt32( row + 20 ) & 8) != 0 )
			.Select( row => new WeakTarget { Marker = Label( row ), Radius = BitConverter.Int32BitsToSingle( mapFile.ReadInt32( row + 4 ) ) * ScaleFactor } )
			.ToArray();
	}

	RenderModelTag ReadRenderModel()
	{
		var model = new RenderModelTag( mapFile, tag );
		if ( firstPersonWeapon is null ) return model;

		// Keep the arm mesh's vertex bone indices intact. The animation graph parents
		// the separate weapon skeleton under the right hand and also contains the camera.
		var weaponBones = new RenderModelTag( mapFile, firstPersonWeapon ).Nodes.ToDictionary( n => n.StringId );
		var indices = model.Nodes.Select( (n, i) => (n.StringId, i) ).ToDictionary( x => x.StringId, x => x.i );
		var rows = Rows( GraphHeader + 12, 32 ).ToArray();
		foreach ( int row in rows )
		{
			int id = mapFile.ReadInt32( row );
			if ( indices.ContainsKey( id ) ) continue;
			var bone = weaponBones.GetValueOrDefault( id ) ?? new Node { StringId = id, RotationW = 1 };
			int parent = (short)mapFile.ReadInt32( row + 8 );
			bone.ParentIndex = parent < 0 ? (short)-1 : (short)indices[mapFile.ReadInt32( rows[parent] )];
			indices.Add( id, model.Nodes.Count );
			model.Nodes.Add( bone );
		}
		return model;
	}

	BodyMaterial ReadBodyMaterial()
	{
		if ( collisionModel is not { DataSize: >= 0x68 } ) return null;
		int block = mapFile.VaToOffset( (int)collisionModel.DataVirtualAddress ) + 0x60;
		if ( mapFile.ReadInt32( block ) == 0 ) return null;
		int info = mapFile.VaToOffset( mapFile.ReadInt32( block + 4 ) );
		bool explosivesOnly = (mapFile.ReadInt32( info ) & 0x20) != 0;
		int index = (short)mapFile.ReadInt32( info + 0xce );
		if ( index >= 0 )
		{
			var name = materialNames is { Length: > 0 } ? materialNames.ElementAtOrDefault( index ) : Label( info + 4 );
			if ( !string.IsNullOrEmpty( name ) )
				return new BodyMaterial { SurfacePath = $"mount://{Host.Ident}/surfaces/{name}.surface", ExplosivesOnly = explosivesOnly };
		}
		return explosivesOnly ? new BodyMaterial { ExplosivesOnly = true } : null;
	}

	protected override object Load()
	{
		var model = ReadRenderModel();
		var builder = Model.Builder.WithName( Path );
		if ( biped is { DataSize: >= 0x15c } && Label( mapFile.VaToOffset( (int)biped.DataVirtualAddress ) + 0x158 ) is { Length: > 0 } hand )
			builder.WithData( new WeaponAttachment { PrimaryHand = hand } );
		if ( ReadBodyMaterial() is {} bodyMaterial ) builder.WithData( bodyMaterial );
		if ( HasFirstPersonWeapon && GetAnimationContext( "any", "any", "any" ) is {} context )
		{
			var actions = context.Actions.ToDictionary( a => a.Key, a => GetClipInfo( a.Value ) );
			var sounds = new Dictionary<string, CuePlayback>();
			foreach ( var cue in actions.Values.SelectMany( a => a.Cues ) )
			{
				if ( cue.Kind == "sound" && cue.TagPath is {} path && !sounds.ContainsKey( path )
					&& Host.GetByPath( Host.AssetPath( mapFile, path, "sound" ) ) is Halo2SoundLoader sound )
					sounds[path] = sound.Cue;
			}
			builder.WithData( new FirstPersonAnimation { Actions = actions, Sounds = sounds,
				Movement = context.Overlays.TryGetValue( "moving", out var movement ) ? GetClipInfo( movement ) : null } );
		}
		if ( biped is not null && ReadWeakTargets() is { Length: > 0 } weakTargets )
			builder.WithData( weakTargets );

		if ( model.Sections.Count == 0 || model.Regions.Count == 0 )
			return builder.Create();

		// Build skeleton
		var materials = new Dictionary<int, Material>( model.Shaders.Count );
		var bones = BuildSkeleton( model, mapFile );

		foreach ( var region in model.Regions )
		{
			string regionName = mapFile.ResolveStringId( region.StringId ) ?? $"region_{region.StringId}";

			for ( int permIdx = 0; permIdx < region.Permutations.Count; permIdx++ )
			{
				var perm = region.Permutations[permIdx];
				// The editor traces a single static mesh in the default bodygroup configuration.
				bool needsTraceMesh = Application.IsEditor && permIdx == 0;

				// Halo 2 LOD levels: SuperHigh(0), High(1), Medium(2), Low(3), SuperLow(4), Potato(5)
				ReadOnlySpan<int> lodSections =
				[
					perm.SuperHighSectionIndex,
					perm.HighSectionIndex,
					perm.MediumSectionIndex,
					perm.LowSectionIndex,
					perm.SuperLowSectionIndex,
					perm.PotatoSectionIndex
				];

				for ( int lod = 0; lod < lodSections.Length; lod++ )
				{
					int sectionIndex = lodSections[lod];
					if ( sectionIndex < 0 || sectionIndex >= model.Sections.Count )
						continue;

					// Skip if this section was already loaded at a higher detail LOD
					if ( lodSections[..lod].Contains( sectionIndex ) )
						continue;

					var section = model.Sections[sectionIndex];
					if ( section.VertexCount == 0 || section.DataSize == 0 )
						continue;

					try
					{
						var bounds = model.BoundingBoxes.Count > 0 ? model.BoundingBoxes[0] : null;
						if ( BuildSectionMeshes( Host.FileSystem, mapFile, model, section, sectionIndex, builder, bounds, lod, regionName, permIdx, materials, bones, needsTraceMesh ) )
							needsTraceMesh = false;
					}
					catch ( Exception ex )
					{
						Log.Warning( $"[Halo2] Failed to load section {sectionIndex}: {ex.Message}" );
					}
				}
			}
		}

		if ( bones.Count > 0 )
			builder.AddBones( [.. bones] );

		AddAttachments( mapFile, tag, builder, bones );

		if ( collision is not null && bones.Count > 0 )
			CollisionModel.AddHitboxes( mapFile, collision, model, builder, ScaleFactor, collisionModel, materialNames );

		if ( graph is not null && bones.Count > 0 )
			Blam.AnimationGraph.AddSequences( mapFile, graph, model, builder, ScaleFactor );

		// Physics bodies can be bound to a bone or authored in model space.
		if ( phmoTag != null )
		{
			try
			{
				var phmo = new PhysicsModelTag( mapFile, phmoTag );
				BuildPhysics( phmo, model, mapFile, builder, bones );
			}
			catch ( Exception ex )
			{
				Log.Warning( $"[Halo2] Failed to load physics: {ex.Message}" );
			}
		}

		return builder.Create();
	}

	bool BuildSectionMeshes( BaseFileSystem fileSystem, MapFile map, RenderModelTag model, Section section, int sectionIndex, ModelBuilder builder, BoundingBox bounds, int lod, string regionName, int permIndex, Dictionary<int, Material> materials, List<ModelBuilder.Bone> bones, bool addTraceMesh )
	{
		// Read geometry blob via DataPointer
		var blobData = ReadSectionBlob( fileSystem, map, section );
		if ( blobData == null || blobData.Length == 0 )
			return false;

		using var ms = new MemoryStream( blobData );
		using var reader = new BinaryReader( ms );

		int baseAddress = section.BaseAddress;

		// Find resource blocks by type
		// MCC resource types: submesh=first, index=32, vertex=56/0, uv=56/1, normals=56/2, nodeMap=104
		ResourceInfo submeshResource = section.Resources.Count > 0 ? section.Resources[0] : null;
		ResourceInfo indexResource = null;
		ResourceInfo vertexResource = null;
		ResourceInfo uvResource = null;
		ResourceInfo normalsResource = null;
		ResourceInfo nodeMapResource = null;

		foreach ( var res in section.Resources )
		{
			if ( res == submeshResource )
				continue;
			if ( res.Type0 == 32 )
				indexResource = res;
			else if ( res.Type0 == 56 && res.Type1 == 0 )
				vertexResource = res;
			else if ( res.Type0 == 56 && res.Type1 == 1 )
				uvResource = res;
			else if ( res.Type0 == 56 && res.Type1 == 2 )
				normalsResource = res;
			else if ( res.Type0 == 104 )
				nodeMapResource = res;
		}

		if ( vertexResource == null || indexResource == null || submeshResource == null )
			return false;

		// Read index count from blob header (offset 40 for MCC, per Reclaimer MeshResourceDetailsBlock)
		reader.BaseStream.Seek( 40, SeekOrigin.Begin );
		int indexCount = reader.ReadUInt16();

		// Read submeshes (72 bytes each)
		int submeshCount = submeshResource.Size / 72;
		var submeshes = new List<Submesh>( submeshCount );
		for ( int i = 0; i < submeshCount; i++ )
		{
			reader.BaseStream.Seek( baseAddress + submeshResource.Offset + i * 72, SeekOrigin.Begin );
			ushort partType = reader.ReadUInt16();
			reader.ReadUInt16(); // flags
			short shaderIndex = reader.ReadInt16(); // +4
			ushort indexStart = reader.ReadUInt16(); // +6
			ushort indexLength = reader.ReadUInt16(); // +8
			submeshes.Add( new Submesh { ShaderIndex = shaderIndex, IndexStart = indexStart, IndexLength = indexLength,
				Influences = partType is 4 or 5 ? section.TransparentNodesPerVertex : section.NodesPerVertex } );
		}

		// Read indices
		reader.BaseStream.Seek( baseAddress + indexResource.Offset, SeekOrigin.Begin );
		var indices = new ushort[indexCount];
		for ( int i = 0; i < indexCount; i++ )
			indices[i] = reader.ReadUInt16();

		// Determine if triangle list or strip
		bool isTriList = section.FaceCount * 3 == indexCount;

		// Brute-force hack from Reclaimer: detect strips where FaceCount*3 happens to equal IndexCount
		if ( isTriList )
		{
			// Check if any of the first 10 triangles have duplicate indices (degenerate = strip indicator)
			for ( int i = 0; i < Math.Min( indexCount - 2, 30 ); i += 3 )
			{
				if ( indices[i] == indices[i + 1] || indices[i + 1] == indices[i + 2] || indices[i] == indices[i + 2] )
				{
					isTriList = false;
					break;
				}
			}
		}

		if ( submeshes.Count == 0 ) return false;
		SkinnedVertex[] Vertices( int influences ) => ReadVertices( reader, section, bounds, vertexResource, uvResource, normalsResource, nodeMapResource, influences );
		var verts = Vertices( submeshes[0].Influences );
		SkinnedVertex[] alternate = null;
		var visited = new int[verts.Length];
		int part = 0;
		var traceIndices = addTraceMesh ? new List<int>() : null;

		// Build mesh for each submesh
		foreach ( var submesh in submeshes )
		{
			var triIndices = isTriList
				? ExtractTriangleList( indices, submesh.IndexStart, submesh.IndexLength )
				: ConvertTriStrip( indices, submesh.IndexStart, submesh.IndexLength );

			if ( triIndices.Count == 0 )
				continue;

			var index = submesh.ShaderIndex;
			int shaderId = (uint)index < model.Shaders.Count ? model.Shaders[index].TagIndex : -1;
			if ( !materials.TryGetValue( shaderId, out var material ) )
				materials[shaderId] = material = LoadMaterial( map, model, index );

			var mesh = new Mesh( $"section{sectionIndex}", material );
			var partVertices = section.GeometryClassification == 3 && submesh.Influences != submeshes[0].Influences
				? alternate ??= Vertices( submesh.Influences ) : verts;
			mesh.CreateVertexBuffer( partVertices.Length, partVertices );
			mesh.CreateIndexBuffer( triIndices.Count, triIndices );
			var meshBounds = BBox.FromPositionAndSize( partVertices[triIndices[0]].position );
			part++;
			foreach ( int vertexIndex in triIndices )
			{
				if ( visited[vertexIndex] == part ) continue;
				visited[vertexIndex] = part;
				ref readonly var vertex = ref partVertices[vertexIndex];
				meshBounds = meshBounds.AddPoint( vertex.position );
				var weights = vertex.blendWeights;
				var ids = vertex.blendIndices;
				Include( ids.r, weights.r, vertex.position );
				Include( ids.g, weights.g, vertex.position );
				Include( ids.b, weights.b, vertex.position );
				Include( ids.a, weights.a, vertex.position );
			}
			mesh.Bounds = meshBounds;
			builder.AddMesh( mesh, lod, regionName, permIndex );
			traceIndices?.AddRange( triIndices );
		}

		if ( traceIndices is not { Count: > 0 } ) return false;
		builder.AddTraceMesh( verts.Select( v => v.position ).ToArray(), CollectionsMarshal.AsSpan( traceIndices ) );
		return true;

		void Include( byte index, byte weight, Vector3 position )
		{
			if ( weight == 0 || index >= bones.Count ) return;
			var bone = bones[index];
			var local = bone.Rotation.Inverse * (position - bone.Position);
			bones[index] = bone with { Bounds = bone.Bounds?.AddPoint( local ) ?? BBox.FromPositionAndSize( local ) };
		}
	}

	static SkinnedVertex[] ReadVertices( BinaryReader reader, Section section, BoundingBox bounds,
		ResourceInfo positions, ResourceInfo uvs, ResourceInfo normals, ResourceInfo nodeMap, int influences )
	{
		var stream = reader.BaseStream;
		int stride = positions.Size / section.VertexCount;
		var center = Vector3.Zero;
		var range = Vector3.One;
		var uvCenter = Vector2.Zero;
		var uvRange = Vector2.One;
		if ( bounds is not null )
		{
			var max = new Vector3( bounds.XMax, bounds.YMax, bounds.ZMax );
			center = (new Vector3( bounds.XMin, bounds.YMin, bounds.ZMin ) + max) * 0.5f;
			range = max - center;
			var uvMax = new Vector2( bounds.UMax, bounds.VMax );
			uvCenter = (new Vector2( bounds.UMin, bounds.VMin ) + uvMax) * 0.5f;
			uvRange = uvMax - uvCenter;
		}

		var bones = Array.Empty<byte>();
		if ( nodeMap is { Size: > 0 } )
		{
			stream.Position = section.BaseAddress + nodeMap.Offset;
			bones = reader.ReadBytes( nodeMap.Size );
		}

		byte ReadBone()
		{
			byte index = reader.ReadByte();
			return index < bones.Length ? bones[index] : index;
		}

		var vertices = new SkinnedVertex[section.VertexCount];
		for ( int i = 0; i < vertices.Length; i++ )
		{
			stream.Position = section.BaseAddress + positions.Offset + i * stride;
			var raw = new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
			var position = ConvertPosition( center + raw * range );
			var indices = new Color32( 0, 0, 0, 0 );
			var weights = new Color32( 255, 0, 0, 0 );
			if ( section.GeometryClassification == 1 && section.NodesPerVertex >= 1 && bones.Length > 0 )
			{
				indices.r = bones[0];
			}
			else if ( section.GeometryClassification >= 2 && stride > 12 )
			{
				if ( section.GeometryClassification == 2 )
				{
					indices.r = ReadBone();
				}
				else if ( section.GeometryClassification == 3 )
				{
					indices.r = ReadBone();
					if ( stride >= 20 && influences > 1 )
					{
						indices.g = ReadBone();
						if ( influences > 2 ) indices.b = ReadBone();
						if ( influences > 3 ) indices.a = ReadBone();
						stream.Position = section.BaseAddress + positions.Offset + i * stride + 17;
						weights.g = reader.ReadByte();
						if ( influences > 2 ) weights.b = reader.ReadByte();
						if ( influences > 3 ) weights.a = reader.ReadByte();
						// Halo's vertex shader derives the first weight from the remaining active lanes.
						weights.r = (byte)Math.Max( 0, 255 - weights.g - weights.b - weights.a );
					}
				}
				else
				{
					weights.r = 0;
				}
			}

			var uv = Vector2.Zero;
			if ( uvs is not null )
			{
				stream.Position = section.BaseAddress + uvs.Offset + i * 8;
				uv = uvCenter + new Vector2( reader.ReadSingle(), reader.ReadSingle() ) * uvRange;
			}

			var normal = Vector3.Up;
			var tangent = new Vector4( 1, 0, 0, 1 );
			if ( normals is not null )
			{
				stream.Position = section.BaseAddress + normals.Offset + i * 36;
				var direction = new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() ).Normal;
				if ( direction != Vector3.Zero ) normal = direction;
				var bitangent = new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
				var u = new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() ).Normal;
				tangent = new Vector4( u, Vector3.Dot( Vector3.Cross( normal, u ), bitangent ) < 0 ? -1 : 1 );
			}
			vertices[i] = new SkinnedVertex( position, normal, tangent, uv, indices, weights );
		}
		return vertices;
	}

	static byte[] ReadSectionBlob( BaseFileSystem fileSystem, MapFile map, Section section )
	{
		if ( section.DataSize <= 0 )
			return null;

		// Pass full pointer to ReadRawData which handles location bits and absolute addressing
		return map.ReadRawData( fileSystem, section.DataPointer, section.DataSize );
	}

	static List<int> ExtractTriangleList( ushort[] indices, int start, int length )
	{
		var result = new List<int>( length );
		for ( int i = start; i < start + length && i < indices.Length; i++ )
			result.Add( indices[i] );
		return result;
	}

	static List<int> ConvertTriStrip( ushort[] indices, int start, int length )
	{
		var result = new List<int>();
		int end = Math.Min( start + length, indices.Length );

		for ( int i = start + 2; i < end; i++ )
		{
			int a = indices[i - 2];
			int b = indices[i - 1];
			int c = indices[i];

			if ( a == b || b == c || a == c )
				continue;
			if ( a == 0xFFFF || b == 0xFFFF || c == 0xFFFF )
				continue;

			if ( (i - start) % 2 == 0 )
			{
				result.Add( a );
				result.Add( b );
				result.Add( c );
			}
			else
			{
				result.Add( b );
				result.Add( a );
				result.Add( c );
			}
		}

		return result;
	}

	Material LoadMaterial( MapFile map, RenderModelTag model, int shaderIndex )
	{
		if ( shaderIndex < 0 || shaderIndex >= model.Shaders.Count )
			return Material.Load( "materials/dev/primary_white.vmat" );

		var shaderRef = model.Shaders[shaderIndex];
		var shaderTag = map.GetTag( shaderRef.TagIndex );
		if ( shaderTag == null || string.IsNullOrEmpty( shaderTag.TagPath ) )
			return Material.Load( "materials/dev/primary_white.vmat" );

		try
		{
			var path = Host.AssetPath( map, shaderTag.TagPath, "vmat" );
			if ( biped is not null && Host.GetByPath( path ) is Halo2MaterialLoader loader )
				path = loader.PathForObject( biped );
			return Material.Load( path ) ?? Material.Load( "materials/dev/primary_white.vmat" );
		}
		catch
		{
			return Material.Load( "materials/dev/primary_white.vmat" );
		}
	}

	static void AddAttachments( MapFile map, TagEntry tag, ModelBuilder builder, List<ModelBuilder.Bone> bones )
	{
		// MCC render models store named groups of 36-byte, bone-local markers.
		int block = map.VaToOffset( (int)tag.DataVirtualAddress ) + 0x58;
		int count = map.ReadInt32( block );
		if ( count == 0 ) return;
		int groups = map.VaToOffset( map.ReadInt32( block + 4 ) );
		var names = new string[count];
		var reserved = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		for ( int i = 0; i < count; i++ )
		{
			names[i] = map.ResolveStringId( map.ReadInt32( groups + i * 12 ) );
			if ( names[i] is { Length: > 0 } name ) reserved.Add( name );
		}

		var used = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		for ( int i = 0; i < count; i++ )
		{
			string name = names[i];
			if ( string.IsNullOrWhiteSpace( name ) ) continue;
			int group = groups + i * 12;
			int markers = map.ReadInt32( group + 4 );
			if ( markers == 0 ) continue;
			using var reader = map.OpenReader( map.VaToOffset( map.ReadInt32( group + 8 ) ), checked(markers * 36) );
			int suffix = 2;
			for ( int j = 0; j < markers; j++ )
			{
				reader.BaseStream.Position = j * 36 + 2;
				int node = reader.ReadByte();
				reader.ReadByte();
				var position = new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
				var rotation = new Rotation( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
				if ( node != byte.MaxValue && node >= bones.Count ) continue;

				string attachment = name;
				if ( !used.Add( attachment ) )
				{
					do { attachment = $"{name}_{suffix++}"; }
					while ( reserved.Contains( attachment ) || !used.Add( attachment ) );
				}
				builder.AddAttachment( attachment, ConvertPosition( position ), rotation, node == byte.MaxValue ? null : bones[node].Name );
			}
		}
	}

	static List<ModelBuilder.Bone> BuildSkeleton( RenderModelTag model, MapFile map )
	{
		var bones = new List<ModelBuilder.Bone>();
		var worldTransforms = new Transform[model.Nodes.Count];

		for ( int i = 0; i < model.Nodes.Count; i++ )
		{
			var node = model.Nodes[i];
			int parentIdx = node.ParentIndex;

			var localPos = ConvertPosition( new Vector3( node.PositionX, node.PositionY, node.PositionZ ) );
			var localRot = new Rotation( node.RotationX, node.RotationY, node.RotationZ, node.RotationW );
			var localTx = new Transform( localPos, localRot );

			bool hasParent = parentIdx >= 0 && parentIdx < worldTransforms.Length;
			worldTransforms[i] = hasParent ? worldTransforms[parentIdx].ToWorld( localTx ) : localTx;

			string name = map.ResolveStringId( node.StringId ) ?? $"bone_{i}";
			string parentName = hasParent ? bones[parentIdx].Name : null;

			bones.Add( new ModelBuilder.Bone(
				name, parentName,
				worldTransforms[i].Position,
				worldTransforms[i].Rotation
			) );
		}

		return bones;
	}

	// Halo 2 uses "world units" where 1 WU = 10 feet = 3.048 meters = 120 inches
	const float ScaleFactor = 120.0f;
	static Vector3 ConvertPosition( Vector3 v ) => v * ScaleFactor;

	static void BuildPhysics( PhysicsModelTag phmo, RenderModelTag model, MapFile map,
		ModelBuilder builder, List<ModelBuilder.Bone> bones )
	{
		if ( phmo.RigidBodies.Count == 0 )
			return;

		// Build mapping from phmo node index Ã¢â€ â€™ render model bone index.
		// Try name-based matching first (phmo has its own node list).
		// Fall back to direct indexing if no phmo nodes or no matches found.
		var phmoToRenderBone = new Dictionary<int, int>();

		if ( phmo.Nodes.Count > 0 )
		{
			var renderNodeNameMap = new Dictionary<string, int>();
			for ( int i = 0; i < model.Nodes.Count; i++ )
			{
				var name = map.ResolveStringId( model.Nodes[i].StringId );
				if ( name != null )
					renderNodeNameMap[name] = i;
			}

			for ( int i = 0; i < phmo.Nodes.Count; i++ )
			{
				var name = map.ResolveStringId( phmo.Nodes[i].NameStringId );
				if ( name != null && renderNodeNameMap.TryGetValue( name, out int renderIdx ) )
					phmoToRenderBone[i] = renderIdx;
			}
		}

		// If name matching produced no results, assume NodeIndex is a direct render model bone index
		bool directMapping = phmoToRenderBone.Count == 0;
		var bodyIndexMap = new Dictionary<int, int>();
		int builderBodyIdx = 0;

		for ( int i = 0; i < phmo.RigidBodies.Count; i++ )
		{
			var rb = phmo.RigidBodies[i];
			string boneName = null;
			var bonePose = Transform.Zero;
			if ( rb.NodeIndex != -1 )
			{
				int boneIdx = rb.NodeIndex;
				if ( !directMapping && !phmoToRenderBone.TryGetValue( rb.NodeIndex, out boneIdx ) ) continue;
				if ( (uint)boneIdx >= bones.Count ) continue;
				var bone = bones[boneIdx];
				boneName = bone.Name;
				bonePose = new Transform( bone.Position, bone.Rotation );
			}

			var body = builder.AddBody( mass: rb.Mass > 0 ? rb.Mass : 10f, boneName: boneName );
			body.SetBindPose( bonePose );
			body.SetDamping( rb.LinearDamping, rb.AngularDamping );
			body.SetInertiaScale( rb.InertiaScale == 0 ? 1 : rb.InertiaScale );

			if ( rb.ShapeType == 14 && (uint)rb.ShapeIndex < phmo.ShapeLists.Count )
			{
				foreach ( var child in phmo.ShapeLists[rb.ShapeIndex] )
					AddCollisionShape( body, phmo, child.Type, child.Index );
			}
			else
			{
				AddCollisionShape( body, phmo, rb.ShapeType, rb.ShapeIndex );
			}

			bodyIndexMap[i] = builderBodyIdx++;
		}

		// Add constraints from node edges
		foreach ( var edge in phmo.NodeEdges )
		{
			foreach ( var cref in edge.Constraints )
			{
				switch ( cref.Type )
				{
					case 2: // Ragdoll
						if ( cref.Index >= 0 && cref.Index < phmo.RagdollConstraints.Count )
							AddRagdollJoint( phmo.RagdollConstraints[cref.Index], cref.Friction, phmo, builder, bones, bodyIndexMap );
						break;

					case 1: // Limited Hinge
						if ( cref.Index >= 0 && cref.Index < phmo.LimitedHingeConstraints.Count )
							AddLimitedHingeJoint( phmo.LimitedHingeConstraints[cref.Index], cref.Friction, phmo, builder, bones, bodyIndexMap );
						break;

					case 0: // Hinge (free rotation)
						if ( cref.Index >= 0 && cref.Index < phmo.HingeConstraints.Count )
							AddHingeJoint( phmo.HingeConstraints[cref.Index], cref.Friction, phmo, builder, bones, bodyIndexMap );
						break;

					case 4: // Ball and Socket
						if ( cref.Index >= 0 && cref.Index < phmo.BallSocketConstraints.Count )
							AddBallJoint( phmo.BallSocketConstraints[cref.Index], cref.Friction, phmo, builder, bones, bodyIndexMap );
						break;
				}
			}
		}
	}

	static void AddCollisionShape( PhysicsBodyBuilder body, PhysicsModelTag phmo, int type, int index )
	{
		switch ( type )
		{
			case 0: // Sphere
				if ( index >= 0 && index < phmo.Spheres.Count )
				{
					var sph = phmo.Spheres[index];
					var center = new Vector3( sph.CenterX, sph.CenterY, sph.CenterZ ) * ScaleFactor;
					body.AddSphere( new Sphere( center, sph.Radius * ScaleFactor ) );
				}
				break;

			case 1: // Pill (Capsule)
				if ( index >= 0 && index < phmo.Pills.Count )
				{
					var pill = phmo.Pills[index];
					var bottom = new Vector3( pill.BottomX, pill.BottomY, pill.BottomZ ) * ScaleFactor;
					var top = new Vector3( pill.TopX, pill.TopY, pill.TopZ ) * ScaleFactor;
					body.AddCapsule( new Capsule( bottom, top, pill.Radius * ScaleFactor ) );
				}
				break;

			case 4: // Polyhedron
				if ( (uint)index < phmo.Polyhedra.Count && phmo.Polyhedra[index].Length >= 4 )
					body.AddHull( Array.ConvertAll( phmo.Polyhedra[index], ConvertPosition ) );
				break;

			case 2: // Box (as convex hull)
				if ( index >= 0 && index < phmo.Boxes.Count )
				{
					var box = phmo.Boxes[index];
					var he = new Vector3( box.HalfExtentX, box.HalfExtentY, box.HalfExtentZ ) * ScaleFactor;
					var center = new Vector3( box.TransX, box.TransY, box.TransZ ) * ScaleFactor;

					var ri = new Vector3( box.RotIX, box.RotIY, box.RotIZ );
					var rj = new Vector3( box.RotJX, box.RotJY, box.RotJZ );
					var rk = new Vector3( box.RotKX, box.RotKY, box.RotKZ );

					var points = new Vector3[8];
					int idx = 0;
					for ( int sx = -1; sx <= 1; sx += 2 )
					for ( int sy = -1; sy <= 1; sy += 2 )
					for ( int sz = -1; sz <= 1; sz += 2 )
					{
						points[idx++] = center + ri * (he.x * sx) + rj * (he.y * sy) + rk * (he.z * sz);
					}

					body.AddHull( points );
				}
				break;
		}
	}

	static bool FindBodyIndices( ConstraintBase c, PhysicsModelTag phmo, Dictionary<int, int> bodyIndexMap,
		out int body1, out int body2 )
	{
		body1 = -1;
		body2 = -1;

		// Find rigid bodies that reference these nodes
		for ( int i = 0; i < phmo.RigidBodies.Count; i++ )
		{
			if ( phmo.RigidBodies[i].NodeIndex == c.NodeAIndex && bodyIndexMap.ContainsKey( i ) )
				body1 = bodyIndexMap[i];
			if ( phmo.RigidBodies[i].NodeIndex == c.NodeBIndex && bodyIndexMap.ContainsKey( i ) )
				body2 = bodyIndexMap[i];
		}

		return body1 >= 0 && body2 >= 0 && body1 != body2;
	}

	static (Transform frame1, Transform frame2) BuildJointFrames( ConstraintBase c, List<ModelBuilder.Bone> bones )
	{
		// Constraint positions are in bone-local space, scale to s&box units
		var aPos = new Vector3( c.APosX, c.APosY, c.APosZ ) * ScaleFactor;
		var bPos = new Vector3( c.BPosX, c.BPosY, c.BPosZ ) * ScaleFactor;

		var t1 = BuildConstraintFrame( aPos, new( c.AForwardX, c.AForwardY, c.AForwardZ ),
			new( c.ALeftX, c.ALeftY, c.ALeftZ ), new( c.AUpX, c.AUpY, c.AUpZ ) );
		var t2 = BuildConstraintFrame( bPos, new( c.BForwardX, c.BForwardY, c.BForwardZ ),
			new( c.BLeftX, c.BLeftY, c.BLeftZ ), new( c.BUpX, c.BUpY, c.BUpZ ) );

		return (t1, t2);
	}

	static Transform BuildConstraintFrame( Vector3 position, Vector3 forward, Vector3 left, Vector3 up )
	{
		if ( !float.IsFinite( position.LengthSquared ) || !float.IsFinite( forward.LengthSquared )
			|| !float.IsFinite( left.LengthSquared ) || !float.IsFinite( up.LengthSquared )
			|| Math.Abs( forward.LengthSquared - 1 ) > 0.02f || Math.Abs( left.LengthSquared - 1 ) > 0.02f
			|| Math.Abs( up.LengthSquared - 1 ) > 0.02f || Vector3.Cross( forward, left ).Distance( up ) > 0.02f )
			throw new InvalidDataException( "Halo2 physics constraint has an invalid local frame." );
		return new Transform( position, Rotation.LookAt( up, forward ) );
	}

	static void AddRagdollJoint( RagdollConstraint c, float edgeFriction, PhysicsModelTag phmo, ModelBuilder builder,
		List<ModelBuilder.Bone> bones, Dictionary<int, int> bodyIndexMap )
	{
		if ( !FindBodyIndices( c, phmo, bodyIndexMap, out int body1, out int body2 ) )
			return;

		var (frame1, frame2) = BuildJointFrames( c, bones );
		// MCC stores these angular limits in degrees. The engine uses one circular swing cone.
		float swing = MathF.Max( c.ConeMax - c.ConeMin, c.PlaneMax - c.PlaneMin ) * 0.5f;
		float torque = (edgeFriction != 0 ? edgeFriction : c.MaxFrictionTorque) * ScaleFactor * ScaleFactor;
		if ( swing == 0 )
		{
			if ( c.TwistMin == c.TwistMax )
				builder.AddFixedJoint( body1, body2, frame1, frame2 );
			else
			{
				var hinge = builder.AddHingeJoint( body1, body2, frame1, frame2 ).WithTargetVelocity( Vector3.Zero ).WithMaxTorque( torque );
				if ( c.TwistMax - c.TwistMin < 360f )
					hinge.WithTwistLimit( c.TwistMin, c.TwistMax );
			}
			return;
		}
		var ball = builder.AddBallJoint( body1, body2, frame1, frame2 ).WithTargetVelocity( Vector3.Zero ).WithMaxTorque( torque );
		if ( swing < 180f )
			ball.WithSwingLimit( Math.Clamp( swing, 0, 89.1f ) );
		if ( c.TwistMax - c.TwistMin < 360f )
			ball.WithTwistLimit( c.TwistMin, c.TwistMax );
	}

	static void AddLimitedHingeJoint( LimitedHingeConstraint c, float edgeFriction, PhysicsModelTag phmo, ModelBuilder builder,
		List<ModelBuilder.Bone> bones, Dictionary<int, int> bodyIndexMap )
	{
		if ( !FindBodyIndices( c, phmo, bodyIndexMap, out int body1, out int body2 ) )
			return;

		var (frame1, frame2) = BuildJointFrames( c, bones );

		builder.AddHingeJoint( body1, body2, frame1, frame2 )
			.WithTwistLimit( c.LimitMin, c.LimitMax )
			.WithTargetVelocity( Vector3.Zero ).WithMaxTorque( (edgeFriction != 0 ? edgeFriction : c.LimitFriction) * ScaleFactor * ScaleFactor );
	}

	static void AddHingeJoint( HingeConstraint c, float edgeFriction, PhysicsModelTag phmo, ModelBuilder builder,
		List<ModelBuilder.Bone> bones, Dictionary<int, int> bodyIndexMap )
	{

		if ( !FindBodyIndices( c, phmo, bodyIndexMap, out int body1, out int body2 ) )
			return;

		var (frame1, frame2) = BuildJointFrames( c, bones );
		var joint = builder.AddHingeJoint( body1, body2, frame1, frame2 );
		if ( edgeFriction != 0 )
			joint.WithTargetVelocity( Vector3.Zero ).WithMaxTorque( edgeFriction * ScaleFactor * ScaleFactor );
	}

	static void AddBallJoint( BallSocketConstraint c, float edgeFriction, PhysicsModelTag phmo, ModelBuilder builder,
		List<ModelBuilder.Bone> bones, Dictionary<int, int> bodyIndexMap )
	{
		if ( !FindBodyIndices( c, phmo, bodyIndexMap, out int body1, out int body2 ) )
			return;

		var (frame1, frame2) = BuildJointFrames( c, bones );
		var joint = builder.AddBallJoint( body1, body2, frame1, frame2 );
		if ( edgeFriction != 0 )
			joint.WithTargetVelocity( Vector3.Zero ).WithMaxTorque( edgeFriction * ScaleFactor * ScaleFactor );
	}

	struct Submesh
	{
		public byte Influences;
		public short ShaderIndex;
		public ushort IndexStart;
		public ushort IndexLength;
	}
}

[StructLayout( LayoutKind.Sequential )]
public struct SkinnedVertex( Vector3 position, Vector3 normal, Vector4 tangent, Vector2 texcoord, Color32 blendIndices, Color32 blendWeights )
{
	[VertexLayout.Position]
	public Vector3 position = position;

	[VertexLayout.Normal]
	public Vector3 normal = normal;

	[VertexLayout.Tangent]
	public Vector4 tangent = tangent;

	[VertexLayout.TexCoord]
	public Vector2 texcoord = texcoord;

	[VertexLayout.BlendIndices]
	public Color32 blendIndices = blendIndices;

	[VertexLayout.BlendWeight]
	public Color32 blendWeights = blendWeights;
}
