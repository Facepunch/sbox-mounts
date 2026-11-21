using IdTech3.Formats;

namespace IdTech3;

sealed partial class MapLoader
{
	readonly Dictionary<string, Material> _materials = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<int, Model> _brushModels = [];
	readonly Dictionary<int, FogVolume> _fogs = [];

	sealed class MeshGroup
	{
		public Material Material;
		public List<Id3Vertex> Vertices = [];
		public List<int> Indices = [];
		public BBox Bounds;
		public bool HasBounds;
	}

	FogVolume GetFog( int index )
	{
		if ( index < 0 || index >= _bsp.Fogs.Length ) return null;
		if ( _fogs.TryGetValue( index, out var fog ) ) return fog;

		var f = _bsp.Fogs[index];
		var def = _game.Shaders.Find( f.Shader );
		fog = null;
		if ( def?.IsFog == true && f.Brush >= 0 && f.Brush < _bsp.Brushes.Length )
		{
			var points = BrushPoints( _bsp.Brushes[f.Brush] );
			if ( points.Count > 0 )
			{
				var bounds = BBox.FromPositionAndSize( points[0], 0 );
				foreach ( var p in points ) bounds = bounds.AddPoint( p );
				fog = new FogVolume { Color = Convert3.V( def.FogColor ), DepthForOpaque = def.FogDepthForOpaque, Bounds = bounds };
			}
		}
		_fogs[index] = fog;
		return fog;
	}

	List<Vector3> BrushPoints( BspBrush brush, Vector3 offset = default )
	{
		var sides = new List<BspBrushSide>( brush.NumSides );
		for ( int i = 0; i < brush.NumSides; i++ )
		{
			int si = brush.FirstSide + i;
			if ( si >= 0 && si < _bsp.BrushSides.Length ) sides.Add( _bsp.BrushSides[si] );
		}
		return Geometry.BrushPoints( sides ).Select( p => Convert3.V( p ) - offset ).ToList();
	}

	int ContentsOf( BspBrush brush ) => brush.Shader >= 0 && brush.Shader < _bsp.Shaders.Length ? _bsp.Shaders[brush.Shader].ContentFlags : 0;

	Material GetSurfaceMaterial( string shader, LightingMode mode, int page, int fogIndex )
	{
		var key = $"{shader}|{mode}|{page}|{fogIndex}";
		if ( _materials.TryGetValue( key, out var m ) ) return m;

		m = MaterialBuilder.Build( _game, new MaterialRequest
		{
			ShaderName = shader,
			Mode = mode,
			Lightmap = mode == LightingMode.Lightmap && page >= 0 && page < _lightmapPages.Count ? _lightmapPages[page] : null,
			Fog = GetFog( fogIndex ),
			SunShadows = SunShadows,
			Variant = $"/{ScenePath}/{page}/{fogIndex}",
		} );
		_materials[key] = m;
		return m;
	}

