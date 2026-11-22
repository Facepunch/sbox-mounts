using System.Runtime.CompilerServices;
using Sandbox.Npcs;

namespace GoldSrc.HalfLife;

public abstract partial class Personnel
{
	internal sealed class Conversation
	{
		public TimeUntil NextLine;
	}

	static readonly ConditionalWeakTable<Scene, Conversation> Conversations = new();
	internal static Conversation GetConversation( Scene scene ) => Conversations.GetValue( scene, _ => new() );

	protected bool HasSpoken { get; private set; }
	bool _wounded, _badlyWounded, _patientHurt, _patientWounded, _patientCritical;
	int _idleLines;
	TimeUntil _nextIdleLine, _answerAt, _talkUntil, _nextSmellRemark;
	Personnel _speaker;
	GameObject _listener;

	protected float SpeechPitch => VoicePitch + Game.Random.Int( 0, 3 ) / 100f;
	protected abstract int PainVariants { get; }
	float IdleSentenceDuration => PreDisaster ? Game.Random.Float( 4.8f, 5.2f ) : Game.Random.Float( 2.8f, 3.2f );
	protected virtual string PreDisasterGreeting => null;
	protected virtual string PreDisasterIdle => null;
	TimeUntil _nextPain;

	internal override void PainSound()
	{
		if ( _nextPain > 0 ) return;
		_nextPain = Game.Random.Float( 0.5f, 0.75f );
		EmitSound( $"{Sounds.Pain}{Game.Random.Int( 1, PainVariants )}.wav", WorldPosition, VoiceVolume, SpeechPitch, this );
	}

	protected void EmitSentence( string line, float attenuation = GoldSrcSoundEvents.Idle )
	{
		EmitSound( $"!{SentencePrefix}_{line}", WorldPosition, VoiceVolume, SpeechPitch, this, attenuation );
	}

	protected void PauseConversation( float seconds ) => GetConversation( Scene ).NextLine = seconds;

	internal void Say( string line, GameObject listener = null, float duration = 3, float attenuation = GoldSrcSoundEvents.Idle )
	{
		_speaker = null;
		EmitSentence( line, attenuation );
		HasSpoken = true;
		_listener = listener;
		_talkUntil = duration;
		PauseConversation( duration + 2 );
		if ( listener.IsValid() ) Animation.SetLookTarget( listener );
	}

	internal bool FollowingPlayer => Leader.IsValid();

	internal override void SpeakMapSentence( string sentence, float duration, GameObject listener, float volume, float attenuation, bool silenceFriends )
	{
		if ( silenceFriends )
			foreach ( var friend in Scene.GetAllComponents<Personnel>() )
				if ( friend != this && friend.Health > 0 && CanSeeColleague( friend, WorldPosition ) ) friend.StopVoice();

		base.SpeakMapSentence( sentence, duration, listener, volume, attenuation, silenceFriends );
		HasSpoken = true;
		_speaker = null;
		_listener = listener;
		_talkUntil = duration;
		PauseConversation( duration + 2 );
		if ( listener.IsValid() ) Animation.SetLookTarget( listener );
	}

	internal void Ask( Personnel friend, string line, float duration )
	{
		Say( line, friend.GameObject, duration );
		friend.ListenToQuestion( this, duration );
	}

	internal void ListenToQuestion( Personnel speaker, float duration )
	{
		StopWatching();
		_speaker = speaker;
		_answerAt = AnswerDelay( duration );
	}

	internal virtual float AnswerDelay( float questionDuration ) => questionDuration;
	protected void CancelAnswer() => _speaker = null;

	internal bool AnswerQuestion()
	{
		if ( !_speaker.IsValid() || _answerAt > 0 ) return false;
		var speaker = _speaker;
		_speaker = null;
		if ( speaker.Health <= 0 ) return false;
		Say( "ANSWER", speaker.GameObject, Game.Random.Float( 2.8f, 3.2f ) );
		return true;
	}

