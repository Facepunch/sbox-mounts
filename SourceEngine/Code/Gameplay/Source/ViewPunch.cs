using System.Runtime.CompilerServices;

namespace Source1;

/// <summary>The player's angular kick, integrated at the Source movement tick and composed by the camera.</summary>
sealed class ViewPunch : CameraEffectSystem.BaseEffect
{
	const float Tick = 0.015f;
	static readonly ConditionalWeakTable<CameraComponent, ViewPunch> Active = new();
	Vector3 _angle;
	Vector3 _velocity;
	float _elapsed;

	public static void Apply( CameraComponent camera, Angles impulse, bool reset )
	{
		if ( !camera.IsValid() ) return;
		if ( !Active.TryGetValue( camera, out var effect ) || effect.IsDone )
		{
			effect = camera.Scene.GetSystem<CameraEffectSystem>().Add( new ViewPunch { Camera = camera, Duration = 0 } );
			Active.Remove( camera );
			Active.Add( camera, effect );
		}
		if ( reset )
		{
			effect._angle = 0;
			effect._velocity = 0;
		}
		effect._velocity += new Vector3( impulse.pitch, impulse.yaw, impulse.roll ) * 20;
	}

	public override void Update( float delta )
	{
		_elapsed += delta;
		while ( _elapsed >= Tick )
		{
			_elapsed -= Tick;
			if ( _angle.LengthSquared <= 0.001f && _velocity.LengthSquared <= 0.001f )
			{
				_angle = 0;
				_velocity = 0;
				Stop();
				break;
			}

			_angle += _velocity * Tick;
			_velocity = _velocity * (1 - 9 * Tick) - _angle * (65 * Tick);
			_angle = _angle.Clamp( new Vector3( -89, -179, -89 ), new Vector3( 89, 179, 89 ) );
		}
	}

	public override void Evaluate( float scale, ref Vector3 position, ref Angles angles, ref float fieldOfView )
		=> angles += new Angles( _angle.x, _angle.y, _angle.z ) * scale;
}
