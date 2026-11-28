using System;

namespace Source1.Hl2;

/// <summary>
/// What every machine shows and hears of the antlion guard: its feet thud on the clips' footstep events, it pants while it moves and rumbles while it stands, its voice events
/// play growls and roars over those, and it bleeds once badly hurt.
/// </summary>
public sealed partial class AntlionGuard
{
	/// <summary>Footsteps for indoor maps, which use the guard's inside step sounds.</summary>
	[Property] public bool InsideFootsteps { get; set; }

	const float LightBleed = 0.5f;

	/// <summary>At or below this share of its health it limps, bleeds heavily, and heavy throws no longer make it flinch.</summary>
	const float BadlyHurt = 0.25f;

	SoundLoop _growlHigh, _growlIdle, _breath, _confused;

	bool _moving;
	TimeUntil _nextSwell;
	string _bleeding;
	GameObject _bleed;
	static float Random( float min, float max ) => Game.Random.Float( min, max );

	/// <summary>Its voices start silent, and play on unheard once started.</summary>
	protected override void OnAwake()
	{
		SoundLoop Voice( string name ) => new( GameObject, name, waitUntilHeard: true );
		_growlHigh = Voice( "NPC_AntlionGuard.GrowlHigh" );
		_growlIdle = Voice( "NPC_AntlionGuard.GrowlIdle" );
		_breath = Voice( "NPC_AntlionGuard.BreathSound" );
		_confused = Voice( "NPC_AntlionGuard.Confused" );
	}

	protected override void OnUpdate()
	{
		if ( !Npc.IsValid() )
			return;
		Npc.SetPose( "throw", ThrowYaw );
		if ( Application.IsDedicatedServer )
			return;
		Breathe();
		Bleed();
	}

	protected override void OnDestroy()
	{
		foreach ( var loop in new[] { _growlHigh, _growlIdle, _breath, _confused } )
			loop?.Stop();
		if ( _prop.IsValid() )
			Claimed.Remove( _prop );
	}

	/// <summary>
	/// Moving, it pants up to 0.6 over 2 to 4 seconds and its low growl dies away. Stopped, the panting fades over 2 to 8
	/// seconds and the low growl swells now and then. A voice event silences both until the next of these.
	/// </summary>
	void Breathe()
	{
		var moving = Npc.Speed > 10;
		if ( moving != _moving )
		{
			_moving = moving;
			if ( moving )
			{
				Pant( 140, 160 );
				_growlIdle.FadeVolume( (0, 1) );
				_growlIdle.FadePitch( (Random( 90, 110 ), 0.2f) );
			}
			else
			{
				var time = Random( 2, 8 );
				_breath.FadeVolume( (0, time) );
				_breath.FadePitch( (Random( 40, 60 ), time) );
				_growlIdle.FadeVolume( (0, time) );
				_growlIdle.FadePitch( (Random( 120, 140 ), time) );
				_nextSwell = time * 0.25f;
			}
		}

		if ( !_moving && _nextSwell )
		{
			_nextSwell = Random( 1, 8 );
			var swell = Random( 0.5f, 1 );
			_growlIdle.FadeVolume( (Random( 0.2f, 0.3f ), swell), (0, swell) );
			_growlIdle.FadePitch( (Random( 80, 120 ), swell) );
		}

		_breath.Update();
		_growlIdle.Update();
		_growlHigh.Update();
		_confused.Update();
	}

