using System;

namespace GoldSrc.HalfLife;

public partial class Npc
{
	[Property, Sync( SyncFlags.FromHost )] public string ScriptedIdle { get; set; }

	// A map mark can be on furniture or outside the walkable mesh. Only a movement
	// command should hand the actor's transform to the navigation simulation.
	[Property, Hide] public NavMeshAgent MapNavigation { get; set; }
	internal void HoldMapPosition()
	{
		MapNavigation = GetComponent<NavMeshAgent>( true );
		if ( MapNavigation.IsValid() ) MapNavigation.UpdatePosition = false;
	}

	private Transform _scriptMark;
	private int _approach;
	private string _pendingScript;
	private Action _scriptDone;

	internal void StartScript( string sequence, Transform mark, int moveTo, Action done )
	{
		if ( !Networking.IsHost || Health <= 0 ) return;
		Movement?.Stop();
		InterruptAction();
		_scriptMark = mark;
		_approach = moveTo;
		ScriptedIdle = null;
		if ( moveTo == 4 )
		{
			HoldMapPosition();
			WorldPosition = mark.Position;
			WorldRotation = mark.Rotation;
		}
		_pendingScript = sequence;
		_scriptDone = done;
	}

	internal virtual void SpeakMapSentence( string sentence, float duration, GameObject listener, float volume, float attenuation, bool silenceFriends )
	{
		if ( !string.IsNullOrEmpty( sentence ) )
			_audio?.Emit( $"!{sentence.TrimStart( '!' )}", WorldPosition, volume, VoicePitch, this as VoicedNpc, attenuation );
	}

	private void UpdateScript()
	{
		if ( _approach is 1 or 2 && Movement.IsValid() )
		{
			var destination = Movement.MoveTarget;
			if ( !destination.HasValue )
			{
				MoveToward( _scriptMark.Position, 8, _approach == 1 ? 0.5f : 1f );
				return;
			}
			if ( WorldPosition.Distance( destination.Value ) > 8 ) return;
			Movement.Stop();
			WorldRotation = _scriptMark.Rotation;
			_approach = 0;
		}

		if ( _pendingScript is not null )
		{
			if ( Find( _pendingScript ) is { } clip ) PlayAction( clip );
			_pendingScript = null;
		}
		if ( AdvanceAction() ) return;
		var done = _scriptDone;
		_scriptDone = null;
		ScriptedIdle = null;
		done();
	}
}
