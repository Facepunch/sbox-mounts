namespace Diesel;

using Matrix = System.Numerics.Matrix4x4;

/// <summary>Mount-owned immutable rig/clip data; each viewmodel owns its deforming vertex buffers.</summary>
sealed class PaydayViewAssets
{
	public const string HandsPath = "units/characters/fps/suit/fps_hand";
	public ModelFile Hands { get; }
	public ModelFile Gun { get; }
	public Dictionary<string, AnimationFile> Clips { get; } = [];
	public Dictionary<string, Dictionary<ulong, AnimationFile.Track>> GunClips { get; } = [];
	public record Part( ModelFile File, ModelFile.Geometry Geometry, Material[] Materials );
	public List<Part> Parts { get; } = [];
	public Dictionary<ulong, Model> RigidModels { get; } = [];
	public Matrix GunRoot { get; }
	public PaydayViewAssets( BundleDatabase database, MapMaterials materials, WeaponDefinition definition )
	{
		Hands = new( database.Find( "model", HandsPath ) );
		Gun = new( database.Find( "model", definition.ViewUnit ) );
		foreach ( var clip in definition.Clips )
			Clips.Add( clip, new( database.Find( "animation", definition.ClipPath( clip ) ) ) );
		foreach ( var clip in new[] { "sprint_enter", "sprint", "sprint_exit", "recoil_zoom" } )
			if ( database.Find( "animation", definition.ClipPath( clip ) ) is { } bytes )
				Clips.Add( clip, new( bytes ) );
		// Explicit Dallas selection belongs to the playable viewmodel, never global unit visibility.
		Add( Hands, HandsPath, [IdString.Hash( "g_fps_hand_dallas" ), IdString.Hash( "g_button_dallas" )] );
		var obj = UnitModel.Xml( database.Find( "object", definition.ViewUnit ) );
		foreach ( var group in obj.Element( "animations" ).Elements( "animation_group" ) )
			GunClips.Add( group.Attribute( "name" ), Gun.Controllers( group.Elements( "object" )
				.Where( n => n.Attribute( "enabled" ) == "true" ).Select( n => IdString.Hash( n.Attribute( "name" ) ) ).ToHashSet() ) );
		if ( definition.Shotgun )
		{
			GunClips["reload_exit"] = GunClips["reload"];
			GunClips.Remove( "reload" );
		}
		Add( Gun, definition.ViewUnit, UnitGeometry.Read( XmlNode.Parse( "<unit/>" ), obj ).Graphics );
		foreach ( var part in Parts.Where( p => p.File == Gun ) )
			RigidModels.Add( part.Geometry.Name, PaydayViewMesh.BuildRigid( part ) );
		Matrix.Invert( Gun.NodePose( Gun.Pose( null, 0 ), "rp_weapon" ), out var inverse );
		GunRoot = inverse;

		void Add( ModelFile file, string config, HashSet<ulong> selected )
		{
			foreach ( var geometry in file.ReadGeometry( selected, 0, raw: true ) )
				Parts.Add( new( file, geometry, geometry.Draws.Select( d => materials.ResolveVariant( config, d.Material, config ) ).ToArray() ) );
		}
	}
}