	/// <summary>
	/// Geometry for a BSP model (0 = world), relative to <paramref name="pivot"/>. <paramref name="draw"/> picks which
	/// surfaces are rendered (all when null); collision is built unless <paramref name="collision"/> is false.
	/// </summary>
	Model BuildBspModel( int index, Vector3 pivot, string name, Predicate<int> draw = null, bool collision = true )
	{
		if ( index < 0 || index >= _bsp.Models.Length ) return null;
		var bspModel = _bsp.Models[index];
		var groups = new Dictionary<string, MeshGroup>();
		var collisionVerts = new List<Vector3>();
		var collisionIndices = new List<int>();
		int shift = _game.Profile.LightShift;

		var patchVerts = new List<BspVertex>();
		var patchIndices = new List<int>();

		for ( int si = bspModel.FirstSurface; si < bspModel.FirstSurface + bspModel.NumSurfaces; si++ )
		{
			if ( si < 0 || si >= _bsp.Surfaces.Length ) continue;
			var s = _bsp.Surfaces[si];
			if ( s.Type is SurfaceType.Bad or SurfaceType.Flare ) continue;
			if ( s.Shader < 0 || s.Shader >= _bsp.Shaders.Length ) continue;

			var shaderRef = _bsp.Shaders[s.Shader];
			var def = _game.Shaders.Find( shaderRef.Name );
			bool solidPatch = collision && s.Type == SurfaceType.Patch && (shaderRef.ContentFlags & BspFile.ContentsSolid) != 0 && def?.NonSolid != true;
			bool visible = !MaterialBuilder.IsInvisible( def ) && !(IsQuakeStyle && (shaderRef.SurfaceFlags & BspFile.SurfNoDraw) != 0)
				&& (draw is null || draw( si ));
			if ( !visible && !solidPatch ) continue;

			// collect triangles
			IReadOnlyList<BspVertex> verts;
			List<int> tris;
			if ( s.Type == SurfaceType.Patch )
			{
				if ( s.FirstVert < 0 || s.FirstVert + s.NumVerts > _bsp.Vertices.Length ) continue;
				var control = new ArraySegment<BspVertex>( _bsp.Vertices, s.FirstVert, s.NumVerts );
				patchVerts.Clear(); patchIndices.Clear();
				Geometry.TessellatePatch( control, s.PatchWidth, s.PatchHeight, patchVerts, patchIndices );
				verts = patchVerts;
				tris = patchIndices;
			}
			else
			{
				if ( s.FirstVert < 0 || s.FirstVert + s.NumVerts > _bsp.Vertices.Length ) continue;
				if ( s.FirstIndex < 0 || s.FirstIndex + s.NumIndexes > _bsp.Indices.Length ) continue;
				verts = new ArraySegment<BspVertex>( _bsp.Vertices, s.FirstVert, s.NumVerts );
				tris = new List<int>( s.NumIndexes );
				for ( int i = 0; i + 2 < s.NumIndexes; i += 3 )
				{
					int a = _bsp.Indices[s.FirstIndex + i], b = _bsp.Indices[s.FirstIndex + i + 1], c = _bsp.Indices[s.FirstIndex + i + 2];
					if ( (uint)a >= s.NumVerts || (uint)b >= s.NumVerts || (uint)c >= s.NumVerts ) continue;
					tris.Add( a ); tris.Add( b ); tris.Add( c );
				}
				// stored clockwise; make them counter-clockwise relative to the vertex normals
				Geometry.OrientTriangles( verts, tris );
			}

			if ( solidPatch )
			{
				int baseIndex = collisionVerts.Count;
				foreach ( var v in verts ) collisionVerts.Add( Convert3.V( v.Position ) - pivot );
				foreach ( var i in tris ) collisionIndices.Add( baseIndex + i );
			}

			if ( !visible || tris.Count == 0 ) continue;

			// lighting mode as R_LoadSurfaces does it
			var mode = LightingMode.Vertex;
			int page = -1;
			LightmapSlot slot = default;
			// (triangle soups only have a lightmap when q3map2 lightmapped a misc_model)
			bool lightmapped = s.Lightmap >= 0 && s.Lightmap < _lightmapSlots.Count;
			if ( lightmapped )
			{
				slot = _lightmapSlots[s.Lightmap];
				page = slot.Page;
				mode = LightingMode.Lightmap;
			}

			var material = GetSurfaceMaterial( shaderRef.Name, mode, page, s.Fog );
			if ( material is null ) continue;

			var key = $"{shaderRef.Name}|{mode}|{page}|{s.Fog}";
			if ( !groups.TryGetValue( key, out var group ) )
				groups[key] = group = new MeshGroup { Material = material };

			float offset = def?.PolygonOffset == true ? 0.25f : 0f;
			int start = group.Vertices.Count;
			foreach ( var v in verts )
			{
				var p = Convert3.V( v.Position ) - pivot;
				var n = Convert3.V( v.Normal );
				if ( n.LengthSquared < 0.0001f ) n = Vector3.Up;
				n = n.Normal;
				if ( offset != 0 ) p += n * offset;

				byte r = v.Color.R, g = v.Color.G, b = v.Color.B;
				TextureUtil.ColorShift( ref r, ref g, ref b, shift );

				var lm = Convert3.V( v.LightmapCoord );
				if ( lightmapped ) lm = slot.Offset + lm * slot.Scale;

				group.Vertices.Add( new Id3Vertex
				{
					Position = p,
					Normal = n,
					Tangent = Convert3.TangentFor( n ),
					TexCoord = Convert3.V( v.TexCoord ),
					LightmapCoord = lm,
					Color = new Color32( r, g, b, v.Color.A ),
				} );

				if ( !group.HasBounds ) { group.Bounds = BBox.FromPositionAndSize( p, 0 ); group.HasBounds = true; }
				else group.Bounds = group.Bounds.AddPoint( p );
			}
			foreach ( var i in tris ) group.Indices.Add( start + i );
		}

		var builder = Model.Builder.WithName( name );
		int meshCount = 0;
		foreach ( var g in groups.Values )
		{
			if ( g.Vertices.Count == 0 || g.Indices.Count == 0 ) continue;
			var mesh = new Mesh( g.Material );
			mesh.CreateVertexBuffer( g.Vertices.Count, g.Vertices );
			mesh.CreateIndexBuffer( g.Indices.Count, g.Indices );
			mesh.Bounds = g.Bounds;
			builder.AddMesh( mesh );
			meshCount++;
		}

		// brushes: convex hulls for everything solid
		int hulls = 0;
		for ( int bi = bspModel.FirstBrush; collision && bi < bspModel.FirstBrush + bspModel.NumBrushes; bi++ )
		{
			if ( bi < 0 || bi >= _bsp.Brushes.Length ) continue;
			var brush = _bsp.Brushes[bi];
			if ( (ContentsOf( brush ) & BspFile.ContentsSolid) == 0 ) continue;
			var points = BrushPoints( brush, pivot );
			if ( points.Count < 4 ) continue;
			builder.AddCollisionHull( points );
			hulls++;
		}

		// Call of Duty collision patches/terrain
		if ( collision && _bsp.Kind == BspKind.CallOfDuty )
		{
			int first = index == 0 && bspModel.NumCollision == 0 ? 0 : bspModel.FirstCollision;
			int count = index == 0 && bspModel.NumCollision == 0 ? _bsp.CollisionMeshes.Count : bspModel.NumCollision;
			for ( int ci = first; ci < first + count && ci < _bsp.CollisionMeshes.Count; ci++ )
			{
				if ( ci < 0 ) continue;
				var cm = _bsp.CollisionMeshes[ci];
				if ( cm.Indices.Count < 3 ) continue;
				if ( cm.Shader >= 0 && cm.Shader < _bsp.Shaders.Length && (_bsp.Shaders[cm.Shader].ContentFlags & BspFile.ContentsSolid) == 0 ) continue;
				int baseIndex = collisionVerts.Count;
				foreach ( var v in cm.Vertices ) collisionVerts.Add( Convert3.V( v ) - pivot );
				// collision triangles share the clockwise convention
				for ( int i = 0; i + 2 < cm.Indices.Count; i += 3 )
				{
					collisionIndices.Add( baseIndex + cm.Indices[i] );
					collisionIndices.Add( baseIndex + cm.Indices[i + 2] );
					collisionIndices.Add( baseIndex + cm.Indices[i + 1] );
				}
			}
		}

		if ( collisionIndices.Count >= 3 )
		{
			builder.AddCollisionMesh( collisionVerts, collisionIndices );
			builder.AddTraceMesh( collisionVerts, collisionIndices );
		}

		if ( meshCount == 0 && hulls == 0 && collisionIndices.Count == 0 ) return null;
		return builder.Create();
	}

