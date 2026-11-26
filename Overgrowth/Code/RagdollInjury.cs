using System;

namespace Overgrowth;

internal sealed class RagdollInjury : Component
{
	ModelPhysics physics;
	RagdollDamping damping;
	readonly RagdollMotorPose injuryMotor = new();
	RagdollFallPose injuryPose;
	float injuryAge, writheTime;
	internal float Age => injuryAge;
	float initialStun;
	float injuryMouth, mouthRemainder;
	[Sync( SyncFlags.Interpolate )] internal float MouthOpening { get; private set; }
	RabbitGuard fighter;
	float displayedMouth;
	readonly CharacterVoice voice = new();
	static readonly float[] ItemVoiceTimes = [0.2f, 2f, 5f];
	int nextVoice;
	bool voiced;
	TimeUntil? deathVoice;

	internal void AdvanceInjuryAge( float seconds ) => injuryAge += seconds;
	internal void Speak( string cue ) => CharacterVoice.Select( GetComponent<SkinnedModelRenderer>(), cue, PlayVoice );
	internal void QueueDeathVoice() => deathVoice = 0.4f;

	protected override void OnStart()
	{
		fighter = GetComponentInParent<RabbitGuard>();
		if ( fighter.IsValid() ) fighter.ActiveInjury = this;
	}

	protected override void OnUpdate()
	{
		if ( fighter.IsValid() || !voiced && MouthOpening == displayedMouth ) return;
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
			voiced = voice.Playing;
		}
		renderer.Morphs.Set( "mouth_open", MouthOpening, 0f );
		if ( voiced ) voice.ApplyMouth( renderer, Time.Delta );
		displayedMouth = MouthOpening;
	}

	internal void StartInjury( ModelPhysics model, RagdollFallPose pose, float age, bool itemInjury, RagdollInjury previous = null, float stun = 0f )
	{
		Enabled = true;
		physics = model;
		damping = GetComponent<RagdollDamping>( true );
		damping.Refresh();
		damping.Enabled = false;
		SetAutoSleep( false );
		injuryPose = pose;
		injuryAge = age;
		writheTime = previous?.writheTime ?? 0f;
		MouthOpening = previous?.MouthOpening ?? 0f;
		injuryMouth = previous?.injuryMouth ?? 0f;
		mouthRemainder = previous?.mouthRemainder ?? 0f;
		nextVoice = itemInjury ? 0 : ItemVoiceTimes.Length;
		initialStun = writheTime + (itemInjury ? 0.9f : stun);
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( deathVoice is { } delay && delay <= 0f )
		{
			deathVoice = null;
			Speak( "death" );
		}
		if ( injuryPose is null || !physics.IsValid() ) return;
		if ( CharacterLoader.RagdollVelocity( physics ) is not { } velocity ) return;
		writheTime += Time.Delta;
		injuryAge += Time.Delta;
		while ( nextVoice < ItemVoiceTimes.Length && writheTime >= ItemVoiceTimes[nextVoice] && injuryAge < 12f )
		{
			Speak( "death" );
			nextVoice++;
		}
		float speed = velocity.Length / 40f;
		float strength = Math.Clamp( Math.Clamp( 2f - speed * 0.3f, 0.2f, 1f ) * (12f - injuryAge) * 0.1f, 0f, 0.9f );
		strength = Math.Max( 0f, strength - (fighter.IsValid() ? fighter.RagdollStun : Math.Max( 0f, initialStun - writheTime )) );
		UpdateInjuredMouth( Time.Now, Time.Delta, strength );
		if ( strength > 0f )
		{
			float phase = (writheTime / injuryPose.Writhe.Clip.Duration) % 1f;
			injuryMotor.Sample( physics.Model, injuryPose, phase, 0f, 0f, injured: true );
		}
		physics.GetComponent<RagdollHands>()?.Advance( injuryPose.Writhe.Clip, writheTime, Time.Delta, strength );
		injuryMotor.Apply( physics, strength );
		if ( injuryAge <= 12f ) return;
		Enabled = false;
	}

	// SetActiveRagdollInjuredPose blends once per character update, independently of the physics rate.
	void UpdateInjuredMouth( float time, float delta, float strength )
	{
		const float step = 4f / 120f;
		mouthRemainder += delta;
		while ( mouthRemainder >= step )
		{
			mouthRemainder -= step;
			float sampleTime = time - mouthRemainder;
			float target = (MathF.Sin( sampleTime * 4f ) + MathF.Sin( sampleTime * 6.3f )) * 0.5f;
			injuryMouth += (target - injuryMouth) * strength;
		}
		MouthOpening = Math.Max( 0f, injuryMouth );
	}

	protected override void OnDisabled()
	{
		voice.Stop();
		if ( !Networking.IsHost || injuryPose is null ) return;
		injuryPose = null;
		MouthOpening = 0f;
		if ( voiced || injuryAge > 12f ) StopVoice();
		if ( physics.IsValid() )
		{
			injuryMotor.Apply( physics, 0f );
			SetAutoSleep( true );
		}
		if ( damping.IsValid() ) damping.Enabled = true;
	}

	void SetAutoSleep( bool enabled )
	{
		foreach ( var part in physics.Bodies )
			if ( part.Component.IsValid() && part.Component.PhysicsBody is { IsValid: true } body )
				body.AutoSleep = enabled;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayVoice( string path, float volume, float pitch, float distance )
	{
		var renderer = GetComponent<SkinnedModelRenderer>();
		if ( !renderer.IsValid() ) return;
		voiced = true;
		if ( fighter.IsValid() )
		{
			fighter.PlayLocalVoice( path, volume, pitch, distance );
			return;
		}
		voice.Play( renderer, CharacterLoader.AverageChainPosition( renderer, "head" ) ?? renderer.WorldPosition, path, volume, pitch, distance );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void StopVoice()
	{
		if ( fighter.IsValid() ) fighter.StopLocalVoice();
		else
		{
			voice.Stop();
			UpdateMouth();
		}
		voiced = false;
	}
}
