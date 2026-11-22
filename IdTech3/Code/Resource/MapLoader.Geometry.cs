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
		/// <summary>Opaque surfaces, which go into the shadow blockers.</summary>
		public bool Shadow;
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
				fog = new FogVolume { Color =  def.FogColor , DepthForOpaque = def.FogDepthForOpaque, Bounds = bounds };
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
		return Geometry.BrushPoints( sides ).Select( p =>  p  - offset ).ToList();
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
	/// When <paramref name="shadows"/> is given, it receives the model's shadow blockers. When
	/// <paramref name="skyHulls"/> is given, sky brushes go there instead of into the model's collision.
	/// </summary>
	Model BuildBspModel( int index, Vector3 pivot, string name, Predicate<int> draw = null, bool collision = true, List<Model> shadows = null,
		List<List<Vector3>> skyHulls = null )
	{
		if ( index < 0 || index >= _bsp.Models.Length ) return null;
		var bspModel = _bsp.Models[index];
		var groups = new Dictionary<string, MeshGroup>();
		// every collision triangle, for the trace mesh
		var collisionVerts = new List<Vector3>();
		var collisionIndices = new List<int>();
		// the same triangles by physics surface
		var surfaceMeshes = new Dictionary<string, (Surface Surface, List<Vector3> Vertices, List<uint> Indices)>();
		int shift = _game.Profile.LightShift;

		void AddCollisionTriangles( Surface surface, IEnumerable<Vector3> vertices, IReadOnlyList<int> triangles )
		{
			int baseIndex = collisionVerts.Count;
			var key = surface?.ResourceName ?? "";
			if ( !surfaceMeshes.TryGetValue( key, out var mesh ) )
				surfaceMeshes[key] = mesh = (surface, [], []);
			uint meshBase = (uint)mesh.Vertices.Count;
			foreach ( var v in vertices )
			{
				collisionVerts.Add( v );
				mesh.Vertices.Add( v );
			}
			foreach ( var i in triangles )
			{
				collisionIndices.Add( baseIndex + i );
				mesh.Indices.Add( meshBase + (uint)i );
			}
		}

		var patchVerts = new List<BspVertex>();
		var patchIndices = new List<int>();
		var patchLevels = StitchedPatchLevels( bspModel );

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
				Geometry.TessellatePatch( control, s.PatchWidth, s.PatchHeight, patchVerts, patchIndices,
					levels: patchLevels.TryGetValue( si, out var levels ) ? (levels.U, levels.V) : null );
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
				AddCollisionTriangles( SurfaceFor( s.Shader ), verts.Select( v =>  v.Position  - pivot ), tris );

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
				groups[key] = group = new MeshGroup { Material = material, Shadow = MaterialBuilder.CastsShadow( def ) };

			float offset = def?.PolygonOffset == true ? 0.25f : 0f;
			int start = group.Vertices.Count;
			foreach ( var v in verts )
			{
				var p =  v.Position  - pivot;
				var n =  v.Normal ;
				if ( n.LengthSquared < 0.0001f ) n = Vector3.Up;
				n = n.Normal;
				if ( offset != 0 ) p += n * offset;

				byte r = v.Color.r, g = v.Color.g, b = v.Color.b;
				TextureUtil.ColorShift( ref r, ref g, ref b, shift );

				var lm =  v.LightmapCoord ;
				if ( lightmapped ) lm = slot.Offset + lm * slot.Scale;

				group.Vertices.Add( new Id3Vertex
				{
					Position = p,
					Normal = n,
					Tangent = Convert3.TangentFor( n ),
					TexCoord =  v.TexCoord ,
					LightmapCoord = lm,
					Color = new Color32( r, g, b, v.Color.a ),
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

		if ( shadows is not null )
			shadows.AddRange( BuildShadowBlockers( groups.Values, name ) );

		// one static physics body per surface, so each shape carries its material
		var bodies = new Dictionary<string, PhysicsBodyBuilder>();
		PhysicsBodyBuilder BodyFor( Surface surface )
		{
			var key = surface?.ResourceName ?? "";
			if ( !bodies.TryGetValue( key, out var body ) )
				bodies[key] = body = builder.AddBody( 0, surface );
			return body;
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
			if ( skyHulls is not null && IsSkyBrush( brush ) )
			{
				skyHulls.Add( points );
				continue;
			}
			BodyFor( BrushSurface( brush ) ).AddHull( points.ToArray() );
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
				// collision triangles share the clockwise convention
				var tris = new List<int>( cm.Indices.Count );
				for ( int i = 0; i + 2 < cm.Indices.Count; i += 3 )
				{
					tris.Add( cm.Indices[i] );
					tris.Add( cm.Indices[i + 2] );
					tris.Add( cm.Indices[i + 1] );
				}
				AddCollisionTriangles( SurfaceFor( cm.Shader ), cm.Vertices.Select( v =>  v  - pivot ), tris );
			}
		}

		foreach ( var mesh in surfaceMeshes.Values )
		{
			if ( mesh.Vertices.Count < 3 || mesh.Indices.Count < 3 ) continue;
			BodyFor( mesh.Surface ).AddMesh( mesh.Vertices.ToArray(), mesh.Indices.ToArray(), default );
		}

		if ( collisionIndices.Count >= 3 )
			builder.AddTraceMesh( collisionVerts, collisionIndices );

		if ( meshCount == 0 && hulls == 0 && collisionIndices.Count == 0 ) return null;
		return builder.Create();
	}

	/// <summary>
	/// Subdivision levels for every patch of a BSP model, with patches that share an edge agreeing on it, so their
	/// tessellations meet without cracks. Covers all the model's patches, drawn or not, so every caller agrees.
	/// </summary>
	Dictionary<int, Geometry.PatchInfo> StitchedPatchLevels( BspModel bspModel )
	{
		var patches = new Dictionary<int, Geometry.PatchInfo>();
		for ( int si = bspModel.FirstSurface; si < bspModel.FirstSurface + bspModel.NumSurfaces; si++ )
		{
			if ( si < 0 || si >= _bsp.Surfaces.Length ) continue;
			var s = _bsp.Surfaces[si];
			if ( s.Type != SurfaceType.Patch || s.FirstVert < 0 || s.FirstVert + s.NumVerts > _bsp.Vertices.Length ) continue;
			var control = new ArraySegment<BspVertex>( _bsp.Vertices, s.FirstVert, s.NumVerts );
			if ( Geometry.PatchLevels( control, s.PatchWidth, s.PatchHeight ) is not { } levels ) continue;
			patches[si] = new Geometry.PatchInfo { Control = control, Width = s.PatchWidth, Height = s.PatchHeight, U = levels.U, V = levels.V };
		}
		Geometry.StitchPatchLevels( patches.Values.ToList() );
		return patches;
	}

	/// <summary>True when a brush has a sky side (only the side facing the map usually does).</summary>
	bool IsSkyBrush( BspBrush brush )
	{
		if ( IsSkyShader( brush.Shader ) ) return true;
		for ( int i = 0; i < brush.NumSides; i++ )
		{
			int si = brush.FirstSide + i;
			if ( si >= 0 && si < _bsp.BrushSides.Length && IsSkyShader( _bsp.BrushSides[si].Shader ) ) return true;
		}
		return false;
	}

	bool IsSkyShader( int shader )
	{
		if ( shader < 0 || shader >= _bsp.Shaders.Length ) return false;
		var s = _bsp.Shaders[shader];
		return (s.SurfaceFlags & BspFile.SurfSky) != 0 || _game.Shaders.Find( s.Name )?.IsSky == true;
	}

	/// <summary>
	/// The world's sky brushes as their own static collider tagged "world" and "passaudio", as the Source mount does
	/// with its audio-transparent collision. They still stop players and count as world, but the audio simulation
	/// ignores "passaudio", so its rays escape there and big outdoor areas sound open instead of like a room.
	/// </summary>
	void BuildSkyCollider( GameObject world, List<List<Vector3>> hulls )
	{
		if ( hulls.Count == 0 ) return;
		var builder = Model.Builder.WithName( $"{WorldModelName}_sky" );
		foreach ( var h in hulls ) builder.AddCollisionHull( h );
		var go = new GameObject( world, true, "sky" ) { IsStatic = true };
		go.Tags.Add( "world" );
		go.Tags.Add( "passaudio" );
		var collider = go.AddComponent<ModelCollider>();
		collider.Model = builder.Create();
		collider.Static = true;
	}

	/// <summary>The physics surface for a shader's material (footsteps, impacts).</summary>
	Surface SurfaceFor( int shader )
	{
		int flags = shader >= 0 && shader < _bsp.Shaders.Length ? _bsp.Shaders[shader].SurfaceFlags : 0;
		return SurfaceMap.For( _game.Profile.SurfaceFlags, flags );
	}

	/// <summary>
	/// The physics surface of a brush. Sides can differ, so the top side's material wins (it's what's walked on),
	/// then any side with a material, then the brush's own shader. Caulk and other tool shaders have no material.
	/// </summary>
	Surface BrushSurface( BspBrush brush )
	{
		var style = _game.Profile.SurfaceFlags;
		string top = null, any = null;
		float topZ = 0.7f;
		for ( int i = 0; i < brush.NumSides; i++ )
		{
			int si = brush.FirstSide + i;
			if ( si < 0 || si >= _bsp.BrushSides.Length ) continue;
			var side = _bsp.BrushSides[si];
			if ( side.Shader < 0 || side.Shader >= _bsp.Shaders.Length ) continue;
			var name = SurfaceMap.NameFor( style, _bsp.Shaders[side.Shader].SurfaceFlags );
			if ( name is null ) continue;
			any ??= name;
			if ( side.Normal.z > topZ ) { topZ = side.Normal.z; top = name; }
		}
		var chosen = top ?? any;
		return chosen is not null ? SurfaceMap.ForName( chosen ) : SurfaceFor( brush.Shader );
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

	Model GetBrushModel( int index, Vector3 pivot, out List<Model> shadows )
	{
		if ( _brushModels.TryGetValue( index, out var model ) )
		{
			shadows = _brushShadows[index];
			return model;
		}
		shadows = [];
		model = BuildBspModel( index, pivot, $"{WorldModelName}*{index}", shadows: shadows );
		_brushModels[index] = model;
		_brushShadows[index] = shadows;
		return model;
	}

	// ------------------------------------------------------------------ shadow blockers

	/// <summary>Side of the cubes opaque triangles are grouped into, so shadow passes cull blockers per chunk.</summary>
	const float ShadowChunkSize = 1024;

	readonly Dictionary<int, List<Model>> _brushShadows = [];
	Material _shadowMaterial;

	/// <summary>
	/// Shadow blockers: the opaque triangles turned inside out, drawn only into
	/// shadow maps. Lit faces cast from their back faces, so they don't shadow themselves; the visible world draws no
	/// shadows of its own.
	/// </summary>
	List<Model> BuildShadowBlockers( IEnumerable<MeshGroup> groups, string name )
	{
		var chunks = new Dictionary<Vector3, (List<Id3Vertex> Vertices, List<int> Indices)>();
		foreach ( var g in groups )
		{
			if ( !g.Shadow ) continue;
			for ( int i = 0; i + 2 < g.Indices.Count; i += 3 )
			{
				var a = g.Vertices[g.Indices[i]];
				var b = g.Vertices[g.Indices[i + 1]];
				var c = g.Vertices[g.Indices[i + 2]];
				var center = (a.Position + b.Position + c.Position) / (3 * ShadowChunkSize);
				var cell = new Vector3( MathF.Floor( center.x ), MathF.Floor( center.y ), MathF.Floor( center.z ) );
				if ( !chunks.TryGetValue( cell, out var chunk ) )
					chunks[cell] = chunk = ([], []);

				// reversed winding, flipped normals
				int start = chunk.Vertices.Count;
				chunk.Vertices.Add( Flip( a ) );
				chunk.Vertices.Add( Flip( c ) );
				chunk.Vertices.Add( Flip( b ) );
				chunk.Indices.Add( start );
				chunk.Indices.Add( start + 1 );
				chunk.Indices.Add( start + 2 );
			}
		}

		var models = new List<Model>( chunks.Count );
		if ( chunks.Count == 0 ) return models;

		_shadowMaterial ??= CreateShadowMaterial();
		foreach ( var (vertices, indices) in chunks.Values )
		{
			var bounds = BBox.FromPoints( vertices.Select( v => v.Position ) );
			var mesh = new Mesh( _shadowMaterial );
			mesh.CreateVertexBuffer( vertices.Count, vertices );
			mesh.CreateIndexBuffer( indices.Count, indices );
			mesh.Bounds = bounds;
			models.Add( Model.Builder.WithName( $"{name}_shadow{models.Count}" ).AddMesh( mesh ).Create() );
		}
		return models;

		static Id3Vertex Flip( Id3Vertex v )
		{
			v.Normal = -v.Normal;
			v.Tangent = Convert3.TangentFor( v.Normal );
			return v;
		}
	}

	Material CreateShadowMaterial()
	{
		var material = Material.Create( "_shadowblocker", MaterialBuilder.ShaderPath );
		material.SetFeature( "F_RENDER_BACKFACES", 0 );
		return material;
	}

	/// <summary>Shadow-only renderers for a model's blockers, under <paramref name="parent"/>.</summary>
	static void AddShadowBlockers( GameObject parent, List<Model> models )
	{
		foreach ( var model in models )
		{
			var go = new GameObject( parent, false, "shadow blocker" ) { IsStatic = parent.IsStatic };
			var renderer = go.AddComponent<ModelRenderer>();
			renderer.Model = model;
			renderer.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
			go.Enabled = true;
		}
	}
}
