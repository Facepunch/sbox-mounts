namespace Source1.Hl2;

/// <summary>A camera-facing sheet of spray rising from an underwater explosion.</summary>
[Title( "HL2 Water Shaft" )]
[Category( "Half-Life 2" )]
public sealed class WaterShaft : Component
{
	SpriteRenderer _sprite;
	Vector3 _surface;
	float _width, _height, _speed, _lifetime;
	TimeSince _age;

	internal static void CreatePair( Vector3 surface, string mount )
	{
		var lifetime = Game.Random.Float( 0.4f, 0.5f );
		Create( surface, mount, lifetime, 50, Game.Random.Float( 50, 75 ), Game.Random.Float( 650, 750 ) );
		Create( surface, mount, lifetime, 100, Game.Random.Float( 32, 64 ), Game.Random.Float( 64, 128 ) );
	}

	static void Create( Vector3 surface, string mount, float lifetime, float width, float height, float speed )
	{
		var sprite = MountedEffects.AddSprite( Game.ActiveScene, "effects/splash3", mount );
		if ( !sprite.IsValid() ) return;
		sprite.Additive = false;
		sprite.Billboard = SpriteRenderer.BillboardMode.YOnly;
		var shaft = sprite.GameObject.AddComponent<WaterShaft>();
		shaft._sprite = sprite;
		shaft._surface = surface;
		shaft._width = width;
		shaft._height = height;
		shaft._speed = speed;
		shaft._lifetime = lifetime;
		shaft._age = 0;
		shaft.Draw( 0 );
	}

	protected override void OnUpdate()
	{
		if ( _age >= _lifetime ) DestroyGameObject();
		else Draw( _age );
	}

	void Draw( float age )
	{
		var t = age / _lifetime;
		var height = _height + _speed * age;
		_sprite.Size = new Vector2( float.Lerp( _width, 200, t ), height );
		_sprite.Color = Color.White.WithAlpha( 1 - t );
		WorldPosition = _surface + Vector3.Up * (height * 0.5f);
	}
}
