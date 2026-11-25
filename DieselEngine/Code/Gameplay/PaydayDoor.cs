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
	TimeUntil scan;
	SoundEvent openSound, closeSound;
	SoundHandle openingSound;
	bool openingSoundPlayed, closingSoundPlayed = true;
	float previousAmount;
	Model Model => Leaf.IsValid() ? Leaf.GetComponent<ModelRenderer>()?.Model : null;
	Vector3 Center => WorldTransform.PointToWorld( Model?.Bounds.Center ?? Vector3.Up * 40 );

	protected override void OnStart()
	{
		Scene.NavMesh.ExcludedBodies.Add( NavigationTag );
		if ( !OpenBothWays && MathF.Abs( FixedOpenAngle ) > .01f )
		{
			OpenAngle = MathF.Abs( FixedOpenAngle );
			if ( Networking.IsHost ) Direction = MathF.Sign( FixedOpenAngle );
		}
		if ( MathF.Abs( InitialOpenAngle ) > .01f )
		{
			OpenAngle = MathF.Abs( InitialOpenAngle );
			if ( Networking.IsHost )
			{
				Direction = MathF.Sign( InitialOpenAngle );
				Open = true;
				Amount = 1;
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
		_ = LoadSounds();
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
			if ( !Open && scan <= 0 )
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
			float target = Open ? 1 : 0;
			float step = Time.Delta / Math.Max( .1f, TravelTime );
			float next = Amount < target ? Math.Min( target, Amount + step ) : Math.Max( target, Amount - step );
			Blocked = false;
			int samples = Math.Max( 1, (int)MathF.Ceiling( MathF.Abs( next - Amount ) * OpenAngle / 5 ) );
			if ( AuthoredMotion ) samples = Math.Max( samples, (int)MathF.Ceiling( MathF.Abs( next - Amount ) * TargetPose.Position.Length / 5 ) );
			for ( int i = 1; i <= samples && next != Amount; i++ )
				if ( Occupied( Amount + (next - Amount) * i / samples ) ) { Blocked = true; break; }
			if ( !Blocked ) Amount = next;
			else if ( !Open ) SetOpen( true );
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
		foreach ( var door in LinkedDoors ) if ( door.IsValid() ) door.Open = value;
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
		return Scene.GetAllComponents<PlayerController>().Any( p => p.Active && Intersects( p.GameObject ) )
			|| Scene.GetAllComponents<BaseNpc>().Any( n => n.Active && n.Health > 0 && Intersects( n.GameObject ) );
	}

	void OpenAway( GameObject actor )
	{
		if ( OpenBothWays && Amount <= .001f )
		{
			var position = WorldTransform.PointToLocal( actor.WorldPosition );
			var center = Model.Bounds.Center.WithZ( 0 );
			var openingMotion = Vector3.Cross( Vector3.Up, center - Pivot );
			Direction = Vector3.Dot( position - center, openingMotion ) > 0 ? -1 : 1;
		}
		SetOpen( true );
	}

	[Rpc.Host]
	public void Toggle( GameObject source )
	{
		if ( !Leaf.IsValid() || Model is null || !source.IsValid() || source.Scene != Scene ) return;
		var player = source.Root?.GetComponent<PlayerController>();
		if ( !player.IsValid() || (Rpc.Caller is not null && player.Network.Owner != Rpc.Caller) ) return;
		var center = Leaf.WorldTransform.PointToWorld( Model.Bounds.Center );
		if ( player.EyePosition.Distance( center ) > 150 ) return;
		var trace = Scene.Trace.Ray( player.EyePosition, center ).IgnoreGameObjectHierarchy( player.GameObject ).WithoutTags( "trigger" ).Run();
		if ( trace.Hit && !PaydayInteraction.BelongsTo( trace.GameObject, GameObject ) ) return;
		if ( Open ) SetOpen( false );
		else
		{
			OpenAway( player.GameObject );
			// Missions listen for "open" (elevator/fence doors) and "open_out" (apartment doors); which way PDTH
			// names a given swing isn't known, so all opening names are reported.
			foreach ( var sequence in new[] { "open", "open_out", "open_in" } )
				PaydayMission.SequenceRan( GameObject, sequence, player.GameObject );
		}
	}

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e ) =>
		new IPressable.Tooltip( Blocked ? "Doorway blocked" : Open ? "Close door" : "Open door", "door_front", null );
	bool IPressable.CanPress( IPressable.Event e ) => Leaf.IsValid();
	bool IPressable.Press( IPressable.Event e ) { Toggle( e.Source.GameObject ); return true; }
}
