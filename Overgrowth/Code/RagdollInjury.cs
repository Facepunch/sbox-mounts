using System;

namespace Overgrowth;

internal sealed class RagdollInjury : Component
{
	ModelPhysics physics;
	RagdollDamping damping;
	readonly RagdollMotorPose injuryMotor = new();
	RagdollFallPose injuryPose;
	float injuryAge, writheTime;
	float injuryMouth;
	[Sync( SyncFlags.Interpolate )] float MouthOpening { get; set; }
	float displayedMouth;
	readonly CharacterVoice voice = new();
	static readonly float[] ItemVoiceTimes = [0.2f, 2f, 5f];
	int nextVoice;
	bool voiced;

	protected override void OnUpdate()
	{
		if ( !voiced && MouthOpening == displayedMouth ) return;
		UpdateMouth();
	}

	void UpdateMouth()
	{
		if ( GetComponent<SkinnedModelRenderer>() is not { } renderer ) return;
		if ( voiced )
		{
			renderer.Morphs.Set( "ah", 0f, 0f );
			renderer.Morphs.Set( "oh", 0f, 0f );
			renderer.Morphs.Set( "w", 0f, 0f );
		}
		renderer.Morphs.Set( "mouth_open", MouthOpening, 0f );
		voice.ApplyMouth( renderer, Time.Delta );
		displayedMouth = MouthOpening;
	}

	internal void StartInjury( ModelPhysics model, RagdollFallPose pose, float age, bool itemInjury )
	{
		physics = model;
		damping = GetComponent<RagdollDamping>();
		damping.Enabled = false;
		injuryPose = pose;
		injuryAge = age;
		writheTime = 0f;
		injuryMouth = 0f;
		nextVoice = itemInjury ? 0 : ItemVoiceTimes.Length;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || injuryPose is null || !physics.IsValid() ) return;
		Vector3 momentum = default;
		float mass = 0f;
		foreach ( var part in physics.Bodies )
		{
			if ( !part.Component.IsValid() ) continue;
			momentum += part.Component.Velocity * part.Component.Mass;
			mass += part.Component.Mass;
		}
		if ( mass <= 0f ) return;
		writheTime += Time.Delta;
		injuryAge += Time.Delta;
		while ( nextVoice < ItemVoiceTimes.Length && writheTime >= ItemVoiceTimes[nextVoice] && injuryAge < 12f )
		{
			CharacterVoice.Select( physics.Renderer, "death", PlayVoice );
			nextVoice++;
		}
		float speed = momentum.Length / (mass * 40f);
		float strength = Math.Clamp( Math.Clamp( 2f - speed * 0.3f, 0.2f, 1f ) * (12f - injuryAge) * 0.1f, 0f, 0.9f );
		float open = MathF.Sin( Time.Now * 4f ) * 0.5f + MathF.Sin( Time.Now * 6.3f ) * 0.5f;
		injuryMouth += (open - injuryMouth) * (1f - MathF.Pow( 1f - strength, Time.Delta * 120f ));
		MouthOpening = Math.Max( 0f, injuryMouth );
		if ( strength > 0f )
		{
			float phase = (writheTime / injuryPose.Writhe.Clip.Duration) % 1f;
			injuryMotor.Sample( physics.Model, injuryPose, phase, 0f, 0f, injured: true );
			foreach ( var part in physics.Bodies )
				if ( part.Component.IsValid() && part.Component.Sleeping ) part.Component.Sleeping = false;
		}
		injuryMotor.Apply( physics, strength );
		if ( injuryAge < 12f ) return;
		if ( voiced ) StopVoice();
		Enabled = false;
	}

	protected override void OnDisabled()
	{
		voice.Stop();
		if ( !Networking.IsHost || injuryPose is null ) return;
		injuryPose = null;
		if ( physics.IsValid() ) injuryMotor.Apply( physics, 0f );
		if ( damping.IsValid() ) damping.Enabled = true;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayVoice( string path, float volume, float pitch, float distance )
	{
		var renderer = GetComponent<SkinnedModelRenderer>();
		if ( !renderer.IsValid() ) return;
		var rig = GetComponent<CharacterRemains>()?.Rig;
		if ( rig is null && Sandbox.Mounting.Directory.Get( "overgrowth" )?.GetByPath( renderer.Model.ResourcePath ) is CharacterLoader character )
			rig = character.GetGuardAnimations();
		voiced = true;
		voice.Play( renderer, CharacterVoice.HeadPosition( renderer, rig ) ?? renderer.WorldPosition, path, volume, pitch, distance );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void StopVoice()
	{
		voice.Stop();
		UpdateMouth();
		voiced = false;
	}
}
