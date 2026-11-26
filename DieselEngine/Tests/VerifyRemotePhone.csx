// Run on the joined client in a disposable multiplayer test session.
if ( !Networking.IsClient ) throw new Exception( "Run on the remote client." );
var scene = Game.ActiveScene;
var player = scene.GetAllComponents<PlayerController>().Single( p => p.Network.Owner == Connection.Local );
var originalPosition = player.WorldPosition;
try
{
	foreach ( var phone in scene.GetAllComponents<Diesel.PaydayAlarm>().Where( p => !p.Disabled ) )
	{
		foreach ( var offset in new[] { Vector3.Forward * 45, Vector3.Backward * 45, Vector3.Left * 45, Vector3.Right * 45 } )
		{
			player.WorldPosition = phone.WorldPosition + Vector3.Up * 24 + offset - (player.EyePosition - player.WorldPosition);
			await Task.Delay( 750, cancellationToken );
			phone.DisableAlarm( player.GameObject );
			await Task.Delay( 1000, cancellationToken );
			if ( phone.Disabled ) return new { Phone = phone.GameObject.Id, RemoteDisable = true };
		}
	}
	throw new Exception( "No remote phone press completed." );
}
finally { player.WorldPosition = originalPosition; }
