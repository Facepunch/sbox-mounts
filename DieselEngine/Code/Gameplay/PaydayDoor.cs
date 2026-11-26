using Sandbox.Npcs;

namespace Diesel;

/// <summary>Imported moving door panel with optional linked panels and authored motion.</summary>
[Title( "PAYDAY Swing Door" ), Group( "PAYDAY" )]
public sealed class PaydayDoor : Component, Component.IPressable
{
	public const string NavigationTag = "payday_door";
	[Property] public GameObject Leaf { get; set; }
	[Property] public Vector3 Pivot { get; set; }
	[Property] public float InitialOpenAngle { get; set; }
	[Property] public float FixedOpenAngle { get; set; }
	[Property] public bool OpenBothWays { get; set; } = true;
	[Property] public bool AuthoredMotion { get; set; }
	[Property] public Transform TargetPose { get; set; } = global::Transform.Zero;
	[Property] public Transform ClosedPose { get; set; } = global::Transform.Zero;
	[Property] public bool StartsOpen { get; set; }
	[Property] public bool ResolveDoorway { get; set; }
	[Property] public float InitialAmount { get; set; } = 1;
	[Property] public List<PaydayDoor> LinkedDoors { get; set; } = [];
	[Property] public string OpenSoundPath { get; set; }
	[Property] public string CloseSoundPath { get; set; }
	[Property, Range( 0, 180 )] public float OpenAngle { get; set; } = 95;
	[Property] public float TravelTime { get; set; } = .45f;
	[Sync( SyncFlags.FromHost )] public bool Open { get; private set; }
	[Sync( SyncFlags.FromHost )] public float Amount { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool Blocked { get; private set; }
	[Sync( SyncFlags.FromHost )] float Direction { get; set; } = 1;
	/// <summary>Holding its placed ajar angle until first used.</summary>
	[Sync( SyncFlags.FromHost )] bool Resting { get; set; }
	[ConVar( "payday_door_debug", Help = "Log PAYDAY door state at start and on every use." )]
	public static bool Debug { get; set; }

	string State => $"{GameObject.Name} ({GameObject.Id}) at {WorldPosition} yaw {WorldRotation.Yaw():0.#}: open={Open} amount={Amount:0.###} "
		+ $"openAngle={OpenAngle:0.#} dir={Direction} resting={Resting} blocked={Blocked} initial={InitialOpenAngle:0.#} fixed={FixedOpenAngle:0.#} "
		+ $"bothWays={OpenBothWays} authored={AuthoredMotion} startsOpen={StartsOpen} leafYaw={(Leaf.IsValid() ? Leaf.LocalRotation.Yaw() : 0):0.#} leaf={(Leaf.IsValid() ? Leaf.Id : Guid.Empty)}";

	TimeUntil scan;
	SoundEvent openSound, closeSound;
	SoundHandle openingSound;
	bool openingSoundPlayed, closingSoundPlayed = true;
	float previousAmount;
	internal readonly record struct NetworkState( bool Open, float Amount, bool Blocked, float Direction,
		bool Resting, float OpenAngle, Transform RootPose );
	NetworkState? published;
	PaydayMapState publishedRelay;
	bool receivedState;

	internal void ApplyState( NetworkState state, bool audible )
	{
		receivedState = true;
		ResolveDoorway = false;
		Open = state.Open; Amount = state.Amount; Blocked = state.Blocked;
		Direction = state.Direction; Resting = state.Resting; OpenAngle = state.OpenAngle;
		LocalTransform = state.RootPose;
		if ( Leaf.IsValid() ) Leaf.LocalTransform = Pose( Amount );
		if ( !audible )
		{
			previousAmount = Amount;
			openingSoundPlayed = Open;
			closingSoundPlayed = !Open;
		}
	}

	void PublishState()
	{
		var relay = PaydayMapState.For( Scene );
		if ( relay is null ) return;
		var state = new NetworkState( Open, Amount, Blocked, Direction, Resting, OpenAngle, LocalTransform );
		if ( publishedRelay == relay && published == state ) return;
		relay.DoorStates[GameObject.Id] = Json.Serialize( state );
		published = state;
		publishedRelay = relay;
	}
	Model Model => Leaf.IsValid() ? Leaf.GetComponent<ModelRenderer>()?.Model : null;
	Vector3 Center => WorldTransform.PointToWorld( Model?.Bounds.Center ?? Vector3.Up * 40 );

	protected override void OnStart()
	{
		if ( receivedState )
		{
			Scene.NavMesh.ExcludedBodies.Add( NavigationTag );
			_ = LoadSounds();
			return;
		}
		// Import callbacks are batched: wall collision becomes available only after scene load.
		if ( ResolveDoorway )
		{
			PaydayDoorPlacement.Resolve( GameObject, this );
			ResolveDoorway = false;
		}
		Scene.NavMesh.ExcludedBodies.Add( NavigationTag );
		if ( !OpenBothWays && MathF.Abs( FixedOpenAngle ) > .01f )
		{
			OpenAngle = MathF.Abs( FixedOpenAngle );
			if ( Networking.IsHost ) Direction = MathF.Sign( FixedOpenAngle );
		}
		if ( MathF.Abs( InitialOpenAngle ) > .01f )
		{
			// A one-way door placed part-open (ajar) rests at that angle until used; its full swing stays FixedOpenAngle.
			bool partial = !OpenBothWays && MathF.Abs( FixedOpenAngle ) > .01f && MathF.Sign( FixedOpenAngle ) == MathF.Sign( InitialOpenAngle );
			if ( !partial ) OpenAngle = MathF.Abs( InitialOpenAngle );
			// Placed mostly open: the designer's angle (usually against a wall) becomes the full open angle.
			// Placed slightly ajar: rest there, but count as open so player use closes it.
			float placed = MathF.Abs( InitialOpenAngle );
			if ( partial && placed >= OpenAngle * 0.5f ) { OpenAngle = placed; partial = false; }
			if ( Networking.IsHost )
			{
				Direction = MathF.Sign( InitialOpenAngle );
				Amount = partial ? placed / OpenAngle : 1;
				Open = true;
				Resting = partial;
				if ( Leaf.IsValid() ) Leaf.LocalTransform = Pose( Amount );
			}
		}
		previousAmount = Amount;
		if ( AuthoredMotion && StartsOpen && Networking.IsHost )
		{
			Open = true; Amount = InitialAmount;
			if ( Leaf.IsValid() ) Leaf.LocalTransform = Pose( Amount );
			previousAmount = Amount;
		}
		openingSoundPlayed = MathF.Abs( InitialOpenAngle ) > .01f;
		closingSoundPlayed = !Open; // a door placed open can still play its closing sound
		if ( Debug ) Log.Info( $"PAYDAY door start: {State}" );
		_ = LoadSounds();
		if ( Networking.IsHost ) PublishState();
	}

	async Task LoadSounds()
	{
		openSound = await LoadSound( OpenSoundPath );
		closeSound = await LoadSound( CloseSoundPath );
	}

	static async Task<SoundEvent> LoadSound( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return null;
		try { return await ResourceLibrary.LoadAsync<SoundEvent>( path ); }
		catch ( Exception e ) { Log.Warning( $"PAYDAY door sound {path}: {e.Message}" ); return null; }
	}

	protected override void OnUpdate()
	{
		if ( !Leaf.IsValid() || Model is null ) return;
		if ( !Open ) openingSoundPlayed = false;
		if ( Networking.IsHost )
		{
			if ( (!Open || Resting) && scan <= 0 )
			{
				scan = .1f;
				foreach ( var npc in Scene.GetAllComponents<BaseNpc>() )
				{
					if ( !npc.Active || npc.Health <= 0 || npc.WorldPosition.Distance( Center ) > 160 ) continue;
					var velocity = npc.Animation?.MoveVelocity ?? Vector3.Zero;
					if ( Vector3.Dot( velocity, (Center - npc.WorldPosition).WithZ( 0 ).Normal ) <= 10 ) continue;
					if ( !PaydayPerception.ClearLine( npc, GameObject, Center ) ) continue;
					OpenAway( npc.GameObject );
					break;
				}
			}
			float target = Resting ? Amount : Open ? 1 : 0;
			float step = Time.Delta / Math.Max( .1f, TravelTime );
			float next = Amount < target ? Math.Min( target, Amount + step ) : Math.Max( target, Amount - step );
			Blocked = false;
			int samples = Math.Max( 1, (int)MathF.Ceiling( MathF.Abs( next - Amount ) * OpenAngle / 5 ) );
			if ( AuthoredMotion ) samples = Math.Max( samples, (int)MathF.Ceiling( MathF.Abs( next - Amount ) * TargetPose.Position.Length / 5 ) );
			for ( int i = 1; i <= samples && next != Amount; i++ )
				if ( Occupied( Amount + (next - Amount) * i / samples ) ) { Blocked = true; break; }
			if ( !Blocked ) Amount = next;
			else
			{
				if ( Debug ) Log.Info( $"PAYDAY door blocked: {State}" );
				if ( !Open ) SetOpen( true );
			}
		}
		Leaf.LocalTransform = Pose( Amount );
		// Observe actual movement on each peer: no spawn sound, no repeated sound while
		// held open, and no sound until an obstructed opening can start moving.
		if ( Open && Amount > previousAmount && !openingSoundPlayed )
		{
			openingSoundPlayed = true;
			closingSoundPlayed = false;
			if ( openSound is not null )
			{
				openingSound?.Stop();
				openingSound = Leaf.PlaySound( openSound );
			}
		}
		if ( !Open && Amount < previousAmount && !closingSoundPlayed )
		{
			closingSoundPlayed = true;
			if ( closeSound is not null )
			{
				openingSound?.Stop();
				openingSound = Leaf.PlaySound( closeSound );
			}
		}
		previousAmount = Amount;
		if ( Networking.IsHost ) PublishState();
	}

	protected override void OnDisabled()
	{
		openingSound?.Stop();
		base.OnDisabled();
	}

	Transform Pose( float amount )
	{
		if ( !AuthoredMotion ) return new Transform( Vector3.Zero ).RotateAround( Pivot, Rotation.FromYaw( amount * OpenAngle * Direction ) );
		var pose = new Transform( Vector3.Zero ).RotateAround( Pivot, Rotation.Slerp( ClosedPose.Rotation, TargetPose.Rotation, amount ) );
		pose.Position += Vector3.Lerp( ClosedPose.PointToWorld( Pivot ) - Pivot, TargetPose.PointToWorld( Pivot ) - Pivot, amount );
		return pose;
	}

	void SetOpen( bool value )
	{
		Open = value;
		Resting = false;
		foreach ( var door in LinkedDoors ) if ( door.IsValid() ) { door.Open = value; door.Resting = false; }
	}

	bool Occupied( float amount )
	{
		var transform = WorldTransform.ToWorld( Pose( amount ) );
		var bounds = Model.Bounds;
		bool Intersects( GameObject actor )
		{
			var point = transform.PointToLocal( actor.WorldPosition + Vector3.Up * 36 );
			return point.x >= bounds.Mins.x - 14 && point.x <= bounds.Maxs.x + 14
				&& point.y >= bounds.Mins.y - 14 && point.y <= bounds.Maxs.y + 14
				&& point.z >= bounds.Mins.z - 30 && point.z <= bounds.Maxs.z + 30;
		}
		// One-way doors can't swing away from a player, and players stand in the swing path to use them; letting players
		// block made those doors freeze or bounce back open. Only both-way doors (which open away from the user) and NPCs block.
		return (OpenBothWays && Scene.GetAllComponents<PlayerController>().Any( p => p.Active && Intersects( p.GameObject ) ))
			|| Scene.GetAllComponents<BaseNpc>().Any( n => n.Active && n.Health > 0 && Intersects( n.GameObject ) );
	}

	void OpenAway( GameObject actor )
	{
		if ( Debug ) Log.Info( $"PAYDAY door opened for {actor?.Name}: {State}" );
		if ( OpenBothWays && Amount <= .001f )
		{
			var position = WorldTransform.PointToLocal( actor.WorldPosition );
			var center = Model.Bounds.Center.WithZ( 0 );
			var openingMotion = Vector3.Cross( Vector3.Up, center - Pivot );
			Direction = Vector3.Dot( position - center, openingMotion ) > 0 ? -1 : 1;
		}
		SetOpen( true );
	}

	public void Toggle( GameObject source )
	{
		if ( PaydayMapState.For( Scene ) is { } relay ) relay.RequestDoor( GameObject.Id, source );
		else ToggleDirect( source );
	}

	[Rpc.Host]
	void ToggleDirect( GameObject source ) => ToggleHost( source );

	internal void ToggleHost( GameObject source )
	{
		if ( !Networking.IsHost ) return;
		if ( Debug ) Log.Info( $"PAYDAY door use: {State}" );
		if ( !Leaf.IsValid() || Model is null || !source.IsValid() || source.Scene != Scene ) return;
		var player = source.Root?.GetComponent<PlayerController>();
		if ( !player.IsValid() || (Rpc.Caller is not null && player.Network.Owner != Rpc.Caller) ) return;
		var center = Leaf.WorldTransform.PointToWorld( Model.Bounds.Center );
		if ( player.EyePosition.Distance( center ) > 150 ) { if ( Debug ) Log.Info( "  rejected: too far" ); return; }
		var trace = Scene.Trace.Ray( player.EyePosition, center ).IgnoreGameObjectHierarchy( player.GameObject ).WithoutTags( "trigger" ).Run();
		if ( trace.Hit && !PaydayInteraction.BelongsTo( trace.GameObject, GameObject ) )
		{
			if ( Debug ) Log.Info( $"  rejected: line of sight blocked by {trace.GameObject?.Name} ({trace.GameObject?.Id})" );
			return;
		}
		if ( Debug ) Log.Info( Open ? "  -> closing" : "  -> opening" );
		if ( Open ) SetOpen( false );
		else OpenAway( player.GameObject );
		PublishState();
	}

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e ) =>
		new IPressable.Tooltip( Blocked ? "Doorway blocked" : Open ? "Close door" : "Open door", "door_front", null );
	bool IPressable.CanPress( IPressable.Event e ) => Leaf.IsValid();
	bool IPressable.Press( IPressable.Event e ) { Toggle( e.Source.GameObject ); return true; }
}
