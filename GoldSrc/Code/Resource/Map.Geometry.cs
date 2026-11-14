using System;
using System.Runtime.InteropServices;
using GoldSrc.Bsp;

namespace GoldSrc;

partial class MapLoader
{
	private int[] _faceLeaves;

	private sealed class MapGeometry
	{
		public Model Surfaces;
		public PhysicsGroupDescription Physics;
		public readonly List<MaterialBatch> StaticMeshes = [];
		public bool IsEmpty => Surfaces is null && Physics is null && StaticMeshes.Count == 0;
	}

	private readonly record struct MapAggregate( Model Model, List<AggregateFragmentInfo> Fragments );

	private sealed class MaterialBatch( Material material, bool aggregate )
	{
		public readonly Material Material = material;
		public readonly bool Aggregate = aggregate;
		public readonly Dictionary<int, MeshBatch> Fragments = [];
	}

	private sealed class MeshBatch
	{
		public readonly List<MapVertex> Vertices = [];
		public readonly List<int> Indices = [];
		public BBox Bounds = new() { Mins = float.MaxValue, Maxs = float.MinValue };
	}

	private MapGeometry CreateGeometry( BspFile bsp, BspModel bspModel, string modelName, Vector3 origin = default, BlendMode blend = BlendMode.Opaque, char surfaceOverride = '\0', bool aggregate = false )
	{
		var materials = new Dictionary<(int Tex, int Page), MaterialBatch>();
		var collisionVertices = new List<Vector3>();
		var collisionIndices = new List<int>();
		var collisionMaterials = new List<byte>();
		var collisionSurfaces = new List<Surface>();
		var surfaceIndices = new Dictionary<char, byte>();
		var faceVertices = new List<Vector3>();
		if ( aggregate )
			_faceLeaves ??= BuildFaceLeaves( bsp );

		for ( var i = 0; i < bspModel.FaceCount; ++i )
		{
			var faceIndex = bspModel.FirstFace + i;
			if ( faceIndex < 0 || faceIndex >= bsp.Faces.Length )
				continue;

			var face = bsp.Faces[faceIndex];
			var texInfo = bsp.TexInfos[face.TextureInfo];
			var texIndex = (int)texInfo.MipTex;
			var texture = bsp.Textures[texIndex];

			if ( texture.IsTool )
				continue;

			var surface = _surfaces[faceIndex];
			var plane = bsp.Planes[face.Plane];
			var normal = (plane.Normal * (face.PlaneSide == 0 ? 1f : -1f)).Normal;
			var tangent = texInfo.S.Normal;

			faceVertices.Clear();
			for ( var edge = 0; edge < face.EdgeCount; ++edge )
				faceVertices.Add( bsp.VertexFromSurfEdge( face.FirstEdge + edge ) - origin );
			var triangles = Mesh.TriangulatePolygon( CollectionsMarshal.AsSpan( faceVertices ) );
			if ( triangles.IsEmpty )
			{
				Log.Warning( $"{FilePath}: face {faceIndex} could not be triangulated." );
				continue;
			}

			// Sky is drawn by the skybox, but we still want to collide with it.
			var isSky = texture.IsSky;
			var isLiquid = texture.IsLiquid;
			var surfaceType = surfaceOverride != '\0' ? surfaceOverride : Host.GetTextureSurfaceType( texture.Name, isLiquid );

			if ( !isLiquid )
			{
				if ( !surfaceIndices.TryGetValue( surfaceType, out var surfaceIndex ) )
				{
					surfaceIndex = checked((byte)collisionSurfaces.Count);
					surfaceIndices.Add( surfaceType, surfaceIndex );
					collisionSurfaces.Add( Host.GetSurface( surfaceType ) );
				}

				var collisionOffset = collisionVertices.Count;
				collisionVertices.AddRange( faceVertices );

				AppendIndices( collisionIndices, triangles, collisionOffset );
				for ( var t = 0; t < triangles.Length; t += 3 )
					collisionMaterials.Add( surfaceIndex );
			}

			if ( isSky )
				continue;

			var page = isLiquid ? LiquidPage
				: face.LightmapOffset < 0 || texInfo.Special || texture.IsEmissive ? UnlitPage
				: surface.Page;

			var key = (texIndex, page);
			if ( !materials.TryGetValue( key, out var materialBatch ) )
			{
				var material = GetMaterial( bsp, texIndex, page, blend, surfaceType );
				materialBatch = new MaterialBatch( material, aggregate && blend == BlendMode.Opaque && !isLiquid && !material.Flags.IsTranslucent );
				materials.Add( key, materialBatch );
			}

			var fragment = materialBatch.Aggregate ? _faceLeaves[faceIndex] : 0;
			if ( !materialBatch.Fragments.TryGetValue( fragment, out var group ) )
			{
				group = new MeshBatch();
				materialBatch.Fragments.Add( fragment, group );
			}
			var vertices = group.Vertices;
			var indices = group.Indices;
			var indexOffset = vertices.Count;

			for ( var edge = 0; edge < face.EdgeCount; ++edge )
			{
				var position = faceVertices[edge];
				var world = position + origin;

				var u = Vector3.Dot( world, texInfo.S ) + texInfo.OffsetS;
				var v = Vector3.Dot( world, texInfo.T ) + texInfo.OffsetT;

				var uv = new Vector2( u / texture.Width, v / texture.Height );
				var uv2 = LightmapCoord( surface, u, v );

				vertices.Add( new MapVertex
				{
					Position = position,
					Normal = normal,
					Tangent = tangent,
					Texcoord = uv,
					Texcoord2 = uv2,
					LightStyles = new Vector4(
						face.Style0 == 255 ? 0 : face.Style0,
						face.Style1 == 255 ? 0 : face.Style1,
						face.Style2 == 255 ? 0 : face.Style2,
						face.Style3 == 255 ? 0 : face.Style3 ),
				} );

				group.Bounds = group.Bounds.AddPoint( position );
			}

			AppendIndices( indices, triangles, indexOffset );
		}

		var geometry = new MapGeometry
		{
			Physics = CreateMeshPhysics( modelName, collisionVertices, collisionIndices, collisionMaterials, collisionSurfaces, surfaceOverride )
		};
		BuildRenderModels( materials, modelName, geometry, collisionVertices, collisionIndices );
		return geometry;
	}

