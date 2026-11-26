// Read-only status on either process during packaged multiplayer verification.
var scene = Game.ActiveScene;
if ( scene is null ) return new { Ready = false };
var relay = scene.GetAllComponents<Diesel.PaydayMapState>().FirstOrDefault();
return new
{
	Scene = scene.Name, Networking.IsActive, Networking.IsHost, Networking.IsClient,
	Local = Connection.Local?.Id, Host = Connection.Host?.Id,
	Connections = Connection.All.Select( c => new { c.Id, c.IsActive, c.IsHost } ).ToArray(),
	Players = scene.GetAllComponents<PlayerController>().Select( p => new
		{ p.GameObject.Id, Owner = p.Network.Owner?.Id, Position = p.WorldPosition.ToString() } ).ToArray(),
	Relay = relay?.GameObject.Id, UnitStates = relay?.UnitSequences.Count, DoorStates = relay?.DoorStates.Count,
	Doors = scene.GetAllComponents<Diesel.PaydayDoor>().Take( 6 ).Select( d => new
		{ d.GameObject.Id, d.GameObject.Name, d.Open, d.Amount, Position = d.WorldPosition.ToString() } ).ToArray()
};
