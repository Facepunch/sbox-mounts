using System;
using System.IO;

namespace GoldSrc;

[Hide, Category( "GoldSrc" )]
public sealed class LightStyleProbes : Component, Component.ExecuteInEditor, Component.DontExecuteOnServer
{
	internal const int SampleStride = 17;

	[Property] public LightStyles Styles { get; set; }
	[Property] public Vector3Int GridSize { get; set; }
	[Property] public BBox Bounds { get; set; }
	[Property] public int Priority { get; set; }
	[Property] public bool Fullbright { get; set; }
	[Property] public byte[] Samples { get; set; } = [];

	private GameObject _helper;
	private Texture _texture;
	private byte[] _pixels;
	private readonly int[] _values = new int[LightStyles.StyleCount];
	private int[] _usedStyles;

	protected override void OnEnabled() => UpdateLighting();
	protected override void OnPreRender() => UpdateLighting();

	protected override void OnDisabled()
	{
		_helper?.Destroy();
		_helper = null;
		_texture?.Dispose();
		_texture = null;
		_pixels = null;
		_usedStyles = null;
	}

	private void UpdateLighting()
	{
		bool create = _texture is null;
		if ( create )
		{
			if ( GridSize.x <= 0 || GridSize.y <= 0 || GridSize.z <= 0
				|| GridSize.x > 16 || GridSize.y > 16 || GridSize.z > 16
				|| Samples.Length != GridSize.x * GridSize.y * GridSize.z * SampleStride )
				throw new InvalidDataException( $"{GameObject.Name}: invalid light probe samples." );
			if ( !Styles.IsValid() )
				throw new InvalidDataException( $"{GameObject.Name}: missing lightstyles." );

			var used = new HashSet<int>();
			for ( int sample = 0; sample < Samples.Length; sample += SampleStride )
			{
				if ( Samples[sample] == 0 ) continue;
				for ( int slot = 0; slot < 4; slot++ )
				{
					int style = Samples[sample + 4 + slot * 4];
					if ( style == 255 ) break;
					if ( style >= LightStyles.StyleCount )
						throw new InvalidDataException( $"{GameObject.Name}: invalid lightstyle {style}." );
					used.Add( style );
				}
			}
			_usedStyles = used.ToArray();
			_pixels = new byte[GridSize.x * GridSize.y * GridSize.z * 48];
		}

		bool changed = create;
		if ( !Fullbright )
		{
			foreach ( int style in _usedStyles )
			{
				int value = Styles.GetValue( style, Time.NowDouble );
				if ( _values[style] == value ) continue;
				_values[style] = value;
				changed = true;
			}
		}
		if ( !changed ) return;

		int probes = Samples.Length / SampleStride;
		for ( int probe = 0; probe < probes; probe++ )
		{
			int sample = probe * SampleStride;
			if ( Samples[sample] == 0 ) continue;
			var color = Fullbright ? new Vector3( 255 ) : SampleColor( sample );
			for ( int side = 0; side < 6; side++ )
			{
				float lightcos = side == 2 ? -1 : side == 5 ? 1 : 0;
				float shade = Fullbright ? 1 : 1 - 0.9f * Math.Max( 0, (lightcos + 0.4953241f) / 1.4953241f );
				int offset = (side * probes + probe) * 8;
				WriteHalf( offset, Linear( color.x * shade ) );
				WriteHalf( offset + 2, Linear( color.y * shade ) );
				WriteHalf( offset + 4, Linear( color.z * shade ) );
				WriteHalf( offset + 6, 1 );
			}
		}

		if ( !create )
		{
			_texture.Update3D( _pixels, width: GridSize.x, height: GridSize.y, depth: GridSize.z * 6 );
			return;
		}

		_texture = Texture.CreateVolume( GridSize.x, GridSize.y, GridSize.z * 6, ImageFormat.RGBA16161616F )
			.WithDynamicUsage().WithData( _pixels ).Finish();
		if ( _texture is null || !_texture.IsValid || _texture.IsError )
			throw new InvalidOperationException( $"{GameObject.Name}: failed to create model light probes." );
		_helper = new GameObject( GameObject, false, "light probe volume" )
		{
			Flags = GameObjectFlags.NotSaved | GameObjectFlags.Hidden | GameObjectFlags.NotNetworked
		};
		var volume = _helper.AddComponent<LightProbeVolume>();
		volume.Bounds = Bounds;
		volume.Priority = Priority;
		volume.Texture = _texture;
		_helper.Enabled = true;
	}

	private Vector3 SampleColor( int sample )
	{
		int red = 0, green = 0, blue = 0;
		for ( int slot = 0; slot < 4; slot++ )
		{
			int offset = sample + 1 + slot * 4;
			int style = Samples[offset + 3];
			if ( style == 255 ) break;
			int value = _values[style];
			red += Samples[offset] * value;
			green += Samples[offset + 1] * value;
			blue += Samples[offset + 2] * value;
		}
		return new Vector3( red >> 8, green >> 8, blue >> 8 ).Clamp( 0, 255 );
	}

	private void WriteHalf( int offset, float value ) =>
		BitConverter.TryWriteBytes( _pixels.AsSpan( offset, 2 ), (Half)value );

	private static float Linear( float value )
	{
		value = Math.Clamp( value / 255, 0, 1 );
		return value <= 0.04045f ? value / 12.92f : MathF.Pow( (value + 0.055f) / 1.055f, 2.4f );
	}
}