	private static int[] BuildFaceLeaves( BspFile bsp )
	{
		var leaves = new int[bsp.Faces.Length];
		Array.Fill( leaves, -1 );
		for ( int leafIndex = 0; leafIndex < bsp.Leaves.Length; leafIndex++ )
		{
			var leaf = bsp.Leaves[leafIndex];
			if ( leaf.Solid ) continue;
			for ( int i = 0; i < leaf.MarkSurfaceCount; i++ )
			{
				int face = bsp.MarkSurfaces[leaf.FirstMarkSurface + i];
				if ( leaves[face] == -1 )
					leaves[face] = leafIndex;
			}
		}
		return leaves;
	}

	private static void AppendIndices( List<int> destination, ReadOnlySpan<int> indices, int offset )
	{
		for ( int i = indices.Length - 1; i >= 0; i-- )
			destination.Add( offset + indices[i] );
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct MapVertex
	{
		[VertexLayout.Position]
		public Vector3 Position;

		[VertexLayout.Normal]
		public Vector3 Normal;

		[VertexLayout.Tangent]
		public Vector3 Tangent;

		[VertexLayout.TexCoord( 0 )]
		public Vector2 Texcoord;

		[VertexLayout.TexCoord( 1 )]
		public Vector2 Texcoord2;

		[VertexLayout.BlendIndices( 10 )]
		public Vector4 LightStyles;
	}
}
