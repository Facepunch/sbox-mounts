namespace Source1;

[Hide]
public sealed class ChangeLevel : PlayerTrigger
{
	[Property] public string Destination { get; set; }
	[Property] public GameObject Landmark { get; set; }
	[Property] public List<BBox> Volumes { get; set; } = [];
	[Property] public bool TouchEnabled { get; set; }
	[Property] public Doo OnChangeLevel { get; set; }

	bool _changing;

	protected override void ActivateActor( GameObject actor )
	{
		if ( TouchEnabled && !actor.Tags.Has( "noclip" ) ) Change( actor );
	}

	internal override Doo.Block BuildInput( string input, string parameter ) =>
		input.Equals( "ChangeLevel", System.StringComparison.OrdinalIgnoreCase ) ? this.Call( nameof( Change ), MapInputs.User( true ) ) : base.BuildInput( input, parameter );

	public void Change( GameObject activator )
	{
		if ( _changing || !Networking.IsHost || string.IsNullOrEmpty( Destination ) || !Landmark.IsValid() ) return;
		var player = activator.IsValid() ? activator.GetComponentInParent<PlayerController>() : null;
		player ??= Scene.GetAllComponents<PlayerController>().FirstOrDefault();
		if ( !player.IsValid() || Npcs.HealthOf( player.GameObject ) is { Health: <= 0 } ) return;
		var hull = new BBox( new Vector3( -player.BodyRadius, -player.BodyRadius, 0 ), new Vector3( player.BodyRadius, player.BodyRadius, player.CurrentHeight ) );
		var bounds = hull.Transform( Landmark.Parent.WorldTransform.ToLocal( player.WorldTransform ) );
		if ( Volumes.Count > 0 && !Volumes.Any( volume => volume.Overlaps( bounds ) ) ) return;
		_changing = true;
		Fire( OnChangeLevel, activator );
		LaunchArguments.Map = Destination;
		Game.Load( Game.Ident, keepClients: true );
	}
}
