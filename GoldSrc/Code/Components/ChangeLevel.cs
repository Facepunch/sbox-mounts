namespace GoldSrc.Maps;

public sealed class TransitionVolume : MapEntity
{
	[Property] public BBox Bounds { get; set; }
}

public sealed class Landmark : MapEntity
{
	internal void Arrived( string target, float delay )
	{
		if ( !string.IsNullOrEmpty( target ) ) Fire( Map.BuildOutput( target, delay: delay ), GameObject );
	}
}

public sealed class ChangeLevel : MapEntity, Component.ITriggerListener
{
	[Property] public string Destination { get; set; }
	[Property] public string LandmarkName { get; set; }
	[Property] public string ChangeTargetName { get; set; }
	[Property] public float ChangeDelay { get; set; }
	private bool _changing;

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( other.GetComponentInParent<PlayerController>() is {} player ) Trigger( player.GameObject );
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	public override void Trigger( GameObject activator = null )
	{
		if ( !Networking.IsHost || _changing || !CanTrigger || string.IsNullOrEmpty( Destination ) ) return;
		var instance = Map.GetComponentInParent<MapInstance>();
		if ( !instance.IsValid() || !instance.IsLoaded ) return;
		var player = activator.IsValid() ? activator.GetComponentInParent<PlayerController>() : null;
		player ??= Scene.GetAllComponents<PlayerController>().FirstOrDefault( p => !p.IsProxy );
		if ( !player.IsValid() ) return;

		var named = Map.FindAll( LandmarkName ).ToArray();
		var volumes = named.OfType<TransitionVolume>().Select( v => v.Bounds.Transform( v.WorldTransform ) ).ToArray();
		bool InTransition( BBox bounds ) => volumes.Length == 0 || volumes.Any( v => v.Overlaps( bounds ) );
		var hull = new BBox( new Vector3( -player.BodyRadius, -player.BodyRadius, 0 ),
			new Vector3( player.BodyRadius, player.BodyRadius, player.CurrentHeight ) ).Transform( player.WorldTransform );
		if ( !InTransition( hull ) ) return;

		_changing = true;
		var landmark = named.OfType<Landmark>().FirstOrDefault();
		// Global tracktrains continue on the overlapping track in the next BSP. Their
		// configured starting node is only for entering that map directly.
		var trains = !landmark.IsValid() ? [] : Map.Entities.OfType<TrackTrain>()
			.Where( t => t.IsValid() && !t.NoRotation && !string.IsNullOrEmpty( t.GlobalName ) && InTransition( t.GameObject.GetBounds() ) )
			.Select( t => (t.GlobalName, Offset: t.WorldPosition - landmark.WorldPosition, t.WorldRotation, t.Speed) ).ToArray();
		var loader = Sandbox.Mounting.MountUtility.TryParse( instance.MapName, out var mount )
			? Sandbox.Mounting.Directory.Get( mount )?.GetByPath( instance.MapName ) as MapLoader : null;
		var visibility = landmark.IsValid() ? loader?.VisibilityAt( landmark.EntityIndex ) : null;
		var actors = !landmark.IsValid() ? [] : Map.Entities.OfType<Monster>()
			.Where( a => a.IsValid() && a.Npc.IsValid() && a.Npc.Health > 0 )
			.Where( a =>
			{
				var bounds = a.GameObject.GetBounds();
				return InTransition( bounds ) && (visibility?.Contains( bounds.Transform( Map.WorldTransform.ToLocal( global::Transform.Zero ) ) ) ?? true);
			} )
			.Select( a => (Actor: a, Offset: a.WorldPosition - landmark.WorldPosition) ).ToArray();
		var offset = player.WorldPosition - (landmark?.WorldPosition ?? player.WorldPosition);
		var eye = player.EyeAngles;
		var velocity = player.Body.Velocity;
		var name = LandmarkName;
		var target = ChangeTargetName;
		var delay = ChangeDelay;
		var destination = Destination;
		Fire( OnTrigger, activator );
		foreach ( var (actor, _) in actors )
			actor.GameObject.SetParent( instance.GameObject.Parent, true );
		instance.OnMapLoaded += Arrive;
		instance.MapName = destination;

		void Arrive()
		{
			instance.OnMapLoaded -= Arrive;
			var next = instance.GetComponentsInChildren<Landmark>().FirstOrDefault( x => x.TargetName == name );
			if ( instance.MapName != destination || !next.IsValid() )
			{
				foreach ( var (actor, _) in actors )
					if ( actor.IsValid() ) actor.GameObject.Destroy();
				return;
			}
			foreach ( var (actor, relative) in actors )
			{
				if ( !actor.IsValid() ) continue;
				// The world's map-owned root handles teardown. The checkpoint owns the
				// actor snapshot because the destination BSP cannot recreate it.
				actor.GameObject.SetParent( next.Map.GameObject, true );
				actor.GameObject.BreakFromPrefab();
				actor.GameObject.Flags &= ~GameObjectFlags.NotSaved;
				actor.FromPreviousMap = true;
				actor.WorldPosition = next.WorldPosition + relative;
				actor.Transform.ClearInterpolation();
				actor.EntityIndex = -1;
				actor.Map = next.Map;
				next.Map.Entities.Add( actor );
			}
			foreach ( var train in trains )
			{
				var replacement = next.Map.Entities.OfType<TrackTrain>().FirstOrDefault( t => t.GlobalName == train.GlobalName && !t.NoRotation );
				replacement?.ResumeTrack( next.WorldPosition + train.Offset, train.WorldRotation, train.Speed );
			}
			if ( !player.IsValid() ) return;
			player.WorldPosition = next.WorldPosition + offset;
			player.EyeAngles = eye;
			player.Body.Velocity = velocity;
			player.Transform.ClearInterpolation();
			next.Arrived( target, delay );
		}
	}
}
