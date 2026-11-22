using System;
using System.Text.Json.Serialization;

namespace GoldSrc.Maps;

[Hide]
public sealed class RenderAppearance : Component
{
	public sealed class MaterialSlot
	{
		public ModelRenderer Renderer { get; set; }
		public int Index { get; set; }
		public Material OriginalOverride { get; set; }
		[JsonIgnore] public Material Material => OriginalOverride ?? Renderer.MaterialOverride ?? Renderer.Materials.GetOriginal( Index );
	}

	[Property] public List<ModelRenderer> Renderers { get; set; } = [];
	[Property] public List<MaterialSlot> Materials { get; set; } = [];
	[Property] public int InitialMode { get; set; }
	[Property] public int RenderMode { get; set; }
	[Property] public int RenderFx { get; set; }
	[Property] public float RenderAmount { get; set; }
	[Property] public Color RenderColor { get; set; }
	[Property] public bool Applied { get; set; }

	private readonly Dictionary<MaterialSlot, Material> _copies = [];
	private int _lastMode = -1;
	private bool _warnedMode;

	public void CaptureMaterials()
	{
		Materials.Clear();
		foreach ( var renderer in Renderers )
		{
			for ( int i = 0; i < renderer.Materials.Count; i++ )
			{
				var original = renderer.Materials.GetOverride( i );
				Materials.Add( new MaterialSlot
				{
					Renderer = renderer,
					Index = i,
					OriginalOverride = original
				} );
			}
		}
	}

	protected override void OnEnabled()
	{
		if ( Applied ) UpdateRenderer();
	}

	public void UpdateRenderer()
	{
		Applied = true;
		var tint = RenderMode == 1 ? RenderColor : Color.White;
		tint.a = RenderMode is 1 or 2 or 3 or 5 ? RenderAmount / 255f : 1f;
		foreach ( var renderer in Renderers )
			if ( renderer.IsValid() ) renderer.Tint = tint;

		if ( _lastMode == RenderMode ) return;
		_lastMode = RenderMode;
		if ( Blend( RenderMode ) == Blend( InitialMode ) && _copies.Count == 0 ) return;
		foreach ( var slot in Materials )
		{
			if ( !slot.Renderer.IsValid() || !slot.Material.IsValid() ) continue;
			if ( !slot.Material.ShaderName.EndsWith( "goldsrc_generic.shader", StringComparison.OrdinalIgnoreCase ) )
			{
				if ( !_warnedMode )
				{
					_warnedMode = true;
					Log.Warning( $"GoldSrc: '{GameObject.Name}' cannot change blending for shader '{slot.Material.ShaderName}'." );
				}
				continue;
			}
			if ( !_copies.TryGetValue( slot, out var copy ) )
			{
				copy = slot.Material.CreateCopy();
				if ( copy is null )
					throw new InvalidOperationException( $"Could not copy GoldSrc material '{slot.Material.Name}'." );
				_copies.Add( slot, copy );
			}
			copy.SetFeature( "F_ADDITIVE_BLEND", 0 );
			copy.SetFeature( "F_TRANSLUCENT", Blend( RenderMode ) == 0 ? 0 : 1 );
			copy.SetFeature( "F_ADDITIVE_BLEND", Blend( RenderMode ) == 2 ? 1 : 0 );
			slot.Renderer.Materials.SetOverride( slot.Index, copy );
		}
	}

	private static int Blend( int mode ) => mode switch { 1 or 2 => 1, 3 or 5 => 2, _ => 0 };

	protected override void OnDisabled()
	{
		foreach ( var (slot, copy) in _copies )
			if ( slot.Renderer.IsValid() && slot.Renderer.Materials.GetOverride( slot.Index ) == copy )
				slot.Renderer.Materials.SetOverride( slot.Index, slot.OriginalOverride );
		_copies.Clear();
		_lastMode = -1;
	}
}
