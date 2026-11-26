// Run on the joined client in a disposable multiplayer test session.
if ( !Networking.IsClient ) throw new Exception( "Run on the remote client." );
var scene = Game.ActiveScene;
var relay = scene.GetAllComponents<Diesel.PaydayMapState>().Single();
var player = scene.GetAllComponents<PlayerController>().Single( p => p.Network.Owner == Connection.Local );
var originalPosition = player.WorldPosition;
try
{
	foreach ( var door in scene.GetAllComponents<Diesel.PaydayDoor>() )
	{
		if ( !door.Leaf.IsValid() || door.Leaf.GetComponent<ModelRenderer>()?.Model is not { } model ) continue;
		var center = door.Leaf.WorldTransform.PointToWorld( model.Bounds.Center );
		foreach ( var offset in new[] { Vector3.Forward * 45, Vector3.Backward * 45, Vector3.Left * 45, Vector3.Right * 45 } )
		{
			player.WorldPosition = center + offset - (player.EyePosition - player.WorldPosition);
			await Task.Delay( 750, cancellationToken );
			if ( player.EyePosition.Distance( center ) > 150 ) continue;
			var trace = scene.Trace.Ray( player.EyePosition, center ).IgnoreGameObjectHierarchy( player.GameObject ).WithoutTags( "trigger" ).Run();
			if ( trace.Hit && trace.GameObject != door.GameObject && !trace.GameObject.IsDescendant( door.GameObject ) ) continue;
			bool before = door.Open;
			relay.RequestDoor( door.GameObject.Id, player.GameObject );
			await Task.Delay( 1500, cancellationToken );
			if ( door.Open == before ) continue;
			// A real remote request naming the host's player must be rejected.
			var other = scene.GetAllComponents<PlayerController>().First( p => p.Network.Owner == Connection.Host );
			var accepted = door.Open;
			relay.RequestDoor( door.GameObject.Id, other.GameObject );
			await Task.Delay( 1000, cancellationToken );
			if ( door.Open != accepted ) throw new Exception( "Foreign-owner request changed the door." );
			return new { Door = door.GameObject.Id, Before = before, After = door.Open, RemoteToggle = true, ForeignOwnerRejected = true };
		}
	}
	throw new Exception( "No remotely usable door completed a replicated toggle." );
}
finally { player.WorldPosition = originalPosition; }