	/// <summary>The clips' footsteps and voice cues, on every machine.</summary>
	public override void OnAnimationEvent( SourceModelSequenceData.Event e )
	{
		if ( Application.IsDedicatedServer || e.Name is not { } name )
			return;

		switch ( name )
		{
			case "AE_ANTLIONGUARD_FOOTSTEP_LIGHT":
				PlayLocalSound( InsideFootsteps ? "NPC_AntlionGuard.Inside.StepLight" : "NPC_AntlionGuard.StepLight", follow: false );
				break;
			case "AE_ANTLIONGUARD_FOOTSTEP_HEAVY":
				PlayLocalSound( InsideFootsteps ? "NPC_AntlionGuard.Inside.StepHeavy" : "NPC_AntlionGuard.StepHeavy", follow: false );
				break;
			case "AE_ANTLIONGUARD_VOICE_GROWL":
				// Mostly a fast growl, sometimes a frustrated roar
				Silence( Game.Random.Int( 0, 10 ) < 6 ? FastGrowl() : FrustratedRoar() );
				PlayLocalSound( "NPC_AntlionGuard.Anger", follow: false );
				break;
			case "AE_ANTLIONGUARD_VOICE_BARK":
				Silence( MathF.Max( Bark(), _confused.FadeVolume( (1, Random( 0.1f, 0.2f )), (Random( 0.2f, 0.3f ), Random( 0.1f, 0.2f )), (0, Random( 0.4f, 0.6f )) ) ) );
				break;
			case "AE_ANTLIONGUARD_VOICE_ROAR":
				Silence( FastGrowl() );
				PlayLocalSound( "NPC_AntlionGuard.Roar", follow: false );
				break;
			case "AE_ANTLIONGUARD_VOICE_PAIN":
				Silence( MathF.Max( _confused.FadeVolume( (1, Random( 0.1f, 0.2f )), (1, Random( 0.5f, 0.8f )), (Random( 0.1f, 0.2f ), Random( 0.1f, 0.2f )), (0, Random( 0.5f, 0.75f )) ),
					_growlHigh.FadeVolume( (1, Random( 0.1f, 0.2f )), (Random( 0.2f, 0.3f ), Random( 0.1f, 0.2f )), (0, Random( 0.4f, 0.6f )) ) ) );
				break;
			case "AE_ANTLIONGUARD_VOICE_SQUEEZE":
				Silence( _growlHigh.FadeVolume( (1, Random( 0.1f, 0.2f )), (0, Random( 1, 1.5f )) ) );
				Pant( 60, 80 );
				PlayLocalSound( "NPC_AntlionGuard.Anger", follow: false );
				break;
			case "AE_ANTLIONGUARD_VOICE_GRUNT":
				Silence( MathF.Max( _confused.FadeVolume( (Random( 0.6f, 1 ), Random( 0.1f, 0.2f )), (0, Random( 0.1f, 0.2f )) ),
					_growlHigh.FadeVolume( (Random( 0.2f, 0.4f ), Random( 0.1f, 0.2f )), (0, Random( 0.4f, 0.6f )) ) ) );
				break;
			case "AE_ANTLIONGUARD_VOICE_SCRATCH":
				Silence( 0 );
				Pant( 60, 80 );
				PlayLocalSound( "NPC_AntlionGuard.Anger", follow: false );
				break;
		}
	}

	float FastGrowl() => _growlHigh.FadeVolume( (1, Random( 0.2f, 0.4f )), (0.1f, Random( 0.8f, 1 )), (0, Random( 0.4f, 0.8f )) );

	float FrustratedRoar()
	{
		PlayLocalSound( "NPC_AntlionGuard.FrustratedRoar", follow: false );
		return _growlHigh.FadeVolume( (0, 0.5f) );
	}

	float Bark() => _growlHigh.FadeVolume( (1, Random( 0.1f, 0.2f )), (0, Random( 0.4f, 0.6f )) );

	/// <summary>A voice event: the panting and the low growl drop to nothing over 0.1 s, and the next swell waits the event out.</summary>
	void Silence( float length )
	{
		_breath.FadeVolume( (0, 0.1f) );
		_growlIdle.FadeVolume( (0, 0.1f) );
		_nextSwell = MathF.Max( _nextSwell.Relative, 0 ) + length;
	}

	/// <summary>A hard breath, up to 0.6 over 2 to 4 seconds, at a pitch between the two.</summary>
	void Pant( float lowPitch, float highPitch )
	{
		var time = Random( 2, 4 );
		_breath.FadeVolume( (0.6f, time) );
		_breath.FadePitch( (Random( lowPitch, highPitch ), time) );
	}

	/// <summary>Below half health it bleeds lightly, at a quarter heavily.</summary>
	void Bleed()
	{
		var share = Npc.MaxHealth > 0 ? Npc.Health / Npc.MaxHealth : 1;
		var effect = Npc.Health < 1 ? null : share <= BadlyHurt ? "blood_antlionguard_injured_heavy" : share < LightBleed ? "blood_antlionguard_injured_light" : null;
		if ( effect == _bleeding )
			return;
		_bleeding = effect;
		if ( _bleed.IsValid() )
			_bleed.Destroy();
		_bleed = effect is null ? null : MountedEffects.Attach( effect, GameObject, MountContent.Of( GameObject ), new Transform( Vector3.Up * 65 ) );
	}
}
