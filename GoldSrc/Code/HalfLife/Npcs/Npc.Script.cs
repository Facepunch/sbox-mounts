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
	[Sync( SyncFlags.FromHost )] private int ScriptApproach { get; set; }
	private StudioSequence ScriptGait => ScriptApproach switch { 1 => _walk, 2 => _run, _ => null };
	private string _pendingScript;
	private Action _scriptDone;
	private Vector3? _navigationEntry;

	internal void StartScript( string sequence, Transform mark, int moveTo, Action done )
	{
		if ( !Networking.IsHost || Health <= 0 ) return;
		Movement?.Stop();
		InterruptAction();
		_scriptMark = mark;
		ScriptApproach = moveTo;
		_navigationEntry = null;
		if ( moveTo is 1 or 2 && Definition.Movement == NpcMovement.Ground && MapNavigation.IsValid() )
			_navigationEntry = Movement?.ProjectPoint( WorldPosition );
		ScriptedIdle = null;
		if ( moveTo == 4 )
		{
			HoldMapPosition();
			WorldPosition = mark.Position;
			WorldRotation = mark.Rotation;
			Transform.ClearInterpolation();
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
		if ( ScriptGait is {} gait && Movement.IsValid() )
		{
			var destination = Movement.MoveTarget;
			if ( !destination.HasValue )
			{
				if ( WalkOntoMesh( gait ) ) return;
				MoveToward( _scriptMark.Position, 8, gait.GroundSpeed > 0 ? gait.GroundSpeed / Definition.Speed : 1 );
				return;
			}
			if ( WorldPosition.Distance( destination.Value ) > 8 ) return;
			Movement.Stop();
			WorldRotation = _scriptMark.Rotation;
		}
		ScriptApproach = 0;

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

	// Actors can start on a scripted mark just outside the eroded edge of the navmesh.
	// Walk the clear entry segment at the clip's speed before handing position to the agent.
	private bool WalkOntoMesh( StudioSequence gait )
	{
		if ( _navigationEntry is not {} entry ) return false;
		var delta = (entry - WorldPosition).WithZ( 0 );
		float step = (gait.GroundSpeed > 0 ? gait.GroundSpeed : Definition.Speed) * Time.Delta;
		if ( delta.Length > 1 && step > 0 )
		{
			var next = WorldPosition + delta.Normal * Math.Min( step, delta.Length );
			var radius = Definition.Radius;
			var hull = new BBox( new Vector3( -radius, -radius, 1 ), new Vector3( radius, radius, Definition.Height ) );
			if ( !Scene.Trace.Box( hull, WorldPosition, next ).WithTag( "world" ).Run().Hit )
			{
				WorldRotation = Rotation.Slerp( WorldRotation, Rotation.LookAt( delta ), Movement.TurnSpeed * Time.Delta );
				WorldPosition = next;
				return true;
			}
		}
		_navigationEntry = null;
		return false;
	}
}
