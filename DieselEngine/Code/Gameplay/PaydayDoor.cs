using Sandbox.Npcs;

namespace Diesel;

/// <summary>Single-leaf hospital door. The parent stays at the authored hinge.</summary>
[Title( "PAYDAY Swing Door" ), Group( "PAYDAY" )]
public sealed class PaydayDoor : Component, Component.IPressable
{
	public const string NavigationTag = "payday_door";
	[Property] public GameObject Leaf { get; set; }
	[Property, Range( 0, 180 )] public float OpenAngle { get; set; } = 95;
	[Property] public float TravelTime { get; set; } = .45f;
	[Sync( SyncFlags.FromHost )] public bool Open { get; private set; }
	[Sync( SyncFlags.FromHost )] public float Amount { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool Blocked { get; private set; }
	[Sync( SyncFlags.FromHost )] float Direction { get; set; } = 1;
	TimeUntil scan;
	Model Model => Leaf.IsValid() ? Leaf.GetComponent<ModelRenderer>()?.Model : null;
	Vector3 Center => WorldTransform.PointToWorld( Model?.Bounds.Center ?? Vector3.Up * 40 );

	protected override void OnStart()
	{
		Scene.NavMesh.ExcludedBodies.Add( NavigationTag );
	}

	protected override void OnUpdate()
	{
		if ( !Leaf.IsValid() || Model is null ) return;
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
			for ( int i = 1; i <= samples && next != Amount; i++ )
				if ( Occupied( Amount + (next - Amount) * i / samples ) ) { Blocked = true; break; }
			if ( !Blocked ) Amount = next;
			else if ( !Open ) Open = true;
		}
		Leaf.LocalTransform = Pose( Amount );
	}

	Transform Pose( float amount ) => new( Vector3.Zero, Rotation.FromYaw( amount * OpenAngle * Direction ) );

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
		if ( Amount <= .001f )
		{
			var position = WorldTransform.PointToLocal( actor.WorldPosition );
			var center = Model.Bounds.Center.WithZ( 0 );
			var openingMotion = Vector3.Cross( Vector3.Up, center );
			Direction = Vector3.Dot( position - center, openingMotion ) > 0 ? -1 : 1;
		}
		Open = true;
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
		if ( Open ) Open = false;
		else OpenAway( player.GameObject );
	}

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e ) =>
		new IPressable.Tooltip( Blocked ? "Doorway blocked" : Open ? "Close door" : "Open door", "door_front", null );
	bool IPressable.CanPress( IPressable.Event e ) => Leaf.IsValid();
	bool IPressable.Press( IPressable.Event e ) { Toggle( e.Source.GameObject ); return true; }
}
