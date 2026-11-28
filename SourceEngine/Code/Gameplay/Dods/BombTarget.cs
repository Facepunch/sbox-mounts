namespace Source1.Dods;

public sealed class BombTarget : Component, Component.IPressable, IMapInput, IMapOutputs
{
	Doo.Block IMapInput.BuildInput( string input, string parameter, bool hasUser ) => input.ToLowerInvariant() switch
	{
		"enable" when parameter.Length == 0 => this.Call( nameof( Activate ) ),
		"disable" when parameter.Length == 0 => this.Call( nameof( Deactivate ) ),
		_ => null
	};

	void IMapOutputs.Wire( System.Func<string, bool, Doo> output )
	{
		OnPlanted = output( "OnBombPlanted", false );
		OnExploded = output( "OnBombExploded", false );
		OnDefused = output( "OnBombDefused", false );
		OnActivated = output( "OnBombTargetActivated", false );
		OnDeactivated = output( "OnBombTargetDeactivated", false );
	}

	internal const string TargetModel = "models/weapons/w_tnt_red.mdl";
	public enum States { Ready, Armed, Inactive }

	[Property, Sync( SyncFlags.FromHost ), Change( nameof( OnStateChanged ) )] public States State { get; set; }
	[Sync( SyncFlags.FromHost )] TimeUntil Detonation { get; set; }

	[Property] public Doo OnPlanted { get; set; }
	[Property] public Doo OnExploded { get; set; }
	[Property] public Doo OnDefused { get; set; }
	[Property] public Doo OnActivated { get; set; }
	[Property] public Doo OnDeactivated { get; set; }

	public void Activate()
	{
		if ( State != States.Inactive ) return;
		State = States.Ready;
		this.RunDoo( OnActivated );
	}

	public void Deactivate()
	{
		if ( State == States.Inactive ) return;
		StopUse();
		State = States.Inactive;
		this.RunDoo( OnDeactivated );
	}

	PlayerController _user;
	GameObject _planter;
	TimeUntil _complete;
	TimeSince _lastUse;
	TimeUntil _sendUse;
	internal bool CanPlant( PlayerController player ) => State == States.Ready && InRange( player, 80 );
	bool InRange( PlayerController player, float range )
	{
		if ( !player.IsValid() || !player.IsOnGround ) return false;
		var eye = player.EyeTransform;
		return WorldPosition.Distance( (player.WorldPosition + eye.Position) * 0.5f ) <= range
			&& eye.Forward.Dot( (WorldPosition - eye.Position).Normal ) >= 0.8f;
	}

	bool IPressable.CanPress( IPressable.Event e ) => State != States.Inactive;
	bool IPressable.Press( IPressable.Event e ) { Use( e.Source.GameObject ); return true; }
	bool IPressable.Pressing( IPressable.Event e )
	{
		if ( _sendUse <= 0 ) Use( e.Source.GameObject );
		return true;
	}
	void IPressable.Release( IPressable.Event e ) => ReleaseUse( e.Source.GameObject );

	internal void Use( GameObject source )
	{
		_sendUse = 0.1f;
		RequestUse( source );
	}

	[Rpc.Host]
	void RequestUse( GameObject source )
	{
		var player = source?.GetComponentInParent<PlayerController>();
		if ( !Authorized( player ) || (_user.IsValid() && player != _user) || State == States.Inactive ) return;
		if ( !InRange( player, State == States.Armed ? 96 : 80 ) ) return;
		if ( State == States.Ready && Inventory( player )?.ActiveItem is not ObjectiveBomb ) return;
		if ( !_user.IsValid() )
		{
			_user = player;
			_complete = State == States.Armed ? 3 : 2;
			if ( State == States.Ready ) PlantAnimation( player, true );
		}
		_lastUse = 0;
	}

	static BaseInventoryComponent Inventory( PlayerController player ) => player.GameObject.Root.GetComponentInChildren<BaseInventoryComponent>();
	static bool Authorized( PlayerController player ) => player.IsValid() && (Rpc.Caller == Connection.Local || player.Network.Owner == Rpc.Caller);

	[Rpc.Host]
	void ReleaseUse( GameObject source )
	{
		var player = source?.GetComponentInParent<PlayerController>();
		if ( Authorized( player ) && player == _user ) StopUse();
	}

	void StopUse()
	{
		if ( _user.IsValid() && State == States.Ready ) PlantAnimation( _user, false );
		_user = null;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlantAnimation( PlayerController player, bool planting )
	{
		if ( !player.IsValid() || Inventory( player )?.ActiveItem is not ObjectiveBomb bomb ) return;
		if ( planting ) bomb.WeaponModel?.OnAttack();
		else if ( bomb.WeaponModel is ViewModel view ) view.OnThrow( view.Idle );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( State == States.Armed && Detonation <= 0 )
		{
			Deactivate();
			this.RunDoo( OnExploded );
			Stun.Burst( Scene, WorldPosition, 500, 1 );
			new Explosion { Position = WorldPosition, Source = GameObject, Attacker = _planter, Damage = 200, Radius = 500, ForceScale = 1.5f }.Spawn();
			return;
		}
		if ( !_user.IsValid() ) return;
		var armed = State == States.Armed;
		var inventory = Inventory( _user );
		if ( State == States.Inactive || _lastUse > 0.35f || !InRange( _user, armed ? 96 : 80 ) || (!armed && inventory?.ActiveItem is not ObjectiveBomb) )
		{
			StopUse();
			return;
		}
		if ( _complete > 0 ) return;
		if ( armed )
		{
			State = States.Ready;
			this.RunDoo( OnDefused );
		}
		else
		{
			_planter = _user.GameObject;
			Detonation = 20;
			State = States.Armed;
			inventory.Remove( inventory.ActiveItem );
			this.RunDoo( OnPlanted );
		}
		_user = null;
	}

	void OnStateChanged( States before, States after )
	{
		var model = GetComponent<MountedModel>( true );
		model?.ChangeModel( after switch { States.Armed => "models/weapons/w_tnt.mdl", States.Ready => TargetModel, _ => null } );
		if ( after == States.Inactive && model?.Renderer is { } renderer ) renderer.Enabled = false;
	}
}
