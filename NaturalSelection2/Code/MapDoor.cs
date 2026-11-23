namespace NS2;

/// <summary>Plays the door model's sliding animations as players approach or leave.</summary>
[Hide]
public sealed class MapDoor : Component
{
	[Property] public SkinnedModelRenderer Renderer { get; set; }

	bool open = true;
	TimeUntil nextCheck;

	protected override void OnUpdate()
	{
		if ( nextCheck > 0 || !Renderer.IsValid() ) return;
		nextCheck = 0.3f;

		bool nearby = false;
		foreach ( var player in Scene.GetAllComponents<PlayerController>() )
		{
			var radius = player.Velocity.LengthSquared > 8f * 8f * MapLoader.Scale * MapLoader.Scale
				? 10f * MapLoader.Scale : 6f * MapLoader.Scale;
			if ( player.WorldPosition.DistanceSquared( WorldPosition ) >= radius * radius ) continue;
			nearby = true;
			break;
		}

		if ( nearby == open ) return;
		open = nearby;
		Renderer.Sequence.Blending = true;
		Renderer.Sequence.Name = open ? "open" : "close";
	}
}
