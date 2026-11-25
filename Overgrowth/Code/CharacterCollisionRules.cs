using Sandbox.Physics;

namespace Overgrowth;

internal sealed class CharacterCollisionRules : CollisionRules
{
	internal const string FighterHull = "overgrowth_fighter_hull";
	internal const string ThrownItem = "overgrowth_thrown_item";
	internal const string RagdollPart = "overgrowth_ragdoll_part";

	CharacterCollisionRules( CollisionRules original )
	{
		Defaults = new( original.Defaults );
		Pairs = new( original.Pairs )
		{
			[(ThrownItem, FighterHull)] = Result.Ignore,
			[(RagdollPart, FighterHull)] = Result.Ignore
		};
		OnValidate();
	}

	internal static void Apply( PhysicsWorld world )
	{
		var rules = world.CollisionRules;
		if ( rules.GetCollisionRule( ThrownItem, FighterHull ) != Result.Ignore || rules.GetCollisionRule( RagdollPart, FighterHull ) != Result.Ignore )
			world.CollisionRules = new CharacterCollisionRules( rules );
	}
}
