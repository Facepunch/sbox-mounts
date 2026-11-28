namespace Source1;

[Hide]
public sealed class MovementTrigger : PlayerTrigger
{
	[System.Flags]
	public enum Restrictions { NoStep = 128, Duck = 2048, Walk = 4096, NoJump = 8192 }

	[Property] public Restrictions Settings { get; set; }
	protected override bool TrackClientTouches => true;

	protected override void OccupancyChanged( GameObject actor, bool inside )
	{
		if ( !actor.IsValid() || actor.GetComponent<PlayerController>() is not { IsProxy: false } ) return;
		var movement = inside ? actor.GetOrAddComponent<MovementRestrictions>() : actor.GetComponent<MovementRestrictions>();
		movement?.SetInside( this, inside );
	}

	protected override void OnDestroy()
	{
		foreach ( var actor in Occupants ) OccupancyChanged( actor, false );
	}
}
