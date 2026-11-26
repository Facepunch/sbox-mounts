using System;

namespace Source1;

/// <summary>The short camera-facing glow shared by Source explosions and RPG shootdowns.</summary>
sealed class ExplosionOverlay : Component
{
	internal const float Lifetime = 0.1f;
	static readonly float ViewCone = MathF.Cos( 40f.DegreeToRadian() );
	SpriteRenderer _sprite;
	Vector3 _origin;
	float _width;
	TimeSince _age;

	internal static void Show( Scene scene, Vector3 origin, string mount, float width )
	{
		if ( Application.IsDedicatedServer ) return;
		var sprite = MountedEffects.AddSprite( scene, "sun/overlay", mount );
		if ( !sprite.IsValid() ) return;
		sprite.Billboard = SpriteRenderer.BillboardMode.None;
		var glow = sprite.GameObject.AddComponent<ExplosionOverlay>();
		glow._sprite = sprite;
		glow._origin = origin;
		glow._width = width;
		glow._age = 0;
	}

	protected override void OnUpdate()
	{
		float age = _age;
		if ( age >= Lifetime )
		{
			GameObject.Destroy();
			return;
		}

		var camera = Scene.Camera;
		if ( !camera.IsValid() ) return;
		var eye = camera.WorldPosition;
		var direction = (_origin - eye).Normal;
		var alignment = Vector3.Dot( direction, camera.WorldRotation.Forward ).Remap( ViewCone, 1 );
		_sprite.Enabled = alignment > 0 && !Scene.Trace.Ray( eye, _origin )
			.WithoutTags( "trigger", "projectile", "playercontroller" ).IgnoreGameObjectHierarchy( camera.GameObject ).Run().Hit;
		if ( !_sprite.Enabled ) return;

		WorldPosition = eye + direction * 100;
		WorldRotation = Rotation.LookAt( -direction );
		_sprite.Size = (new Vector2( _width, _width * 0.5f ) + 16 * age) * (2 * float.Lerp( 150, 70, alignment ));
		_sprite.Color = (new Color( 1, 0.9f, 0.7f ) * (alignment * (1 - age / Lifetime))).WithAlpha( 1 );
	}
}