sealed class PaydayViewMesh
{
	readonly PaydayViewAssets assets;
	readonly List<(PaydayViewAssets.Part Part, Vector3[] Positions, Vector3[] Normals, List<(ModelFile.Draw Draw, Mesh Mesh, Vertex[] Vertices)> Draws)> parts = [];
	public Model Model { get; }
	public Transform Muzzle { get; private set; }
	public Dictionary<ulong, Transform> RigidTransforms { get; } = [];
	public PaydayViewMesh( PaydayViewAssets assets )
	{
		this.assets = assets;
		var builder = Model.Builder;
		foreach ( var part in assets.Parts )
		{
			if ( part.File == assets.Gun ) continue;
			var draws = new List<(ModelFile.Draw, Mesh, Vertex[])>();
			for ( int i = 0; i < part.Geometry.Draws.Length; i++ )
			{
				var draw = part.Geometry.Draws[i];
				if ( part.Materials[i] is null || draw.Count == 0 ) continue;
				var mesh = new Mesh( part.Materials[i] );
				var vertices = new Vertex[draw.Count];
				mesh.CreateVertexBuffer<Vertex>( vertices.Length, vertices.AsSpan() );
				mesh.CreateIndexBuffer( vertices.Length, Enumerable.Range( 0, vertices.Length ).ToArray() );
				// Includes all reload/equip poses; don't cull the moving hands by their bind pose.
				mesh.Bounds = new BBox( new Vector3( -100 ), new Vector3( 100 ) );
				builder.AddMesh( mesh );
				draws.Add( (draw, mesh, vertices) );
			}
			parts.Add( (part, new Vector3[part.Geometry.Positions.Length], new Vector3[part.Geometry.Positions.Length], draws) );
		}
		Model = builder.Create();
		Update( "idle", 0 );
	}
	public void Update( string clip, float time )
	{
		var hands = assets.Hands.Pose( assets.Clips[clip], time );
		var gun = assets.Gun.Pose( null, time, assets.GunClips.GetValueOrDefault( clip ) );
		// The animation definition marks rp_fps_hands as the alignment root.
		Matrix.Invert( assets.Hands.NodePose( hands, "rp_fps_hands" ), out var handsRoot );
		var gunRoot = assets.GunRoot * assets.Hands.NodePose( hands, "a_weapon_right" ) * handsRoot;
		Muzzle = ModelFile.Convert( assets.Gun.NodePose( gun, "fire" ) * gunRoot );
		foreach ( ulong name in assets.RigidModels.Keys )
			RigidTransforms[name] = ModelFile.Convert( assets.Gun.NodePose( gun, name ) * gunRoot );
		foreach ( var part in parts )
		{
			var geometry = part.Part.Geometry;
			bool isHands = part.Part.File == assets.Hands;
			part.Part.File.Deform( geometry, isHands ? hands : gun, isHands ? handsRoot : gunRoot, part.Positions, part.Normals );
			foreach ( var (draw, mesh, vertices) in part.Draws )
			{
				Fill( geometry, draw, part.Positions, part.Normals, vertices );
				mesh.SetVertexBufferData<Vertex>( vertices.AsSpan() );
			}
		}
	}
	public static Model BuildRigid( PaydayViewAssets.Part part )
	{
		var g = part.Geometry;
		var positions = g.Positions.Select( Coordinates.Position ).ToArray();
		var normals = g.Normals.Select( n => new Vector3( n.y, -n.x, n.z ) ).ToArray();
		var builder = Model.Builder;
		for ( int i = 0; i < g.Draws.Length; i++ )
		{
			var draw = g.Draws[i];
			if ( part.Materials[i] is null || draw.Count == 0 ) continue;
			var vertices = new Vertex[draw.Count];
			Fill( g, draw, positions, normals, vertices );
			var mesh = new Mesh( part.Materials[i] );
			mesh.CreateVertexBuffer<Vertex>( vertices.Length, vertices.AsSpan() );
			mesh.CreateIndexBuffer( vertices.Length, Enumerable.Range( 0, vertices.Length ).ToArray() );
			mesh.Bounds = BBox.FromPoints( positions );
			builder.AddMesh( mesh );
		}
		return builder.Create();
	}
	static void Fill( ModelFile.Geometry geometry, ModelFile.Draw draw, Vector3[] positions, Vector3[] normals, Vertex[] vertices )
	{
		for ( int i = 0; i < draw.Count; i += 3 )
		{
			int a = geometry.Indices[draw.Start + i], b = geometry.Indices[draw.Start + i + 1], c = geometry.Indices[draw.Start + i + 2];
			var edge1 = positions[b] - positions[a]; var edge2 = positions[c] - positions[a];
			var uv1 = geometry.UVs[b] - geometry.UVs[a]; var uv2 = geometry.UVs[c] - geometry.UVs[a];
			float det = uv1.x * uv2.y - uv1.y * uv2.x;
			var tangent = MathF.Abs( det ) > 0.000001f ? (edge1 * uv2.y - edge2 * uv1.y) / det : edge1;
			var bitangent = MathF.Abs( det ) > 0.000001f ? (edge2 * uv1.x - edge1 * uv2.x) / det : edge2;
			for ( int j = 0; j < 3; j++ )
			{
				int v = geometry.Indices[draw.Start + i + j];
				var n = normals[v]; var t = (tangent - n * Vector3.Dot( tangent, n )).Normal;
				var color = geometry.Colors[v];
				vertices[i + j] = new Vertex( positions[v], n, t, geometry.UVs[v] )
				{
					Tangent = new Vector4( t.x, t.y, t.z, Vector3.Dot( Vector3.Cross( n, t ), bitangent ) < 0 ? -1 : 1 ),
					Color = new Color( color.x, color.y, color.z, color.w )
				};
			}
		}
	}
}
