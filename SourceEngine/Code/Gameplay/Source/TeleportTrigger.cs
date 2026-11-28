namespace Source1;

[Hide]
public sealed class TeleportTrigger : PlayerTrigger
{
	[Property] public string Destination { get; set; }
	[Property] public string Landmark { get; set; }
	[Property] public bool PreserveAngles { get; set; }
	[Property] public bool Relative { get; set; }
	[Property] public Vector3 LocalCenter { get; set; }
	[Property] public Vector3 Offset { get; set; }

	protected override void ActivateActor( GameObject actor )
	{
		Vector3 position;
		var rotation = actor.WorldRotation;
		var turn = false;
		if ( Relative )
		{
			position = WorldTransform.PointToWorld( LocalCenter ) + Offset;
		}
		else
		{
			var destination = Find( Destination, actor );
			if ( !destination.IsValid() ) return;
			position = destination.WorldPosition;
			var landmark = Find( Landmark, actor );
			if ( landmark.IsValid() ) position += actor.WorldPosition - landmark.WorldPosition;
			else if ( !PreserveAngles )
			{
				rotation = destination.WorldRotation;
				turn = true;
			}
		}

		if ( actor.IsProxy && actor.Network.Owner is { } owner )
		{
			using var recipient = Rpc.FilterInclude( owner );
			TeleportOwner( actor, position, rotation, turn, Relative );
		}
		else Move( actor, position, rotation, turn, Relative );
	}

	GameObject Find( string name, GameObject actor ) => name switch
	{
		null or "" => null,
		"!activator" or "!caller" => actor,
		_ => Scene.Directory.FindByName( name ).FirstOrDefault()
	};

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void TeleportOwner( GameObject actor, Vector3 position, Rotation rotation, bool turn, bool stop )
	{
		if ( actor.IsValid() && !actor.IsProxy ) Move( actor, position, rotation, turn, stop );
	}

	static void Move( GameObject actor, Vector3 position, Rotation rotation, bool turn, bool stop )
	{
		if ( actor.GetComponent<PlayerController>() is { } player )
		{
			player.PreventGrounding( Time.Delta );
			if ( turn ) player.EyeAngles = rotation.Angles();
		}
		if ( turn ) actor.WorldRotation = rotation;
		actor.WorldPosition = position;
		if ( stop && actor.GetComponent<Rigidbody>() is { } body ) body.Velocity = Vector3.Zero;
		actor.Transform.ClearInterpolation();
	}
}
