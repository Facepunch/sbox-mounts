using GoldSrc.HalfLife;

namespace GoldSrc.Maps;

public sealed class ScriptedSentence : MapEntity
{
	[Property] public string Speaker { get; set; }
	[Property] public string Listener { get; set; }
	[Property] public string Sentence { get; set; }
	[Property] public float Radius { get; set; }
	[Property] public float Duration { get; set; }
	[Property] public float Repeat { get; set; }
	[Property] public float Volume { get; set; }
	[Property] public float Attenuation { get; set; }
	[Property] public int SpawnFlags { get; set; }

	bool _searching;
	TimeUntil _next;

	protected override void OnStart()
	{
		_searching = string.IsNullOrEmpty( TargetName );
		if ( _searching ) _next = 1;
	}

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || !_searching && _next > 0 ) return;
		_searching = true;
		_next = 0;
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !CanTrigger || _next > 0 || !_searching ) return;
		var npc = FindSpeaker();
		if ( !npc.IsValid() )
		{
			_next = Repeat + 0.5f;
			return;
		}

		var listener = FindListener( npc.WorldPosition );
		npc.SpeakMapSentence( Sentence, Duration, listener, Volume, Attenuation, (SpawnFlags & 8) != 0 );
		_searching = string.IsNullOrEmpty( TargetName );
		_next = Duration + Repeat + (_searching ? 0.1f : 0);
		bool once = (SpawnFlags & 1) != 0;
		Fire( OnTrigger, detached: once );
		if ( once && IsValid ) Kill();
	}

	private Npc FindSpeaker()
	{
		if ( string.IsNullOrEmpty( Speaker ) ) return null;
		return Map.FindAll( Speaker ).OfType<Monster>().Select( actor => actor.Npc ).FirstOrDefault( Accepts )
			?? Scene.GetAllComponents<Npc>().FirstOrDefault( npc => npc.ClassName == Speaker &&
				npc.WorldPosition.DistanceSquared( WorldPosition ) <= Radius * Radius && Accepts( npc ) );
	}

	private bool Accepts( Npc npc ) => npc.IsValid() && npc.Health > 0
		&& ((SpawnFlags & 2) == 0 || npc is Personnel { FollowingPlayer: true })
		&& ((SpawnFlags & 4) != 0 || npc is not Personnel person || person.CanStartConversation);

	private GameObject FindListener( Vector3 origin )
	{
		if ( string.IsNullOrEmpty( Listener ) ) return null;
		if ( Map.Find( Listener ) is {} named ) return named.GameObject;
		if ( Listener == "player" ) return Scene.GetAllComponents<PlayerController>()
			.Where( p => p.WorldPosition.DistanceSquared( origin ) < 4096f * 4096f ).MinBy( p => p.WorldPosition.DistanceSquared( origin ) )?.GameObject;
		return Scene.GetAllComponents<Npc>().Where( npc => npc.ClassName == Listener && npc.WorldPosition.DistanceSquared( origin ) < Radius * Radius )
			.MinBy( npc => npc.WorldPosition.DistanceSquared( origin ) )?.GameObject;
	}
}
