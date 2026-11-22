using System;
using GoldSrc.Maps;

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
	private ScriptedSequence _script;
	private GameObject _scriptActivator;
	private SavedScript _resume;
	private Vector3? _navigationEntry;
	private bool _needsNavigationEntry;

	internal sealed class SavedScript
	{
		public string Idle { get; set; }
		public int Sequence { get; set; } = -1;
		public int Activator { get; set; } = -1;
		public bool PlayerActivated { get; set; }
		public int Approach { get; set; }
		public float Elapsed { get; set; }
		public int NextEvent { get; set; }
	}

	internal SavedScript CaptureScript() => _resume ?? new()
	{
		Idle = ScriptedIdle,
		Sequence = _script.IsValid() ? _script.EntityIndex : -1,
		Activator = _scriptActivator.IsValid() ? _scriptActivator.GetComponentInParent<MapEntity>()?.EntityIndex ?? -1 : -1,
		PlayerActivated = _scriptActivator.IsValid() && _scriptActivator.GetComponentInParent<PlayerController>().IsValid(),
		Approach = ScriptApproach,
		Elapsed = _action is null ? 0 : (float)ActionStart,
		NextEvent = _action is null ? 0 : _nextEvent
	};

	internal void RestoreScript( SavedScript saved, MapState map, PlayerController player )
	{
		ScriptedIdle = saved.Idle;
		if ( saved.Sequence < 0 ) return;
		var script = map.Entities.OfType<ScriptedSequence>().FirstOrDefault( x => x.IsValid() && x.EntityIndex == saved.Sequence );
		if ( !script.IsValid() ) return;
		var activator = saved.PlayerActivated ? player?.GameObject : saved.Activator < 0 ? null
			: map.Entities.FirstOrDefault( x => x.IsValid() && x.EntityIndex == saved.Activator )?.GameObject;
		StartScript( script, activator, saved );
	}

	internal void StartScript( ScriptedSequence script, GameObject activator, SavedScript resume = null )
	{
		if ( !Networking.IsHost || Health <= 0 ) return;
		Movement?.Stop();
		InterruptAction();
		_script = script;
		_scriptActivator = activator;
		_resume = resume;
		_scriptMark = script.WorldTransform;
		ScriptApproach = resume?.Approach ?? script.MoveTo;
		_navigationEntry = null;
		_needsNavigationEntry = ScriptApproach is 1 or 2 && Definition.Movement == NpcMovement.Ground && MapNavigation.IsValid();
		ScriptedIdle = null;
		if ( resume is null && ScriptApproach == 4 )
		{
			HoldMapPosition();
			WorldPosition = _scriptMark.Position;
			WorldRotation = _scriptMark.Rotation;
			Transform.ClearInterpolation();
		}
		_pendingScript = script.PlaySequence;
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
			if ( _needsNavigationEntry )
			{
				if ( !_script.Map.NavigationReady ) return;
				_navigationEntry = Movement.ProjectPoint( WorldPosition );
				_needsNavigationEntry = false;
			}
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
			if ( Find( _pendingScript ) is { } clip )
			{
				PlayAction( clip );
				if ( _resume is {} saved )
				{
					ActionStart = saved.Elapsed;
					_nextEvent = saved.NextEvent;
				}
			}
			_pendingScript = null;
		}
		_resume = null;
		if ( AdvanceAction() ) return;
		var script = _script;
		var activator = _scriptActivator;
		_script = null;
		_scriptActivator = null;
		ScriptedIdle = null;
		script.Finish( activator );
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
