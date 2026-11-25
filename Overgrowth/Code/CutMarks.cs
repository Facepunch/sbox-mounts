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
		var renderer = PrepareBitmap();
		var size = new Vector2( bitmap.Width, bitmap.Height );
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

	ModelRenderer PrepareBitmap()
	{
		var renderer = GetComponent<ModelRenderer>();
		if ( bitmap is null )
		{
			var color = renderer.Model.Materials[0].GetTexture( "g_tColor" );
			bitmap = new Bitmap( Math.Max( 1, color.Width / 4 ), Math.Max( 1, color.Height / 4 ) );
			bitmap.Clear( Color.Transparent );
			ConfigurePen();
		}
		return renderer;
	}

	void INetworkSnapshot.WriteSnapshot( ref ByteStream writer )
	{
		if ( bitmap is not null ) writer.WriteArray( bitmap.ToPng() );
	}

	void INetworkSnapshot.ReadSnapshot( ref ByteStream reader )
	{
		if ( reader.ReadRemaining == 0 ) return;
		bitmap?.Dispose();
		bitmap = Bitmap.CreateFromBytes( reader.ReadArray<byte>() );
		if ( Active && GetComponent<ModelRenderer>() is { } renderer ) Upload( renderer );
		ConfigurePen();
	}

	protected override void OnEnabled()
	{
		if ( bitmap is not null && GetComponent<ModelRenderer>() is { } renderer ) Upload( renderer );
	}

	void ConfigurePen()
	{
		bitmap.SetAntialias( false );
		bitmap.SetPen( new Color( 1f, 253f / 255f, 0f ), 1f );
	}

	void Upload( ModelRenderer renderer )
	{
		if ( Application.IsDedicatedServer || bitmap is null ) return;
		if ( texture is null ) texture = bitmap.ToTexture( false );
		else texture.Update( bitmap );
		renderer.Attributes.Set( "CutMask", texture );
		UpdateAppearance( renderer );
	}

	internal static void RefreshAppearance()
	{
		if ( Game.ActiveScene is not { } scene ) return;
		foreach ( var marks in scene.GetAllComponents<CutMarks>() )
			if ( marks.texture is not null ) UpdateAppearance( marks.GetComponent<ModelRenderer>() );
	}

	static void UpdateAppearance( ModelRenderer renderer )
	{
		renderer.Attributes.Set( "CutTint", RabbitGuard.BloodColor );
		renderer.Attributes.Set( "HasCuts", RabbitGuard.BloodLevel > 0 );
	}

	internal void TransferTo( GameObject corpse )
	{
		if ( bitmap is null ) return;
		var next = corpse.GetOrAddComponent<CutMarks>();
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
