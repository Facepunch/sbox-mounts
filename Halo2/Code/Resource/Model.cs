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
public class Halo2ModelLoader( MapFile mapFile, TagEntry tag, TagEntry phmoTag = null ) : ResourceLoader<Halo2Mount>
{
	protected override object Load()
	{
		var model = new RenderModelTag( mapFile, tag );
		var mapName = mapFile.Header.MapName;
		var builder = Model.Builder.WithName( Path );

		if ( model.Sections.Count == 0 || model.Regions.Count == 0 )
			return builder.Create();

		// Build skeleton
		var bones = BuildSkeleton( model, mapFile );

		foreach ( var region in model.Regions )
		{
			string regionName = mapFile.ResolveStringId( region.StringId ) ?? $"region_{region.StringId}";

			for ( int permIdx = 0; permIdx < region.Permutations.Count; permIdx++ )
			{
				var perm = region.Permutations[permIdx];

				// Halo 2 LOD levels: SuperHigh(0), High(1), Medium(2), Low(3), SuperLow(4), Potato(5)
				int[] lodSections =
				[
					perm.SuperHighSectionIndex,
					perm.HighSectionIndex,
					perm.MediumSectionIndex,
					perm.LowSectionIndex,
					perm.SuperLowSectionIndex,
					perm.PotatoSectionIndex
				];

				var loadedSections = new HashSet<int>();

				for ( int lod = 0; lod < lodSections.Length; lod++ )
				{
					int sectionIndex = lodSections[lod];
					if ( sectionIndex < 0 || sectionIndex >= model.Sections.Count )
						continue;

					// Skip if this section was already loaded at a higher detail LOD
					if ( !loadedSections.Add( sectionIndex ) )
						continue;

					var section = model.Sections[sectionIndex];
					if ( section.VertexCount == 0 || section.DataSize == 0 )
						continue;

					try
					{
						var bounds = model.BoundingBoxes.Count > 0 ? model.BoundingBoxes[0] : null;
						BuildSectionMeshes( Host.FileSystem, mapFile, model, section, sectionIndex, mapName, builder, bounds, lod, regionName, permIdx );
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

		// Add ragdoll physics if phmo tag exists
		if ( phmoTag != null && bones.Count > 0 )
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

	static void BuildSectionMeshes( BaseFileSystem fileSystem, MapFile map, RenderModelTag model, Section section, int sectionIndex, string mapName, ModelBuilder builder, BoundingBox bounds, int lod, string regionName, int permIndex )
	{
		// Read geometry blob via DataPointer
		var blobData = ReadSectionBlob( fileSystem, map, section );
		if ( blobData == null || blobData.Length == 0 )
			return;

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
			return;

		// Read index count from blob header (offset 40 for MCC, per Reclaimer MeshResourceDetailsBlock)
		reader.BaseStream.Seek( 40, SeekOrigin.Begin );
		int indexCount = reader.ReadUInt16();

		// Read submeshes (72 bytes each)
		int submeshCount = submeshResource.Size / 72;
		var submeshes = new List<Submesh>( submeshCount );
		for ( int i = 0; i < submeshCount; i++ )
		{
			reader.BaseStream.Seek( baseAddress + submeshResource.Offset + i * 72, SeekOrigin.Begin );
			reader.ReadInt32(); // +0: unknown
			short shaderIndex = reader.ReadInt16(); // +4
			ushort indexStart = reader.ReadUInt16(); // +6
			ushort indexLength = reader.ReadUInt16(); // +8
			submeshes.Add( new Submesh { ShaderIndex = shaderIndex, IndexStart = indexStart, IndexLength = indexLength } );
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

		// Read vertex positions (float3, 12 bytes min) — compressed on PC, decompress with bounding box
		int vertexSize = vertexResource.Size / section.VertexCount;
		var positions = new Vector3[section.VertexCount];

		// PC compression bounds: Min=Midpoint, so actual = midpoint + raw * (max - midpoint)
		float posXMid = 0, posYMid = 0, posZMid = 0;
		float posXRange = 1, posYRange = 1, posZRange = 1;
		float uvUMid = 0, uvVMid = 0;
		float uvURange = 1, uvVRange = 1;

		if ( bounds != null )
		{
			posXMid = (bounds.XMin + bounds.XMax) * 0.5f;
			posYMid = (bounds.YMin + bounds.YMax) * 0.5f;
			posZMid = (bounds.ZMin + bounds.ZMax) * 0.5f;
			posXRange = bounds.XMax - posXMid;
			posYRange = bounds.YMax - posYMid;
			posZRange = bounds.ZMax - posZMid;
			uvUMid = (bounds.UMin + bounds.UMax) * 0.5f;
			uvVMid = (bounds.VMin + bounds.VMax) * 0.5f;
			uvURange = bounds.UMax - uvUMid;
			uvVRange = bounds.VMax - uvVMid;
		}

		for ( int i = 0; i < section.VertexCount; i++ )
		{
			reader.BaseStream.Seek( baseAddress + vertexResource.Offset + i * vertexSize, SeekOrigin.Begin );
			float x = reader.ReadSingle();
			float y = reader.ReadSingle();
			float z = reader.ReadSingle();
			positions[i] = new Vector3(
				posXMid + x * posXRange,
				posYMid + y * posYRange,
				posZMid + z * posZRange
			);
		}

		// Read UVs (float2, 8 bytes) — also compressed on PC
		var texcoords = new Vector2[section.VertexCount];
		if ( uvResource != null )
		{
			for ( int i = 0; i < section.VertexCount; i++ )
			{
				reader.BaseStream.Seek( baseAddress + uvResource.Offset + i * 8, SeekOrigin.Begin );
				float u = reader.ReadSingle();
				float v = reader.ReadSingle();
				texcoords[i] = new Vector2(
					uvUMid + u * uvURange,
					uvVMid + v * uvVRange
				);
			}
		}

		// Read normals (float3 from 36-byte block)
		var normals = new Vector3[section.VertexCount];
		if ( normalsResource != null )
		{
			for ( int i = 0; i < section.VertexCount; i++ )
			{
				reader.BaseStream.Seek( baseAddress + normalsResource.Offset + i * 36, SeekOrigin.Begin );
				normals[i] = new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() ).Normal;
			}
		}

		// Read node map for bone index remapping
		byte[] nodeMap = Array.Empty<byte>();
		if ( nodeMapResource != null && nodeMapResource.Size > 0 )
		{
			reader.BaseStream.Seek( baseAddress + nodeMapResource.Offset, SeekOrigin.Begin );
			nodeMap = reader.ReadBytes( nodeMapResource.Size );
		}

		// Read blend indices/weights for skinned meshes
		byte[] blendIdx0 = null, blendIdx1 = null;
		byte[] blendWt0 = null, blendWt1 = null;

		if ( section.GeometryClassification == 1 ) // Rigid: all verts bound to one bone
		{
			byte rigidBone = 0;
			if ( section.NodesPerVertex >= 1 && nodeMap.Length > 0 )
				rigidBone = nodeMap[0];

			blendIdx0 = new byte[section.VertexCount];
			blendIdx1 = new byte[section.VertexCount];
			blendWt0 = new byte[section.VertexCount];
			blendWt1 = new byte[section.VertexCount];

			for ( int i = 0; i < section.VertexCount; i++ )
			{
				blendIdx0[i] = rigidBone;
				blendWt0[i] = 255;
			}
		}
		else if ( section.GeometryClassification >= 2 && vertexSize > 12 ) // RigidBoned or Skinned
		{
			blendIdx0 = new byte[section.VertexCount];
			blendIdx1 = new byte[section.VertexCount];
			blendWt0 = new byte[section.VertexCount];
			blendWt1 = new byte[section.VertexCount];

			for ( int i = 0; i < section.VertexCount; i++ )
			{
				reader.BaseStream.Seek( baseAddress + vertexResource.Offset + i * vertexSize + 12, SeekOrigin.Begin );

				if ( section.GeometryClassification == 2 ) // RigidBoned
				{
					blendIdx0[i] = reader.ReadByte();
					blendIdx1[i] = 0;
					blendWt0[i] = 255;
					blendWt1[i] = 0;
				}
				else if ( section.GeometryClassification == 3 ) // Skinned
				{
					// 4 byte indices + 4 byte weights
					byte n0 = reader.ReadByte();
					byte n1 = reader.ReadByte();
					reader.ReadByte(); // n2
					reader.ReadByte(); // n3
					byte w0 = reader.ReadByte();
					byte w1 = reader.ReadByte();

					blendIdx0[i] = n0;
					blendIdx1[i] = n1;
					blendWt0[i] = w0;
					blendWt1[i] = (byte)(255 - w0); // normalize to sum 255
				}
			}

			// Remap through node map
			if ( nodeMap.Length > 0 )
			{
				for ( int i = 0; i < section.VertexCount; i++ )
				{
					if ( blendIdx0[i] < nodeMap.Length )
						blendIdx0[i] = nodeMap[blendIdx0[i]];
					if ( blendIdx1[i] < nodeMap.Length )
						blendIdx1[i] = nodeMap[blendIdx1[i]];
				}
			}
		}

		// Build mesh for each submesh
		foreach ( var submesh in submeshes )
		{
			var triIndices = isTriList
				? ExtractTriangleList( indices, submesh.IndexStart, submesh.IndexLength )
				: ConvertTriStrip( indices, submesh.IndexStart, submesh.IndexLength );

			if ( triIndices.Count == 0 )
				continue;

			var material = LoadMaterial( map, model, submesh.ShaderIndex, mapName );

			var verts = new List<SkinnedVertex>( section.VertexCount );
			for ( int i = 0; i < section.VertexCount; i++ )
			{
				var pos = ConvertPosition( positions[i] );
				var norm = normals[i] != Vector3.Zero ? ConvertPosition( normals[i] ).Normal : Vector3.Up;
				var uv = texcoords[i];

				byte bi0 = blendIdx0 != null ? blendIdx0[i] : (byte)0;
				byte bi1 = blendIdx1 != null ? blendIdx1[i] : (byte)0;
				byte bw0 = blendWt0 != null ? blendWt0[i] : (byte)255;
				byte bw1 = blendWt1 != null ? blendWt1[i] : (byte)0;

				verts.Add( new SkinnedVertex(
					pos, norm, uv,
					new Color32( bi0, bi1, 0, 0 ),
					new Color32( bw0, bw1, 0, 0 )
				) );
			}

			var mesh = new Mesh( $"section{sectionIndex}", material );
			mesh.CreateVertexBuffer( verts.Count, verts );
			mesh.CreateIndexBuffer( triIndices.Count, triIndices );
			mesh.Bounds = BBox.FromPoints( verts.Select( v => v.position ) );
			builder.AddMesh( mesh, lod, regionName, permIndex );
		}
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

	static Material LoadMaterial( MapFile map, RenderModelTag model, int shaderIndex, string mapName )
	{
		if ( shaderIndex < 0 || shaderIndex >= model.Shaders.Count )
			return Material.Load( "materials/dev/primary_white.vmat" );

		var shaderRef = model.Shaders[shaderIndex];
		var shaderTag = map.GetTag( shaderRef.TagIndex );
		if ( shaderTag == null || string.IsNullOrEmpty( shaderTag.TagPath ) )
			return Material.Load( "materials/dev/primary_white.vmat" );

		var tagPath = shaderTag.TagPath.Replace( '\\', '/' );

		try
		{
			var mat = Material.Load( $"mount://halo2/{mapName}/{tagPath}.vmat" );
			return mat ?? Material.Load( "materials/dev/primary_white.vmat" );
		}
		catch
		{
			return Material.Load( "materials/dev/primary_white.vmat" );
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

		// Build mapping from phmo node index → render model bone index.
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
			int boneIdx;

			if ( directMapping )
			{
				boneIdx = rb.NodeIndex;
			}
			else if ( !phmoToRenderBone.TryGetValue( rb.NodeIndex, out boneIdx ) )
			{
				continue;
			}

			if ( boneIdx < 0 || boneIdx >= bones.Count )
				continue;

			var bone = bones[boneIdx];
			var bonePose = new Transform( bone.Position, bone.Rotation );

			var body = builder.AddBody( mass: rb.Mass > 0 ? rb.Mass : 10f, boneName: bone.Name );
			body.SetBindPose( bonePose );

			switch ( rb.ShapeType )
			{
				case 0: // Sphere
					if ( rb.ShapeIndex >= 0 && rb.ShapeIndex < phmo.Spheres.Count )
					{
						var sph = phmo.Spheres[rb.ShapeIndex];
						var center = new Vector3( sph.CenterX, sph.CenterY, sph.CenterZ ) * ScaleFactor;
						body.AddSphere( new Sphere( center, sph.Radius * ScaleFactor ) );
					}
					break;

				case 1: // Pill (Capsule)
					if ( rb.ShapeIndex >= 0 && rb.ShapeIndex < phmo.Pills.Count )
					{
						var pill = phmo.Pills[rb.ShapeIndex];
						var bottom = new Vector3( pill.BottomX, pill.BottomY, pill.BottomZ ) * ScaleFactor;
						var top = new Vector3( pill.TopX, pill.TopY, pill.TopZ ) * ScaleFactor;
						body.AddCapsule( new Capsule( bottom, top, pill.Radius * ScaleFactor ) );
					}
					break;

				case 2: // Box (as convex hull)
					if ( rb.ShapeIndex >= 0 && rb.ShapeIndex < phmo.Boxes.Count )
					{
						var box = phmo.Boxes[rb.ShapeIndex];
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
							AddRagdollJoint( phmo.RagdollConstraints[cref.Index], phmo, builder, bones, bodyIndexMap );
						break;

					case 1: // Limited Hinge
						if ( cref.Index >= 0 && cref.Index < phmo.LimitedHingeConstraints.Count )
							AddLimitedHingeJoint( phmo.LimitedHingeConstraints[cref.Index], phmo, builder, bones, bodyIndexMap );
						break;

					case 0: // Hinge (free rotation)
						if ( cref.Index >= 0 && cref.Index < phmo.HingeConstraints.Count )
							AddHingeJoint( phmo.HingeConstraints[cref.Index], phmo, builder, bones, bodyIndexMap );
						break;

					case 4: // Ball and Socket
						if ( cref.Index >= 0 && cref.Index < phmo.BallSocketConstraints.Count )
							AddBallJoint( phmo.BallSocketConstraints[cref.Index], phmo, builder, bones, bodyIndexMap );
						break;
				}
			}
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

	static void AddRagdollJoint( RagdollConstraint c, PhysicsModelTag phmo, ModelBuilder builder,
		List<ModelBuilder.Bone> bones, Dictionary<int, int> bodyIndexMap )
	{
		if ( !FindBodyIndices( c, phmo, bodyIndexMap, out int body1, out int body2 ) )
			return;

		var (frame1, frame2) = BuildJointFrames( c, bones );
		float swing = MathF.Max( c.ConeMax - c.ConeMin, c.PlaneMax - c.PlaneMin ) * 0.5f;
		float torque = c.MaxFrictionTorque * ScaleFactor * ScaleFactor;
		if ( swing == 0 )
		{
			if ( c.TwistMin == c.TwistMax )
				builder.AddFixedJoint( body1, body2, frame1, frame2 );
			else
			{
				var hinge = builder.AddHingeJoint( body1, body2, frame1, frame2 ).WithTargetVelocity( Vector3.Zero ).WithMaxTorque( torque );
				if ( c.TwistMax - c.TwistMin < MathF.PI * 2 )
					hinge.WithTwistLimit( c.TwistMin.RadianToDegree(), c.TwistMax.RadianToDegree() );
			}
			return;
		}
		var ball = builder.AddBallJoint( body1, body2, frame1, frame2 ).WithTargetVelocity( Vector3.Zero ).WithMaxTorque( torque );
		if ( swing < MathF.PI )
			ball.WithSwingLimit( Math.Clamp( swing.RadianToDegree(), 0, 89.1f ) );
		if ( c.TwistMax - c.TwistMin < MathF.PI * 2 )
			ball.WithTwistLimit( c.TwistMin.RadianToDegree(), c.TwistMax.RadianToDegree() );
	}

	static void AddLimitedHingeJoint( LimitedHingeConstraint c, PhysicsModelTag phmo, ModelBuilder builder,
		List<ModelBuilder.Bone> bones, Dictionary<int, int> bodyIndexMap )
	{
		if ( !FindBodyIndices( c, phmo, bodyIndexMap, out int body1, out int body2 ) )
			return;

		var (frame1, frame2) = BuildJointFrames( c, bones );

		float minDeg = c.LimitMin.RadianToDegree();
		float maxDeg = c.LimitMax.RadianToDegree();

		builder.AddHingeJoint( body1, body2, frame1, frame2 )
			.WithTwistLimit( minDeg, maxDeg )
			.WithTargetVelocity( Vector3.Zero ).WithMaxTorque( c.LimitFriction * ScaleFactor * ScaleFactor );
	}

	static void AddHingeJoint( HingeConstraint c, PhysicsModelTag phmo, ModelBuilder builder,
		List<ModelBuilder.Bone> bones, Dictionary<int, int> bodyIndexMap )
	{
		Log.Info( "AddHingeJoint" );

		if ( !FindBodyIndices( c, phmo, bodyIndexMap, out int body1, out int body2 ) )
			return;

		var (frame1, frame2) = BuildJointFrames( c, bones );
		builder.AddHingeJoint( body1, body2, frame1, frame2 );
	}

	static void AddBallJoint( BallSocketConstraint c, PhysicsModelTag phmo, ModelBuilder builder,
		List<ModelBuilder.Bone> bones, Dictionary<int, int> bodyIndexMap )
	{
		Log.Info( "AddBallJoint" );
		if ( !FindBodyIndices( c, phmo, bodyIndexMap, out int body1, out int body2 ) )
			return;

		var (frame1, frame2) = BuildJointFrames( c, bones );
		builder.AddBallJoint( body1, body2, frame1, frame2 );
	}

	struct Submesh
	{
		public short ShaderIndex;
		public ushort IndexStart;
		public ushort IndexLength;
	}
}

[StructLayout( LayoutKind.Sequential )]
public struct SkinnedVertex( Vector3 position, Vector3 normal, Vector2 texcoord, Color32 blendIndices, Color32 blendWeights )
{
	[VertexLayout.Position]
	public Vector3 position = position;

	[VertexLayout.Normal]
	public Vector3 normal = normal;

	[VertexLayout.TexCoord]
	public Vector2 texcoord = texcoord;

	[VertexLayout.BlendIndices]
	public Color32 blendIndices = blendIndices;

	[VertexLayout.BlendWeight]
	public Color32 blendWeights = blendWeights;
}
