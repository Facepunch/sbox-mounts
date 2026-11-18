namespace Renderware;

partial class WorldLoader
{
	/// <summary>
	/// A spawn point at each of the game's respawn points (hospitals and police stations), facing the way the game
	/// faces you. Points the game puts "on the ground" (z = -100) are dropped onto the highest geometry below them.
	/// </summary>
	void BuildSpawnPoints( Dictionary<(int, int), Cell> cells )
	{
		var root = new GameObject( true, "spawn points" );
		int count = 0;

		foreach ( var point in Host.RestartPoints )
		{
			var position = point.Position * Units.Scale;

			if ( point.Position.z <= -99 )
			{
				if ( !cells.TryGetValue( CellKey( position ), out var cell ) || cell.GroundHeight( position ) is not { } ground )
					continue;

				position = position.WithZ( ground );
			}

			// GTA heading: 0 = north (+y), counter clockwise. s&box yaw: 0 = +x
			var obj = new GameObject( root, true, $"spawn {++count}" )
			{
				WorldPosition = position,
				WorldRotation = Rotation.FromYaw( point.Heading + 90 ),
			};
			obj.AddComponent<SpawnPoint>();
		}

		Log.Info( $"World: {count} spawn points" );
	}
}
