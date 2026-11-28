using Sandbox.Physics;

namespace Source1;

public sealed class MapClipRules : Component
{
	internal const string PlayerClip = "source1_playerclip";
	internal const string NpcClip = "source1_npcclip";
	internal const string ActorClip = "source1_actorclip";

	protected override void OnEnabled()
	{
		var world = Scene.PhysicsWorld;
		if ( world?.CollisionRules is {} rules && rules is not Rules )
			world.CollisionRules = new Rules( rules );
	}

	private sealed class Rules : CollisionRules
	{
		public Rules( CollisionRules original )
		{
			Defaults = new( original.Defaults );
			Pairs = new( original.Pairs );
			Defaults[PlayerClip] = Defaults[NpcClip] = Defaults[ActorClip] = Result.Ignore;
			Pairs[(PlayerClip, "player")] = Pairs[(ActorClip, "player")] = Result.Collide;
			Pairs[(NpcClip, "npc")] = Pairs[(ActorClip, "npc")] = Result.Collide;
			OnValidate();
		}
	}
}
