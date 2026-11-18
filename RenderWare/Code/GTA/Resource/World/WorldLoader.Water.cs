using System;

namespace Renderware;

partial class WorldLoader
{
	/// <summary>
	/// The water polygons as one translucent mesh, textured like the game (one tile per 32 units) and tinted with
	/// the time cycle's water colour.
	/// </summary>
	void BuildWater( WaterData water, Color waterColor )
	{
		var verts = new List<SimpleVertex>();
		var indices = new List<int>();

		foreach ( var polygon in water.Polygons )
		{
			int start = verts.Count;
			foreach ( var p in polygon )
			{
				verts.Add( new SimpleVertex
				{
					Position = p * Units.Scale,
					Normal = Vector3.Up,
					Tangent = Vector3.Forward,
					TexCoord = new Vector2( p.x, p.y ) / 32f,
					Color = new Color32( 255, 255, 255, 255 ),
				} );
			}

			// fan - polygons are convex
			for ( int i = 1; i + 1 < polygon.Length; i++ )
				indices.AddRange( new[] { start, start + i, start + i + 1 } );
		}

		if ( indices.Count == 0 )
			return;

		var material = Material.Create( "renderware_water.vmat", "shaders/renderware_water.shader" );
		var texture = Host.Game == GameVersion.III ? Host.FindTexture( "water_old", "waterclear256" ) : Host.FindTexture( "waterclear256", "water_old" );
		if ( Host.LoadTexture( texture ) is { } tex )
			material.Set( "Color", tex );
		material.Set( "g_vWaterColor", new Vector3( waterColor.r, waterColor.g, waterColor.b ) );

		var mesh = new Mesh( material );
		mesh.CreateIndexBuffer( indices.Count, indices.ToArray() );
		mesh.CreateVertexBuffer( verts.Count, verts.ToArray() );
		// translucent things draw far to near by their bounds' centre, and the engine has no other way to order them. The
		// water should go first so beams, glass and glows above it blend over it - so its bounds deliberately reach far
		// below the map, putting the centre further away than anything else (and still containing all the water)
		var bounds = BBox.FromPoints( verts.Select( v => v.Position ) );
		mesh.Bounds = bounds.AddPoint( bounds.Center.WithZ( -1e7f ) );

		var renderer = new GameObject( true, "water" ).AddComponent<ModelRenderer>();
		renderer.Model = Model.Builder.WithName( "world_water" ).AddMesh( mesh ).Create();
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off; // translucent, no depth pass

		Log.Info( $"World water: {water.Polygons.Count} polygons" );
	}
}
