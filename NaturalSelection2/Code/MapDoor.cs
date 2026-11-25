using Sandbox.Npcs;

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

		bool nearby = HasOpener();
		if ( nearby == open ) return;
		open = nearby;
		Renderer.Sequence.Blending = true;
		Renderer.Sequence.Name = open ? "open" : "close";
	}

	bool HasOpener()
	{
		foreach ( var player in Scene.GetAllComponents<PlayerController>() )
			if ( InRange( player.WorldPosition, player.Velocity ) ) return true;

		foreach ( var npc in Scene.GetAllComponents<BaseNpc>() )
		{
			if ( npc.Health <= 0 || !npc.GameObject.Tags.HasAny( "ns2_lifeform", "ns2_marine", "ns2_exo" ) ) continue;
			if ( InRange( npc.WorldPosition, BaseNpc.GetVelocity( npc.GameObject ) ) ) return true;
		}
		return false;
	}

	bool InRange( Vector3 position, Vector3 velocity )
	{
		const float scale = MapLoader.Scale;
		float radius = velocity.LengthSquared > 8 * 8 * scale * scale ? 10 * scale : 6 * scale;
		return WorldPosition.DistanceSquared( position ) < radius * radius;
	}
}
