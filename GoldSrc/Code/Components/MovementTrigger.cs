namespace GoldSrc.Maps;

[Title( "GoldSrc Movement Trigger" )]
[Icon( "move_up" )]
public sealed class MovementTrigger : MapEntity, Component.ITriggerListener
{
	public enum TriggerKind
	{
		Teleport,
		Gravity
	}

	[Property] public TriggerKind Kind { get; set; }
	[Property] public bool NoClients { get; set; }
	[Property] public bool AllowMonsters { get; set; }
	[Property] public float Gravity { get; set; } = 1f;

	private Collider _collider;
	private bool _missingDestination;
	private readonly List<Collider> _touching = [];

	protected override void OnStart()
	{
		_collider = GetComponent<Collider>();
	}

	void ITriggerListener.OnTriggerEnter( Collider other ) => Touch( other );

	protected override void OnFixedUpdate()
	{
		if ( !CanTrigger || !_collider.IsValid() || !_collider.Enabled ) return;
		_touching.Clear();
		_touching.AddRange( _collider.Touching );
		foreach ( var other in _touching )
			Touch( other );
	}

	private void Touch( Collider other )
	{
		if ( !CanTrigger || !other.IsValid() ) return;
		var player = other.GetComponentInParent<PlayerController>();
		if ( Kind == TriggerKind.Gravity )
		{
			if ( player.IsValid() && player.Body.IsValid() )
				player.Body.GravityScale = Gravity == 0f ? 1f : Gravity;
			return;
		}

		var monster = other.GetComponentInParent<Monster>();
		if ( player.IsValid() ? NoClients : !AllowMonsters || !monster.IsValid() ) return;
		var subject = player.IsValid() ? player.GameObject : monster.GameObject;
		if ( !IsMasterTriggered( subject ) ) return;
		var destination = Map.Find( Target );
		if ( !destination.IsValid() )
		{
			if ( !_missingDestination )
				Log.Warning( $"GoldSrc: teleport destination '{Target}' was not found." );
			_missingDestination = true;
			return;
		}
		_missingDestination = false;
		var body = player.IsValid() ? player.Body : subject.GetComponent<Rigidbody>();
		if ( player.IsValid() )
		{
			player.PreventGrounding( 0.1f );
			player.EyeAngles = destination.WorldRotation.Angles();
		}
		subject.WorldPosition = destination.WorldPosition + Vector3.Up;
		subject.WorldRotation = destination.WorldRotation;
		if ( body.IsValid() ) body.Velocity = Vector3.Zero;
		subject.Transform.ClearInterpolation();
	}
}