	/// <summary>
	/// How far ladder triggers reach past their brush, so a player stopped by the ladder's clip brush is still inside.
	/// </summary>
	const float LadderReach = 4;

	/// <summary>True when any side of the brush has a SURF_LADDER shader (textures/common/ladder in RTCW and CoD).</summary>
	bool IsLadder( BspBrush brush )
	{
		for ( int i = 0; i < brush.NumSides; i++ )
		{
			int si = brush.FirstSide + i;
			if ( si < 0 || si >= _bsp.BrushSides.Length ) continue;
			int shader = _bsp.BrushSides[si].Shader;
			if ( shader >= 0 && shader < _bsp.Shaders.Length && (_bsp.Shaders[shader].SurfaceFlags & BspFile.SurfLadder) != 0 )
				return true;
		}
		return false;
	}

	/// <summary>
	/// A ladder brush as a trigger box tagged "ladder", which MoveModeLadder climbs. Its thin horizontal axis is the
	/// direction the player faces while climbing.
	/// </summary>
	void BuildLadder( GameObject parent, List<Vector3> points, int index )
	{
		var bounds = BBox.FromPoints( points ).Grow( LadderReach );
		var go = new GameObject( parent, true, $"ladder {index}" );
		go.Tags.Add( "ladder" );
		go.LocalPosition = bounds.Center;
		var box = go.AddComponent<BoxCollider>();
		box.Scale = bounds.Size;
		box.IsTrigger = true;
		box.Static = true;
	}

