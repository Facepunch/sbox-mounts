using System;
using System.Runtime.CompilerServices;

namespace Source1;

/// <summary>The player's angular kick, integrated at the Source movement tick and composed by the camera.</summary>
sealed class ViewPunch : CameraEffectSystem.BaseEffect
{
	internal const float Tick = 0.015f;
	static readonly ConditionalWeakTable<CameraComponent, ViewPunch> Active = new();
	Vector3 _angle;
	Vector3 _velocity;
	float _elapsed;

	internal static Vector3 AimDirection( CameraComponent camera, Vector3 forward )
	{
		if ( !camera.IsValid() || !Active.TryGetValue( camera, out var effect ) || effect.IsDone ) return forward;
		var punch = effect._angle;
		return (Rotation.LookAt( forward ).Angles() + new Angles( punch.x, punch.y, punch.z )).Forward;
	}

	static ViewPunch ForOwner( PlayerController owner )
	{
		var camera = owner.Scene.Camera;
		if ( !camera.IsValid() ) return null;
		if ( !Active.TryGetValue( camera, out var effect ) || effect.IsDone )
		{
			if ( owner.Mode is Sandbox.Movement.SitMoveMode ) return null;
			effect = camera.Scene.GetSystem<CameraEffectSystem>().Add( new ViewPunch { Camera = camera, Duration = 0 } );
			Active.Remove( camera );
			Active.Add( camera, effect );
		}
		return effect;
	}

	public static void Apply( PlayerController owner, Angles impulse, bool reset )
	{
		var effect = ForOwner( owner );
		if ( effect is null ) return;
		if ( reset )
		{
			effect._angle = 0;
			effect._velocity = 0;
		}
		if ( owner.Mode is not Sandbox.Movement.SitMoveMode )
			effect._velocity += new Vector3( impulse.pitch, impulse.yaw, impulse.roll ) * 20;
	}

	public static void MachineGunKick( PlayerController owner, float kick )
	{
		var effect = ForOwner( owner );
		if ( effect is null ) return;
		if ( effect._angle.LengthSquared + effect._velocity.LengthSquared <= 100 )
			effect._angle = effect._velocity = 0;
		if ( owner.Mode is Sandbox.Movement.SitMoveMode ) return;

		var pitch = -(0.2f + kick);
		var yaw = pitch / 3 * (Random.Shared.Next( 3 ) == 0 ? 1 : -1);
		var roll = (0.1f + kick / 8) * (Random.Shared.Next( 3 ) == 0 ? 1 : -1);
		var limit = new Vector3( 24, 3, 1 );
		var target = (effect._angle + new Vector3( pitch, yaw, roll )).Clamp( -limit, limit );
		effect._velocity += (target - effect._angle) * 10;
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
