using System;

namespace Source1;

partial class MapLoader
{
	private enum ShadowBacking { None, NoDraw, BlockLight }

	// VBSP strips nodraw backing from the render mesh, including the solid roof above inset lights.
	private static void AddHiddenShadowFaces( BspFile bsp, int modelIndex, HashSet<int> brushIds, Func<Vector3, MeshGroup> chunkFor, Func<int, bool> isOpaque )
	{
		if ( bsp.leafBrushes.Length == 0 ) return;
		var backingTextures = new Dictionary<int, ShadowBacking>();
		var model = bsp.Models[modelIndex];
		var pending = new Stack<int>();

		var polygon = new List<Vector3>();
		var clipped = new List<Vector3>();
		var center = (model.Mins + model.Maxs) * 0.5f;
		var radius = (model.Maxs - model.Mins).Length + 1;
		foreach ( var id in brushIds )
		{
			var brush = bsp.collisionBrushes[id];
			if ( (brush.Contents & (BspContents.Solid | BspContents.Opaque)) == 0 ) continue;
			var sides = bsp.collisionBrushSides.AsSpan( brush.FirstSide, brush.SideCount );
			bool hasBacking = false, passesLight = false;
			bool backsGeometry = modelIndex == 0;
			foreach ( var side in sides )
			{
				if ( side.Bevel != 0 || side.TexInfo < 0 ) continue;
				var info = bsp.TexInfos[side.TexInfo];
				if ( side.DispInfo > 0 || (info.Flags & (BspSurfaceFlags.SkyMask | BspSurfaceFlags.Warp | BspSurfaceFlags.NoShadows)) != 0
					|| ((info.Flags & BspSurfaceFlags.NoDraw) == 0 && info.TexData >= 0 && !isOpaque( info.TexData )) )
				{
					passesLight = true;
					break;
				}
				var backing = GetBacking( side.TexInfo );
				hasBacking |= backing != ShadowBacking.None;
				backsGeometry |= (info.Flags & BspSurfaceFlags.NoDraw) == 0 || backing == ShadowBacking.BlockLight;
			}
			if ( !hasBacking || !backsGeometry || passesLight ) continue;

			foreach ( var side in sides )
			{
				if ( side.Bevel != 0 || GetBacking( side.TexInfo ) == ShadowBacking.None ) continue;
				var plane = bsp.Planes[side.Plane];
				var normal = plane.Normal;
				var tangent = Vector3.Cross( normal, MathF.Abs( normal.z ) < 0.9f ? Vector3.Up : Vector3.Right ).Normal;
				BuildBrushFace( bsp, sides, side, tangent, center, radius, ref polygon, ref clipped );
				if ( polygon.Count < 3 ) continue;
				if ( modelIndex == 0 && bsp.skyboxAreas.Count > 0 )
				{
					var bounds = new BBox( polygon[0], polygon[0] );
					foreach ( var point in polygon ) bounds = bounds.AddPoint( point );
					if ( bsp.IsSkyboxBounds( new BBox( bounds.Mins - Vector3.One, bounds.Maxs + Vector3.One ), pending, out _ ) ) continue;
				}
				var group = chunkFor( polygon[0] );
				int first = group.Vertices.Count;
				foreach ( var point in polygon )
					group.Vertices.Add( new BlendLightmapVertex( point, normal, tangent, default, default ) );
				for ( int i = 1; i < polygon.Count - 1; i++ )
				{
					// Hidden surfaces have no visible mesh to cast from the opposite side.
					group.Indices.AddRange( [first, first + i, first + i + 1, first, first + i + 1, first + i] );
				}
			}
		}

		ShadowBacking GetBacking( int texInfo )
		{
			if ( texInfo < 0 ) return ShadowBacking.None;
			var info = bsp.TexInfos[texInfo];
			if ( info.TexData < 0 || (info.Flags & BspSurfaceFlags.NoDraw) == 0 ) return ShadowBacking.None;
			if ( backingTextures.TryGetValue( info.TexData, out var backing ) ) return backing;
			var name = bsp.GetTexDataString( info.TexData );
			if ( string.Equals( name, "tools/toolsnodraw", StringComparison.OrdinalIgnoreCase ) ) backing = ShadowBacking.NoDraw;
			else if ( string.Equals( name, "tools/toolsblocklight", StringComparison.OrdinalIgnoreCase ) ) backing = ShadowBacking.BlockLight;
			return backingTextures[info.TexData] = backing;
		}
	}
}