	/// <summary>Player clip brushes, ladders and water/slime/lava volumes of a BSP model.</summary>
	void BuildClipAndLiquids( int index, GameObject parent, Vector3 pivot )
	{
		if ( index < 0 || index >= _bsp.Models.Length ) return;
		var bspModel = _bsp.Models[index];

		var clip = new List<List<Vector3>>();
		var liquids = new Dictionary<LiquidType, List<List<Vector3>>>();

		for ( int bi = bspModel.FirstBrush; bi < bspModel.FirstBrush + bspModel.NumBrushes; bi++ )
		{
			if ( bi < 0 || bi >= _bsp.Brushes.Length ) continue;
			var brush = _bsp.Brushes[bi];

			// ladders keep their clip or solid collision too, as in the original
			if ( IsLadder( brush ) )
			{
				var ladder = BrushPoints( brush, pivot );
				if ( ladder.Count >= 4 ) BuildLadder( parent, ladder, bi );
			}

			int contents = ContentsOf( brush );
			if ( (contents & BspFile.ContentsSolid) != 0 ) continue;

			LiquidType? liquid = (contents & BspFile.ContentsLava) != 0 ? LiquidType.Lava
				: (contents & BspFile.ContentsSlime) != 0 ? LiquidType.Slime
				: (contents & BspFile.ContentsWater) != 0 ? LiquidType.Water
				: null;
			bool isClip = (contents & BspFile.ContentsPlayerClip) != 0;
			if ( liquid is null && !isClip ) continue;

			var points = BrushPoints( brush, pivot );
			if ( points.Count < 4 ) continue;

			if ( liquid is LiquidType type )
			{
				if ( !liquids.TryGetValue( type, out var list ) ) liquids[type] = list = [];
				list.Add( points );
			}
			else clip.Add( points );
		}

		if ( clip.Count > 0 )
		{
			var builder = Model.Builder.WithName( $"{WorldModelName}*{index}_clip" );
			foreach ( var h in clip ) builder.AddCollisionHull( h );
			var go = new GameObject( parent, true, "playerclip" );
			go.Tags.Add( "playerclip" );
			var collider = go.AddComponent<ModelCollider>();
			collider.Model = builder.Create();
			collider.Static = true;
		}

		foreach ( var (type, hulls) in liquids )
		{
			var builder = Model.Builder.WithName( $"{WorldModelName}*{index}_{type}" );
			foreach ( var h in hulls ) builder.AddCollisionHull( h );
			var go = new GameObject( parent, true, type.ToString().ToLowerInvariant() );
			go.Tags.Add( "water" );
			var collider = go.AddComponent<ModelCollider>();
			collider.Model = builder.Create();
			collider.IsTrigger = true;
			collider.Static = true;
			go.AddComponent<Liquid>().Type = type;
		}
	}

	Model GetBrushModel( int index, Vector3 pivot )
	{
		if ( _brushModels.TryGetValue( index, out var model ) ) return model;
		model = BuildBspModel( index, pivot, $"{WorldModelName}*{index}" );
		_brushModels[index] = model;
		return model;
	}
}
