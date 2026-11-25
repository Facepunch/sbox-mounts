using System;
using System.Runtime.CompilerServices;

namespace NS2;

public sealed partial class Onos
{
	TimeSince lastStep = 10;
	bool wasGrounded = true;
	static readonly ConditionalWeakTable<CameraComponent, FootstepShake> LastShake = new();

	void UpdateFootsteps()
	{
		if ( !Graphics.IsAvailable || Health <= 0 ) return;
		bool grounded = Animation.IsGrounded;
		float speed = Animation.MoveVelocity.Length;
		bool landed = grounded && !wasGrounded;
		wasGrounded = grounded;
		if ( !grounded || !landed && (speed <= 0.75f * Scale || lastStep <= 0.8f - speed / RunSpeed * 0.15f) ) return;
		if ( !landed ) lastStep = 0;
		if ( GameResource.Load<SoundEvent>( "mount://ns2/sounds/alien/onos/onos_step.sound" ) is { } sound )
			Sound.Play( sound, WorldPosition );

		var camera = Scene.Camera;
		if ( !camera.IsValid() || LastShake.TryGetValue( camera, out var previous ) && previous.Started <= 0.5f ) return;
		var player = Scene.GetAllComponents<PlayerController>().FirstOrDefault( p => p.Active && !p.IsProxy && p.UseCameraControls );
		if ( !player.IsValid() || player.GetComponent<IDamageable>() is { MaxHealth: > 0, Health: <= 0 } ) return;
		float fraction = 1 - player.WorldPosition.Distance( WorldPosition ) / (25 * Scale);
		if ( fraction <= 0 ) return;
		fraction *= fraction;
		var shake = new FootstepShake
		{
			Camera = camera,
			Amplitude = (0.002f + fraction * 0.002f).RadianToDegree() * (landed ? 3 : speed / (5 * Scale)),
			Frequency = 5 + fraction * 9,
			Duration = 0.4f - fraction * 0.2f
		};
		LastShake.Remove( camera );
		LastShake.Add( camera, shake );
		CameraEffectSystem.Get( Scene ).Add( shake );
	}

	// Spark oscillates pitch only. The camera system applies the local shake preference.
	sealed class FootstepShake : CameraEffectSystem.BaseEffect
	{
		internal TimeSince Started = 0;
		readonly float startTime = Time.Now;
		internal float Amplitude, Frequency;
		public override void Evaluate( float scale, ref Vector3 position, ref Angles angles, ref float fieldOfView )
		{
			angles.pitch += scale * Amplitude * MathF.Sin( (startTime + TimeAlive) * Frequency * MathF.Tau );
		}
	}
}
