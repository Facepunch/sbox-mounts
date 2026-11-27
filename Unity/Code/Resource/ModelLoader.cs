using System.Runtime.InteropServices;

namespace UnityMount;

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct UnityStaticVertex( Vector3 position, Vector3 normal, Vector4 tangent, Vector2 texcoord, Vector2 texcoord2, Color32 color )
{
	[VertexLayout.Position]
	public Vector3 position = position;

	[VertexLayout.Normal]
	public Vector3 normal = normal;

	[VertexLayout.Tangent]
	public Vector4 tangent = tangent;

	[VertexLayout.TexCoord]
	public Vector2 texcoord = texcoord;

	[VertexLayout.TexCoord( 1 )]
	public Vector2 texcoord2 = texcoord2;

	[VertexLayout.Color]
	public Color32 color = color;
}

public sealed class UnityModelLoader( SerializedFile file, UnityObjectInfo obj ) : ResourceLoader<UnityGameMount>
{
	protected override object Load()
	{
		var meshData = UnityMeshData.Read( Host.Workspace, file, obj );
		var defaultMat = UnityMaterialLoader.CreateDefaultMaterial();

		// Resolve real in-game Material[] mapped from MeshRenderer / SkinnedMeshRenderer references
		List<Material> materials = null;
		var key = new AssetKey( file.Key, obj.PathId );
		if ( Host.TryGetMeshMaterialKeys( key, out var matKeys ) && matKeys.Length > 0 )
		{
			materials = new List<Material>( matKeys.Length );
			foreach ( var mk in matKeys )
			{
				var mp = Host.GetMaterialMountPath( mk );
				var m = mp is not null ? Material.Load( mp ) : null;
				materials.Add( m ?? defaultMat );
			}
		}

		return BuildModel( Path, meshData, materials, defaultMat, addCollision: true );
	}

	/// <summary>
	/// Builds a s&amp;box Model from a UnityMeshData with per-submesh materials and optional trace/collision mesh.
	/// </summary>
	public static Model BuildModel(
		string name,
		UnityMeshData data,
		IReadOnlyList<Material> materials,
		Material fallbackMaterial,
		bool addCollision,
		bool flipWinding = true,
		int firstSubMesh = 0,
		int subMeshCount = -1 )
	{
		var builder = Model.Builder.WithName( name );
		if ( data is null || data.VertexCount == 0 || data.SubMeshes.Length == 0 )
			return builder.Create();

		int startSub = Math.Clamp( firstSubMesh, 0, data.SubMeshes.Length );
		int endSub = subMeshCount < 0
			? data.SubMeshes.Length
			: Math.Clamp( startSub + subMeshCount, startSub, data.SubMeshes.Length );

		var tracePositions = new List<Vector3>();
		var traceIndices = new List<int>();

		for ( int si = startSub; si < endSub; si++ )
		{
			var indices = data.ReadSubMeshIndices( si, flipWinding );
			if ( indices.Length < 3 )
				continue;

			int matSlot = si - startSub;
			Material mat = fallbackMaterial;
			if ( materials is not null && materials.Count > 0 )
			{
				mat = matSlot < materials.Count
					? (materials[matSlot] ?? fallbackMaterial)
					: (materials[^1] ?? fallbackMaterial);
			}

			var remap = new Dictionary<int, int>( Math.Min( indices.Length, (int)data.VertexCount ) );
			var localIndices = new int[indices.Length];
			var usedVerts = new List<int>();

			for ( int k = 0; k < indices.Length; k++ )
			{
				int gv = indices[k];
				if ( (uint)gv >= data.VertexCount ) continue;

				if ( !remap.TryGetValue( gv, out int lv ) )
				{
					lv = usedVerts.Count;
					remap[gv] = lv;
					usedVerts.Add( gv );
				}
				localIndices[k] = lv;
			}

			if ( usedVerts.Count == 0 )
				continue;

			var min = new Vector3( float.MaxValue );
			var max = new Vector3( float.MinValue );
			int traceBase = tracePositions.Count;

			var verts = new UnityStaticVertex[usedVerts.Count];
			for ( int i = 0; i < usedVerts.Count; i++ )
			{
				int gv = usedVerts[i];
				var p = data.Positions[gv];
				min = Vector3.Min( min, p );
				max = Vector3.Max( max, p );
				tracePositions.Add( p );

				verts[i] = new UnityStaticVertex(
					p,
					data.Normals[gv],
					data.Tangents[gv],
					data.Uv0[gv],
					data.Uv1[gv],
					data.Colors[gv]
				);
			}

			var mesh = new Mesh( mat ) { Bounds = new BBox( min, max ) };
			mesh.CreateVertexBuffer( verts.Length, verts );
			mesh.CreateIndexBuffer( localIndices.Length, localIndices );
			builder.AddMesh( mesh );

			for ( int k = 0; k < localIndices.Length; k++ )
				traceIndices.Add( traceBase + localIndices[k] );
		}

		if ( tracePositions.Count > 0 && traceIndices.Count >= 3 )
		{
			builder.AddTraceMesh( tracePositions, traceIndices );
			if ( addCollision )
				builder.AddCollisionMesh( tracePositions, traceIndices );
		}

		return builder.Create();
	}
}
