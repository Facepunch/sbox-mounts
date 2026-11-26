// Assert live relay initialization, including late joins and map reloads.
var deadline = DateTime.UtcNow.AddSeconds( 90 );
while ( true )
{
	var scene = Game.ActiveScene;
	var relay = scene?.GetAllComponents<Diesel.PaydayMapState>().FirstOrDefault();
	var player = scene?.GetAllComponents<PlayerController>().FirstOrDefault( p => p.Network.Owner == Connection.Local );
	if ( relay is not null && relay.Network.Active && player is not null && relay.DoorStates.Count > 0 )
	{
		await Task.Delay( 500, cancellationToken );
		var mismatches = new System.Collections.Generic.List<Guid>();
		foreach ( var (id, state) in relay.DoorStates )
		{
			var door = scene.Directory.FindByGuid( id )?.GetComponent<Diesel.PaydayDoor>();
			var expected = System.Text.Json.Nodes.JsonNode.Parse( state );
			if ( door is null || door.Open != expected["Open"].GetValue<bool>()
				|| MathF.Abs( door.Amount - expected["Amount"].GetValue<float>() ) > .02f )
				mismatches.Add( id );
		}
		foreach ( var (id, state) in relay.AlarmStates )
		{
			var phone = scene.Directory.FindByGuid( id )?.GetComponent<Diesel.PaydayAlarm>();
			var expected = System.Text.Json.Nodes.JsonNode.Parse( state );
			if ( phone is null || phone.Disabled != expected["Disabled"].GetValue<bool>()
				|| phone.Activated != expected["Activated"].GetValue<bool>() ) mismatches.Add( id );
		}
		foreach ( var (id, state) in relay.CameraStates )
		{
			var camera = scene.Directory.FindByGuid( id )?.GetComponent<Diesel.PaydaySecurityCamera>();
			var expected = System.Text.Json.Nodes.JsonNode.Parse( state );
			if ( camera is null || camera.Disabled != expected["Disabled"].GetValue<bool>()
				|| camera.Health != expected["Health"].GetValue<float>() ) mismatches.Add( id );
		}
		foreach ( var (id, state) in relay.UnitSequences )
		{
			var unit = scene.Directory.FindByGuid( id )?.GetComponent<Diesel.PaydayUnitState>();
			var applied = unit is null ? null : (System.Collections.Generic.List<string>)typeof(Diesel.PaydayUnitState)
				.GetField( "applied", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).GetValue( unit );
			if ( applied is null || string.Join( "|", applied ) != state ) mismatches.Add( id );
			if ( unit?.GetComponent<ModelRenderer>() is { Enabled: true } renderer && (renderer.Model is null || renderer.Model.IsError) )
				mismatches.Add( id );
		}
		if ( mismatches.Count == 0 )
			return new { Networking.IsHost, Relay = relay.GameObject.Id, Doors = relay.DoorStates.Count,
				Phones = relay.AlarmStates.Count, Cameras = relay.CameraStates.Count, ChangedUnits = relay.UnitSequences.Count, MatchingStates = true };
	}
	if ( DateTime.UtcNow > deadline ) throw new Exception( "Network map never reached a live relay, local player and matching door poses." );
	await Task.Delay( 250, cancellationToken );
}
