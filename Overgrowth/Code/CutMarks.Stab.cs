using System;

namespace Overgrowth;

public sealed partial class CutMarks
{
	internal void Stab( Vector3 origin, Vector3 direction, bool light )
	{
		var renderer = GetComponent<SkinnedModelRenderer>();
		if ( direction.IsNearlyZero() || CharacterLoader.FromModel( renderer.Model )?.Surface is not { } surface ) return;
		var faces = surface.StabFaces( renderer, origin, direction, out var posed, out float size );
		if ( faces.Count == 0 ) return;

		Vector3 jitter = new( Random.Shared.Float( -1f, 1f ), Random.Shared.Float( -1f, 1f ), Random.Shared.Float( -1f, 1f ) );
		var x = direction.Cross( jitter ).Normal;
		var y = x.Cross( direction ).Normal;
		var projection = new Vector4[faces.Count * 3];
		var clouds = new List<Vector3>();
		var splats = new List<Vector3>();
		for ( int i = 0; i < faces.Count; i++ )
		{
			Vector3 sum = default;
			for ( int j = 0; j < 3; j++ )
			{
				var (position, uv) = surface.CornerAt( faces[i] + j, posed );
				sum += position;
				var offset = position - origin;
				projection[i * 3 + j] = new( uv.x, uv.y, offset.Dot( x ) / size + 0.5f, 0.5f - offset.Dot( y ) / size );
			}
			if ( Random.Shared.Next( faces.Count ) == 0 ) clouds.Add( sum / 3f );
			if ( light && Random.Shared.Next( 40 ) == 0 ) splats.Add( sum / 3f );
		}
		DrawStab( projection );
		if ( clouds.Count + splats.Count > 0 ) PlayStabParticles( clouds.ToArray(), splats.ToArray(), direction );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayStabParticles( Vector3[] clouds, Vector3[] splats, Vector3 direction )
	{
		if ( RabbitGuard.BloodLevel == 0 || Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		foreach ( var point in clouds )
		{
			mount.EmitBillboard( "Data/Particles/bloodcloud.xml", point, (RabbitGuard.ParticleScatter() + direction) * 40f, RabbitGuard.BloodColor );
			mount.EmitBillboard( "Data/Particles/bloodcloud.xml", point, (RabbitGuard.ParticleScatter() - direction) * 40f, RabbitGuard.BloodColor );
		}
		foreach ( var point in splats ) mount.EmitBillboard( "Data/Particles/bloodsplat.xml", point, RabbitGuard.ParticleScatter() * 80f, RabbitGuard.BloodColor );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal void DrawStab( Vector4[] corners )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		WakeDrying();
		DrawProjection( corners, mount.StabDecal, false );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal void DrawItemSpatter( Vector3 point, Vector3 across, Vector3 up )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ||
			mount.GetByPath( GetComponent<ModelRenderer>().Model.ResourcePath ) is not ObjectLoader loader ) return;
		var (positions, uvs, indices) = loader.GetItemSurface();
		var corners = new Vector4[indices.Count];
		for ( int i = 0; i < indices.Count; i++ )
		{
			int vertex = indices[i];
			var uv = uvs[vertex];
			var offset = positions[vertex] - point;
			corners[i] = new( uv.x, uv.y, offset.Dot( across ) + 0.5f, 0.5f - offset.Dot( up ) );
		}
		DrawProjection( corners, mount.SpatterDecal, true );
	}

	void DrawProjection( Vector4[] corners, GameMount.DecalStamp stamp, bool wet )
	{
		var renderer = PrepareBitmap();
		var size = new Vector2( bitmap.Width, bitmap.Height );
		for ( int i = 0; i < corners.Length; i += 3 )
		{
			var a = corners[i];
			var b = corners[i + 1];
			var c = corners[i + 2];
			if ( OutsideStamp( a.z, b.z, c.z ) || OutsideStamp( a.w, b.w, c.w ) ) continue;
			var p = new Vector2( a.x, a.y ) * size;
			var q = new Vector2( b.x, b.y ) * size;
			var r = new Vector2( c.x, c.y ) * size;
			float area = Cross( q - p, r - p );
			if ( MathF.Abs( area ) < 0.000001f ) continue;
			var min = Vector2.Min( p, Vector2.Min( q, r ) );
			var max = Vector2.Max( p, Vector2.Max( q, r ) );
			for ( int y = Math.Max( 0, (int)MathF.Ceiling( min.y - 0.5f ) ); y < bitmap.Height && y + 0.5f <= max.y; y++ )
			for ( int x = Math.Max( 0, (int)MathF.Ceiling( min.x - 0.5f ) ); x < bitmap.Width && x + 0.5f <= max.x; x++ )
			{
				var offset = new Vector2( x + 0.5f, y + 0.5f ) - p;
				float u = Cross( offset, r - p ) / area;
				float v = Cross( q - p, offset ) / area;
				if ( u < 0f || v < 0f || u + v > 1f ) continue;
				var coords = a + (b - a) * u + (c - a) * v;
				if ( coords.z is < 0f or > 1f || coords.w is < 0f or > 1f ) continue;
				float red = stamp.Sample( coords.z, coords.w );
				if ( red <= 0f ) continue;
				float alpha = Math.Min( 1f, red * 10f );
				var pixel = bitmap.GetPixel( x, y );
				pixel.r = Math.Min( 1f, pixel.r + red * alpha );
				if ( wet ) pixel.g = Math.Min( 1f, pixel.g + alpha );
				pixel.a = Math.Min( 1f, pixel.a + alpha * alpha );
				bitmap.SetPixel( x, y, pixel );
			}
		}
		Upload( renderer );
	}

	static float Cross( Vector2 a, Vector2 b ) => a.x * b.y - a.y * b.x;
	static bool OutsideStamp( float a, float b, float c ) => (a < 0f && b < 0f && c < 0f) || (a > 1f && b > 1f && c > 1f);
}

public partial class GameMount
{
	internal sealed class DecalStamp( Texture texture )
	{
		readonly Color32[] pixels = texture.GetPixels();
		internal float Sample( float u, float v )
		{
			float x = Math.Clamp( u * texture.Width - 0.5f, 0f, texture.Width - 1f );
			float y = Math.Clamp( v * texture.Height - 0.5f, 0f, texture.Height - 1f );
			int left = (int)x, top = (int)y;
			int right = Math.Min( left + 1, texture.Width - 1 ), bottom = Math.Min( top + 1, texture.Height - 1 );
			float Shade( int sx, int sy ) => pixels[sy * texture.Width + sx].r / 255f;
			return MathX.Lerp( MathX.Lerp( Shade( left, top ), Shade( right, top ), x - left ),
				MathX.Lerp( Shade( left, bottom ), Shade( right, bottom ), x - left ), y - top );
		}
	}

	DecalStamp spatterDecal;
	internal DecalStamp SpatterDecal => spatterDecal ??= new( LoadTexture( "Data/Textures/spatterdecal.tga" ) );

	DecalStamp stabDecal;
	internal DecalStamp StabDecal => stabDecal ??= new( LoadTexture( "Data/Textures/stabdecal.tga" ) );
}
