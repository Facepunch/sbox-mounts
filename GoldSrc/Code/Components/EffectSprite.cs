namespace GoldSrc.Maps;

public sealed class EffectSprite
{
	public List<SpriteEffect.Frame> Frames { get; set; } = [];
	public List<SpriteEffect.Group> Groups { get; set; } = [];
	public int TextureFormat { get; set; }

	public SpriteEffect Spawn( MapState map, Vector3 position, float scale, float frameRate, int renderMode, Color color, Vector3 velocity = default )
	{
		if ( Frames.Count == 0 || Groups.Count == 0 || scale == 0 ) return null;
		var go = new GameObject( true, "GoldSrc temporary sprite" );
		if ( map.IsValid() ) go.Parent = map.GameObject;
		go.WorldPosition = position;
		var effect = go.AddComponent<SpriteEffect>();
		effect.Map = map;
		effect.Frames = Frames;
		effect.Groups = Groups;
		effect.TextureFormat = TextureFormat;
		effect.Temporary = true;
		effect.StartOn = true;
		effect.Scale = scale;
		effect.FrameRate = frameRate;
		effect.RenderMode = renderMode;
		effect.RenderColor = color;
		effect.RenderAmount = color.a * 255;
		effect.Velocity = velocity;
		effect.Renderer = go.AddComponent<SpriteRenderer>( false );
		effect.Renderer.Lighting = false;
		effect.Renderer.Shadows = false;
		effect.Renderer.IsSorted = true;
		effect.Renderer.FogStrength = 0;
		effect.Renderer.Billboard = SpriteRenderer.BillboardMode.Always;
		effect.Initialize();
		return effect;
	}
}
