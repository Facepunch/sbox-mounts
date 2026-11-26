using System;

namespace Overgrowth;

public sealed partial class CutMarks : Component, Component.INetworkSnapshot
{
	Bitmap bitmap;
	Texture texture;

	internal void Draw( List<CharacterSurface.Cut> cuts )
	{
		var lines = cuts.Select( cut => new Vector4( cut.Start.x, cut.Start.y, cut.End.x, cut.End.y ) ).ToArray();
		DrawLines( lines, Random.Shared.Next() );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void DrawLines( Vector4[] lines, int seed )
	{
		WakeDrying();
		PaintLines( lines, seed );
	}

	void PaintLines( IEnumerable<Vector4> lines, int seed )
	{
		if ( !TryPrepareBitmap( out var renderer ) ) return;
		var size = new Vector2( bitmap.Width, bitmap.Height );
		int expiry = DryTick + 253;
		bitmap.SetPen( new Color32( 255, (byte)(expiry & 255), (byte)(expiry >> 8), 255 ), 1f );
		var random = new Random( seed );
		foreach ( var line in lines )
		{
			for ( int i = 0; i < 10; i++ )
			{
				var offset = new Vector2( random.NextSingle() * 2f - 1f, random.NextSingle() * 2f - 1f );
				bitmap.DrawLine( new Vector2( line.x, line.y ) * size + offset, new Vector2( line.z, line.w ) * size + offset );
			}
		}
		Upload( renderer );
	}

	bool TryPrepareBitmap( out ModelRenderer renderer )
	{
		renderer = GetComponent<ModelRenderer>();
		if ( !renderer.IsValid() || !renderer.Model.IsValid() ) return false;
		if ( bitmap is null )
		{
			var material = renderer.Model.Materials.FirstOrDefault();
			if ( !material.IsValid() ) return false;
			var color = material.GetTexture( "g_tColor" );
			if ( !color.IsValid() ) return false;
			bitmap = new Bitmap( Math.Max( 1, color.Width / 4 ), Math.Max( 1, color.Height / 4 ) );
			bitmap.Clear( Color.Transparent );
			bitmap.SetAntialias( false );
		}
		return true;
	}

	void INetworkSnapshot.WriteSnapshot( ref ByteStream writer )
	{
		if ( bitmap is null ) return;
		writer.WriteArray( bitmap.ToPng() );
		writer.Write( DrySeconds );
		writer.Write( Math.Max( 0d, dryEnd - Time.NowDouble ) );
	}

	void INetworkSnapshot.ReadSnapshot( ref ByteStream reader )
	{
		if ( reader.ReadRemaining == 0 ) return;
		var incoming = Bitmap.CreateFromBytes( reader.ReadArray<byte>() );
		if ( incoming is null ) return;
		bitmap?.Dispose();
		bitmap = incoming;
		dryElapsed = reader.ReadRemaining > 0 ? reader.Read<double>() : 0d;
		dryStart = Time.NowDouble;
		dryEnd = dryStart + (reader.ReadRemaining > 0 ? reader.Read<double>() : 0d);
		if ( Active && GetComponent<ModelRenderer>() is { } renderer ) Upload( renderer );
		bitmap.SetAntialias( false );
	}

	protected override void OnEnabled()
	{
		if ( bitmap is not null && GetComponent<ModelRenderer>() is { } renderer ) Upload( renderer );
	}

	double dryElapsed, dryStart, dryEnd;
	int displayedDryTick = -1;
	double DrySeconds => dryElapsed + Math.Clamp( Time.NowDouble - dryStart, 0d, dryEnd - dryStart );
	int DryTick => (int)Math.Ceiling( DrySeconds * 20d );

	void WakeDrying()
	{
		dryElapsed = DrySeconds;
		dryStart = Time.NowDouble;
		dryEnd = dryStart + 5d;
		// Rebase before the two-byte expiry overflows after 50 minutes of active drying.
		if ( dryElapsed < 3000d ) return;
		dryElapsed -= 3000d;
		if ( bitmap is not null )
		{
			for ( int y = 0; y < bitmap.Height; y++ )
			for ( int x = 0; x < bitmap.Width; x++ )
			{
				var pixel = bitmap.GetPixel( x, y ).ToColor32();
				int expiry = (pixel.b << 8) + pixel.g;
				if ( expiry == 0 ) continue;
				expiry = Math.Max( 0, expiry - 60000 );
				pixel.g = (byte)(expiry & 255);
				pixel.b = (byte)(expiry >> 8);
				bitmap.SetPixel( x, y, pixel );
			}
			Upload( GetComponent<ModelRenderer>() );
		}
	}

	void SetDryTick( ModelRenderer renderer )
	{
		if ( !renderer.IsValid() ) return;
		displayedDryTick = DryTick;
		renderer.Attributes.Set( "CutDryTick", (float)displayedDryTick );
	}

	void Upload( ModelRenderer renderer )
	{
		if ( Application.IsDedicatedServer || bitmap is null || !renderer.IsValid() ) return;
		if ( texture is null ) texture = bitmap.ToTexture( false );
		else texture.Update( bitmap );
		renderer.Attributes.Set( "CutMask", texture );
		SetDryTick( renderer );
		UpdateAppearance( renderer );
	}

	internal static void RefreshAppearance()
	{
		if ( Game.ActiveScene is not { } scene ) return;
		foreach ( var marks in scene.GetAllComponents<CutMarks>() )
			if ( marks.texture is not null && marks.GetComponent<ModelRenderer>() is { } renderer ) UpdateAppearance( renderer );
	}

	static void UpdateAppearance( ModelRenderer renderer )
	{
		renderer.Attributes.Set( "CutTint", RabbitGuard.BloodColor );
		renderer.Attributes.Set( "HasCuts", RabbitGuard.BloodLevel > 0 );
	}

	internal void TransferTo( GameObject corpse )
	{
		if ( bitmap is null && drips.Count == 0 ) return;
		var next = corpse.GetOrAddComponent<CutMarks>();
		next.drips.AddRange( drips );
		drips.Clear();
		next.dripClock = dripClock;
		next.dryElapsed = dryElapsed;
		next.dryStart = dryStart;
		next.dryEnd = dryEnd;
		next.bitmap = bitmap;
		next.texture = texture;
		next.Upload( corpse.GetComponent<ModelRenderer>() );
		bitmap = null;
		texture = null;
		if ( corpse.Network.Active ) corpse.Network.Refresh();
	}

	protected override void OnDestroy()
	{
		bitmap?.Dispose();
		texture?.Dispose();
	}
}
