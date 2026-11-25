using System;
using System.Runtime.CompilerServices;

namespace NS2;

public sealed partial class Onos
{
	TimeSince lastStep = 10;
	bool wasGrounded = true;
	TimeUntil nextDirt;
	static readonly ConditionalWeakTable<CameraComponent, FootstepShake> LastShake = new();

	void UpdateFootsteps()
	{
		if ( !Graphics.IsAvailable || Health <= 0 ) return;
		bool grounded = Animation.IsGrounded;
		float speed = Animation.MoveVelocity.Length;
		bool landed = grounded && !wasGrounded;
		wasGrounded = grounded;
		if ( landed || (speed > RunSpeed * 0.5f && nextDirt <= 0 && Random.Shared.NextSingle() < 0.9f) )
			TryShakeDirtLoose();
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

	void TryShakeDirtLoose()
	{
		var map = Scene.Get<MapLocations>();
		if ( map?.LightsNear( WorldPosition ) is not { Length: > 0 } lights ) return;
		for ( int i = 0; i < 11; i++ )
		{
			var light = lights[Random.Shared.Next( lights.Length )];
			if ( WorldPosition.DistanceSquared( light ) >= 12 * Scale * (12 * Scale) ) continue;
			var ceiling = Scene.Trace.Box( Vector3.One * (0.4f * Scale), light, light + Vector3.Up * (0.1f * Scale) )
				.IgnoreGameObjectHierarchy( GameObject ).Run();
			if ( !ceiling.Hit || !ceiling.HasTag( "world" ) ) continue;
			PlayCinematic( "dirt_fall", light, Rotation.Identity );
			nextDirt = 0.1f;
			return;
		}
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
