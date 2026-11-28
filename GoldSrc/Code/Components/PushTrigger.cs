namespace GoldSrc.Maps;

[Title( "GoldSrc Push Trigger" )]
public sealed class PushTrigger : MapEntity, IScenePhysicsEvents
{
	[Property] public Collider Volume { get; set; }
	[Property] public Vector3 PushVelocity { get; set; }
	[Property] public bool Once { get; set; }
	[Property] public bool PlayersOnly { get; set; }
	[Property] public bool ScaleByTickRate { get; set; }

	readonly HashSet<Rigidbody> _pushed = [];

	public override void Trigger( GameObject activator = null )
	{
		if ( Volume.IsValid() ) Volume.Enabled = !Volume.Enabled;
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	void IScenePhysicsEvents.PrePhysicsStep()
	{
		if ( !CanTrigger || !Volume.IsValid() || !Volume.Enabled ) return;
		foreach ( var other in Volume.Touching )
		{
			if ( other.IsTrigger ) continue;
			var body = other.GetComponentInParent<Rigidbody>();
			if ( !body.IsValid() || !body.MotionEnabled || body.IsProxy ) continue;
			var player = body.GetComponent<PlayerController>();
			if ( PlayersOnly && !player.IsValid() ) continue;
			if ( Once )
			{
				body.Velocity += PushVelocity;
				if ( player.IsValid() && body.Velocity.z > 0 ) player.PreventGrounding( Time.Delta );
				Kill();
				return;
			}

			if ( !_pushed.Add( body ) ) continue;
			var velocity = ScaleByTickRate ? PushVelocity / Time.Delta : PushVelocity;
			body.Velocity += velocity.WithZ( velocity.z * Time.Delta );
		}
	}

	void IScenePhysicsEvents.PostPhysicsStep()
	{
		if ( _pushed.Count == 0 ) return;
		var horizontal = PushVelocity.WithZ( 0 );
		if ( ScaleByTickRate ) horizontal /= Time.Delta;
		foreach ( var body in _pushed )
			if ( body.IsValid() ) body.Velocity -= horizontal;
		_pushed.Clear();
	}
}