	internal bool CanStartConversation => GetConversation( Scene ).NextLine <= 0 && !PhysicsCarrier.IsValid();
	internal override void IdleSound() { }

	protected override void Think()
	{
		base.Think();
		if ( Action is not null || PhysicsCarrier.IsValid() || Health <= 0 )
		{
			CancelAnswer();
			StopWatching();
			return;
		}
		if ( _speaker.IsValid() )
		{
			if ( _speaker.Health <= 0 || FindVisibleEnemy().IsValid() || FindVisibleThreat().IsValid() )
				_speaker = null;
			else
			{
				if ( !AnswerQuestion() ) Animation.SetLookTarget( _speaker.GameObject );
				return;
			}
		}
		if ( _previousTurnsBody.HasValue && WatchPlayer() ) return;
		if ( _nextIdleLine > 0 )
		{
			if ( _talkUntil > 0 && _listener.IsValid() ) Animation.SetLookTarget( _listener );
			return;
		}
		_nextIdleLine = Game.Random.Float( 0.4f, 0.7f );
		if ( FindVisibleEnemy().IsValid() || FindVisibleThreat().IsValid() ) return;
		if ( _talkUntil > 0 ) return;
		var player = Senses?.FindNearestVisible( "player" );
		if ( !player.IsValid() || player.WorldPosition.DistanceSquared( WorldPosition ) > 500 * 500 ) return;
		if ( !CanStartConversation )
		{
			TryWatchPlayer( player );
			return;
		}
		if ( !HasSpoken )
		{
			if ( !InViewCone( player.WorldPosition - WorldPosition ) ) return;
			var greeting = PreDisaster ? PreDisasterGreeting : "HELLO";
			if ( greeting is not null ) Say( greeting, player, Game.Random.Float( 3, 3.5f ) );
			else HasSpoken = true;
			return;
		}
		if ( !Leader.IsValid() && _nextSmellRemark <= 0 && Senses.Disturbance is { Kind: "meat" or "carcass" or "garbage" } )
		{
			Say( "SMELL", duration: Game.Random.Float( 2.8f, 3.2f ) );
			_nextSmellRemark = 60;
			return;
		}
		if ( FirstTime( ref _wounded, Health <= MaxHealth * 0.75f ) ) { Say( "WOUND" ); return; }
		if ( FirstTime( ref _badlyWounded, Health <= MaxHealth * 0.5f ) ) { Say( "MORTAL" ); return; }
		if ( Leader.IsValid() && Leader.GetComponent<Component.IDamageable>() is { Health: > 0, MaxHealth: > 0 } patient )
		{
			var fraction = patient.Health / patient.MaxHealth;
			if ( FirstTime( ref _patientCritical, fraction <= 0.125f ) ) { Say( "CUREC", Leader ); return; }
			if ( FirstTime( ref _patientWounded, fraction <= 0.25f ) ) { Say( "CUREB", Leader ); return; }
			if ( FirstTime( ref _patientHurt, fraction <= 0.5f ) ) { Say( "CUREA", Leader ); return; }
		}
		_nextIdleLine = Game.Random.Float( 3, 7 );
		if ( Game.Random.Int( 0, _idleLines * 2 ) != 0 )
		{
			TryWatchPlayer( player );
			return;
		}
		var friend = NearestColleague();
		if ( friend.IsValid() && friend.Movement?.IsMoving != true && Game.Random.Int( 0, 99 ) < 75 )
		{
			Ask( friend, PreDisaster ? "PQUEST" : "QUESTION", IdleSentenceDuration );
			_idleLines++;
		}
		else if ( Game.Random.Int( 0, 1 ) == 1 )
		{
			var line = PreDisaster ? PreDisasterIdle : "IDLE";
			if ( line is not null ) Say( line, player, IdleSentenceDuration );
			_idleLines++;
		}
	}

	static bool FirstTime( ref bool said, bool condition )
	{
		if ( said || !condition ) return false;
		said = true;
		return true;
	}
}
