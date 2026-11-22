using System;
using Doom.Wad;

namespace Doom;

/// <summary>
/// Switch textures. When a switch or gun line fires, vanilla's P_ChangeSwitchTexture swaps the
/// first of the front side's upper, middle and lower textures that is in the switch list for its
/// partner. Vanilla's list is entirely SW1xxx/SW2xxx pairs, so any texture with a partner in the
/// WAD stack counts. That one wall piece is built as its own model, so its material can be swapped.
/// </summary>
partial class MapLoader
{
	private enum WallPart { Upper, Middle, Lower }

	private sealed class SwitchFace( WallPart part, string texture, string partner )
	{
		public readonly WallPart Part = part;
		public readonly string Texture = texture;
		public readonly string Partner = partner;
		public Batch Batch;
		public Carrier Carrier;
		public ModelRenderer Renderer;
	}

	private Dictionary<int, SwitchFace> _switches;

	/// <summary>The texture a switch texture flips to: SW1xxx and SW2xxx swap. Null if it isn't a switch.</summary>
	protected virtual string GetSwitchPartner( string texture )
	{
		if ( texture is null || texture.Length < 4 || !texture.StartsWith( "SW" ) ) return null;

		var partner = texture[2] switch
		{
			'1' => "SW2" + texture[3..],
			'2' => "SW1" + texture[3..],
			_ => null
		};

		return partner is not null && Wads.FindTexture( partner, out _ ) is not null ? partner : null;
	}

	private void FindSwitches()
	{
		_switches = [];

		for ( int i = 0; i < _lines.Count; i++ )
		{
			if ( _lineSpecials[i]?.Trigger is not (LineTrigger.Use or LineTrigger.Shoot) ) continue;
			if ( !TryGetSide( i, true, out var side, out _, out _ ) ) continue;

			foreach ( var (part, texture) in (ReadOnlySpan<(WallPart, string)>)[(WallPart.Upper, side.Upper), (WallPart.Middle, side.Middle), (WallPart.Lower, side.Lower)] )
			{
				if ( !Sidedef.IsSet( texture ) || GetSwitchPartner( texture ) is not { } partner ) continue;

				_switches[i] = new SwitchFace( part, texture, partner );
				break;
			}
		}
	}

	/// <summary>The batch a wall piece goes into: its own if it's the line's switch face, otherwise its carrier's.</summary>
	private Batch WallBatch( int line, bool front, WallPart part, Carrier carrier )
	{
		if ( !front || !_switches.TryGetValue( line, out var face ) || face.Part != part )
			return BatchFor( carrier );

		face.Carrier = carrier;
		return face.Batch ??= new Batch();
	}

	/// <summary>Builds each switch face under whatever it rides on. Runs after the planes are placed.</summary>
	private void FinishSwitches()
	{
		foreach ( var (line, face) in _switches )
		{
			if ( face.Batch is null ) continue;

			var go = BuildOnCarrier( face.Carrier, $"Switch {line}", face.Batch, $"{ModelPrefix}/switch{line}" );
			face.Renderer = go.GetComponent<ModelRenderer>();
		}
	}
}
