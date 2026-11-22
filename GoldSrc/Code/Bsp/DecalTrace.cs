using System;

namespace GoldSrc.Bsp;

internal static class GoldSrcDecalTrace
{
	internal static bool Project( BspFile bsp, BspModel model, Vector3 origin, float limit,
		out Vector3 position, out Vector3 normal, out Vector3 textureT )
	{
		position = default;
		normal = default;
		textureT = default;
		var found = false;
		for ( var index = model.FirstFace; index < model.FirstFace + model.FaceCount; index++ )
		{
			var face = bsp.Faces[index];
			if ( face.EdgeCount < 3 )
				continue;
			var info = bsp.TexInfos[face.TextureInfo];
			var texture = bsp.Textures[info.MipTex];
			if ( texture.IsTool || texture.IsSky || texture.IsLiquid )
				continue;
			var plane = bsp.Planes[face.Plane];
			var distance = Vector3.Dot( origin, plane.Normal ) - plane.Distance;
			if ( MathF.Abs( distance ) > limit )
				continue;
			var projected = origin - plane.Normal * distance;
			var side = 0f;
			var inside = true;
			for ( var edge = 0; edge < face.EdgeCount; edge++ )
			{
				var a = bsp.VertexFromSurfEdge( face.FirstEdge + edge );
				var b = bsp.VertexFromSurfEdge( face.FirstEdge + (edge + 1) % face.EdgeCount );
				var cross = Vector3.Dot( Vector3.Cross( b - a, projected - a ), plane.Normal );
				if ( MathF.Abs( cross ) < 0.001f )
					continue;
				if ( side != 0f && MathF.Sign( side ) != MathF.Sign( cross ) )
				{
					inside = false;
					break;
				}
				side = cross;
			}
			if ( !inside )
				continue;
			limit = MathF.Abs( distance );
			position = projected;
			normal = plane.Normal * (face.PlaneSide == 0 ? 1 : -1);
			textureT = info.T;
			found = true;
		}
		return found;
	}
}
