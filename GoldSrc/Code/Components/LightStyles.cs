using System;

namespace GoldSrc;

[Category( "GoldSrc" ), Icon( "lightbulb" )]
public sealed class LightStyles : Component, Component.ExecuteInEditor
{
	public const int StyleCount = 64;
	public const int MaxPatternLength = 63;

	[Property, Sync( SyncFlags.FromHost )]
	public NetDictionary<int, string> Patterns { get; set; } = CreatePatterns();

	[Property]
	public List<Renderer> Renderers { get; set; } = [];

	private readonly float[] _values = new float[StyleCount];
	private Texture _texture;
	private bool _initialized;

	public void SetStyle( int style, string pattern )
	{
		if ( (uint)style >= StyleCount )
			throw new ArgumentOutOfRangeException( nameof( style ) );
		ArgumentNullException.ThrowIfNull( pattern );
		if ( Networking.IsActive && !Networking.IsHost ) return;

		int end = pattern.IndexOf( '\0' );
		int length = Math.Min( end < 0 ? pattern.Length : end, MaxPatternLength );
		Patterns[style] = pattern[..length];
	}

	public int GetValue( int style, double time )
	{
		if ( (uint)style >= StyleCount )
			throw new ArgumentOutOfRangeException( nameof( style ) );
		Patterns.TryGetValue( style, out var pattern );
		return Evaluate( pattern, time );
	}

	public static int Evaluate( string pattern, double time )
	{
		if ( string.IsNullOrEmpty( pattern ) ) return 256;
		int end = pattern.IndexOf( '\0' );
		int length = Math.Min( end < 0 ? pattern.Length : end, MaxPatternLength );
		if ( length == 0 ) return 256;
		int frame = (int)(Math.Max( 0, time ) * 10.0);
		return (pattern[frame % length] - 'a') * 22;
	}

	protected override void OnEnabled()
	{
		_initialized = false;
		UpdateValues();
	}

	protected override void OnPreRender() => UpdateValues();

	protected override void OnDestroy()
	{
		foreach ( var renderer in Renderers )
		{
			if ( renderer.IsValid() )
				renderer.Attributes.Set( "GoldSrcLightStyles", (Texture)null );
		}
		_texture?.Dispose();
		_texture = null;
	}

	private void UpdateValues()
	{
		if ( Application.IsDedicatedServer ) return;
		bool changed = !_initialized;
		for ( int style = 0; style < StyleCount; style++ )
		{
			float value = GetValue( style, Time.NowDouble ) / 256.0f;
			if ( _values[style] == value ) continue;
			_values[style] = value;
			changed = true;
		}

		if ( !changed ) return;
		_texture ??= Texture.Create( StyleCount, 1, ImageFormat.R32F ).WithDynamicUsage().Finish();
		if ( !_texture.IsValid || _texture.IsError )
			throw new InvalidOperationException( $"{GameObject.Name}: failed to create lightstyle texture." );
		_texture.Update<float>( _values, width: StyleCount, height: 1 );
		if ( _initialized ) return;
		_initialized = true;
		foreach ( var renderer in Renderers )
		{
			if ( renderer.IsValid() )
				renderer.Attributes.Set( "GoldSrcLightStyles", _texture );
		}
	}

	private static NetDictionary<int, string> CreatePatterns() => new()
	{
		[0] = "m",
		[1] = "mmnmmommommnonmmonqnmmo",
		[2] = "abcdefghijklmnopqrstuvwxyzyxwvutsrqponmlkjihgfedcba",
		[3] = "mmmmmaaaaammmmmaaaaaabcdefgabcdefg",
		[4] = "mamamamamama",
		[5] = "jklmnopqrstuvwxyzyxwvutsrqponmlkj",
		[6] = "nmonqnmomnmomomno",
		[7] = "mmmaaaabcdefgmmmmaaaammmaamm",
		[8] = "mmmaaammmaaammmabcdefaaaammmmabcdefmmmaaaa",
		[9] = "aaaaaaaazzzzzzzz",
		[10] = "mmamammmmammamamaaamammma",
		[11] = "abcdefghijklmnopqrrqponmlkjihgfedcba",
		[12] = "mmnnmmnnnmmnn",
		[63] = "a"
	};
}
