using System;

namespace GoldSrc.HalfLife;

public partial class Npc
{
	[Property, Sync( SyncFlags.FromHost )] public string ScriptedIdle { get; set; }

	private string _pendingScript;
	private Action _scriptDone;

	internal void StartScript( string sequence, Action done )
	{
		if ( !Networking.IsHost || Health <= 0 ) return;
		Movement?.Stop();
		InterruptAction();
		_pendingScript = sequence;
		_scriptDone = done;
	}

	private void UpdateScript()
	{
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
