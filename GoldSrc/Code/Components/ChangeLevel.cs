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
		var volumes = named.OfType<TransitionVolume>().ToArray();
		var hull = new BBox( new Vector3( -player.BodyRadius, -player.BodyRadius, 0 ),
			new Vector3( player.BodyRadius, player.BodyRadius, player.CurrentHeight ) ).Transform( player.WorldTransform );
		if ( volumes.Length > 0 && !volumes.Any( v => v.Bounds.Transform( v.WorldTransform ).Overlaps( hull ) ) ) return;

		_changing = true;
		var landmark = named.OfType<Landmark>().FirstOrDefault();
		var offset = player.WorldPosition - (landmark?.WorldPosition ?? player.WorldPosition);
		var eye = player.EyeAngles;
		var velocity = player.Body.Velocity;
		var name = LandmarkName;
		var target = ChangeTargetName;
		var delay = ChangeDelay;
		var destination = Destination;
		Fire( OnTrigger, activator );
		instance.OnMapLoaded += Arrive;
		instance.MapName = destination;

		void Arrive()
		{
			instance.OnMapLoaded -= Arrive;
			if ( instance.MapName != destination || !player.IsValid() ) return;
			var next = instance.GetComponentsInChildren<Landmark>().FirstOrDefault( x => x.TargetName == name );
			if ( !next.IsValid() ) return;
			player.WorldPosition = next.WorldPosition + offset;
			player.EyeAngles = eye;
			player.Body.Velocity = velocity;
			player.Transform.ClearInterpolation();
			next.Arrived( target, delay );
		}
	}
}
