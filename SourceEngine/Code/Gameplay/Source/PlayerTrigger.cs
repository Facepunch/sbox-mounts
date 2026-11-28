using Sandbox.Movement;
using Sandbox.Npcs;

namespace Source1;

[Hide]
public class PlayerTrigger : Component, Component.ITriggerListener, IMapInput
{
	Doo.Block IMapInput.BuildInput( string input, string parameter, bool hasUser ) => BuildInput( input, parameter );

	internal virtual Doo.Block BuildInput( string input, string parameter ) => input.ToLowerInvariant() switch
	{
		"enable" when parameter.Length == 0 => this.Call( nameof( TurnOn ) ),
		"disable" when parameter.Length == 0 => this.Call( nameof( TurnOff ) ),
		"toggle" when parameter.Length == 0 => this.Call( nameof( Toggle ) ),
		"touchtest" when parameter.Length == 0 => this.Call( nameof( TouchTest ) ),
		_ => null
	};

	[Property] public Doo Enter { get; set; }
	[Property] public Doo Leave { get; set; }
	[Property] public Doo First { get; set; }
	[Property] public Doo Last { get; set; }
	[Property] public Doo Activate { get; set; }
	[Property] public float Wait { get; set; }

	[Property] public Doo Touching { get; set; }
	[Property] public Doo NotTouching { get; set; }
	[Property] public ActivatorFilter Filter { get; set; }
	[Property] public Actors ActorFlags { get; set; } = Actors.Players;

	[System.Flags]
	public enum Actors
	{
		Players = 1, Npcs = 2, Pushables = 4, Physics = 8, AlliesOnly = 16,
		SeatedPlayers = 32, All = 64, UnseatedPlayers = 512, Debris = 1024, SeatedNpcs = 2048
	}

	readonly Dictionary<(Collider, Collider), GameObject> _contacts = [];
	readonly Dictionary<GameObject, int> _actors = [];
	protected Dictionary<GameObject, int>.KeyCollection Occupants => _actors.Keys;
	protected virtual bool TrackClientTouches => false;
	[Property, Sync( SyncFlags.FromHost ), Change( nameof( OnDisabledChanged ) )] public bool Disabled { get; private set; }
	TimeUntil _next;
	bool _spent;

	public void TurnOn() => SetListening( true );
	public void TurnOff() => SetListening( false );
	public void Toggle() => SetListening( Disabled );

	void SetListening( bool enabled )
	{
		if ( enabled && _spent || Disabled == !enabled ) return;
		Disabled = !enabled;
	}

	protected virtual void OnDisabledChanged( bool previous, bool disabled )
	{
		foreach ( var collider in GetComponentsInChildren<Collider>( true ) ) collider.Enabled = !disabled;
		if ( !disabled ) return;
		_contacts.Clear();
		foreach ( var actor in _actors.Keys.ToArray() ) EndTouch( actor );
	}

	public void TouchTest()
	{
		if ( !Disabled ) Fire( _actors.Count > 0 ? Touching : NotTouching, GameObject );
	}

	GameObject ResolveActor( Collider other )
	{
		bool Allows( Actors mask ) => (ActorFlags & mask) != 0;
		if ( other.GetComponentInParent<PlayerController>() is { } player )
		{
			if ( !Allows( Actors.Players | Actors.All | Actors.SeatedPlayers | Actors.UnseatedPlayers )
				|| Npcs.HealthOf( player.GameObject ) is { Health: <= 0 } ) return null;
			if ( Allows( Actors.SeatedPlayers | Actors.UnseatedPlayers ) )
			{
				var seated = player.GetComponentInParent<ISitTarget>() is not null;
				if ( Allows( seated ? Actors.UnseatedPlayers : Actors.SeatedPlayers ) ) return null;
			}
			return player.GameObject;
		}

		if ( other.GetComponentInParent<BaseNpc>() is { } npc )
		{
			if ( !Allows( Actors.Npcs | Actors.All | Actors.AlliesOnly | Actors.SeatedNpcs ) ) return null;
			if ( Allows( Actors.SeatedNpcs ) && npc.GetComponentInParent<ISitTarget>() is null ) return null;
			if ( Allows( Actors.AlliesOnly ) && !Scene.GetAllComponents<PlayerController>().Any( p => npc.GetDisposition( p.GameObject ) == Disposition.Friendly ) ) return null;
			return npc.GameObject;
		}

		var body = other.GetComponentInParent<Rigidbody>();
		var actor = body?.GameObject ?? other.GameObject;
		if ( actor.Tags.Has( "debris" ) || actor.Tags.Has( "map_debris" ) )
			return Allows( Actors.Debris ) ? actor : null;
		return Allows( Actors.All ) || (body.IsValid() && Allows( Actors.Physics )) ? actor : null;
	}

	void ITriggerListener.OnTriggerEnter( Collider self, Collider other )
	{
		if ( (!Networking.IsHost && !TrackClientTouches) || Disabled || ResolveActor( other ) is not { } actor || (Filter.IsValid() && !Filter.Accepts( actor )) || !_contacts.TryAdd( (self, other), actor ) ) return;
		_actors.TryGetValue( actor, out var count );
		_actors[actor] = count + 1;
		if ( count != 0 ) return;
		OccupancyChanged( actor, true );
		if ( !Networking.IsHost ) return;
		Fire( Enter, actor );
		if ( _actors.Count == 1 ) Fire( First, actor );
		if ( _actors.ContainsKey( actor ) ) ActivateActor( actor );
	}

	void ITriggerListener.OnTriggerExit( Collider self, Collider other )
	{
		if ( !_contacts.Remove( (self, other), out var actor ) ) return;
		if ( --_actors[actor] == 0 ) EndTouch( actor );
	}

	void EndTouch( GameObject actor )
	{
		if ( !_actors.Remove( actor ) ) return;
		OccupancyChanged( actor, false );
		if ( !Networking.IsHost ) return;
		ActorLeft( actor );
		Fire( Leave, actor );
		if ( _actors.Count == 0 ) Fire( Last, actor );
	}

	protected override void OnFixedUpdate()
	{
		if ( _next > 0 ) return;
		if ( _spent )
		{
			TurnOff();
			Enabled = false;
		}
		else if ( Activate is not null && _actors.Count > 0 ) ActivateActor( _actors.Keys.First() );
	}

	protected virtual void ActivateActor( GameObject player )
	{
		if ( _spent || _next > 0 ) return;
		_next = Wait == 0 ? 0.2f : Wait;
		_spent = Wait < 0;
		if ( _spent ) _next = 0.1f;
		Fire( Activate, player );
	}

	protected virtual void OccupancyChanged( GameObject actor, bool inside ) { }

	protected virtual void ActorLeft( GameObject actor ) { }

	protected void Fire( Doo output, GameObject player ) => RunDoo( output, c => c.SetArgument( "user", player ) );
}
