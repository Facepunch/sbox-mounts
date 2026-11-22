using Sandbox.Engine.Resources;
using Sandbox.Physics;

namespace GoldSrc.Maps;

public sealed class MonsterClip : MapEntity
{
	internal const string BrushTag = "goldsrc_monsterclip";
	private const string MonsterTag = "goldsrc_hitmonsterclip";
	internal static NavMeshAreaDefinition Area => GameResource.Load<NavMeshAreaDefinition>( "goldsrc/monsterclip.navarea" );

	protected override void OnEnabled()
	{
		var world = Scene.PhysicsWorld;
		if ( world is null ) return;
		Scene.NavMesh.ExcludedBodies.Add( BrushTag );
		var existing = world.CollisionRules;
		if ( existing?.Pairs.ContainsKey( (BrushTag, MonsterTag) ) == true ) return;
		// Scene worlds initially share the project's rules. Extend only this scene's copy.
		var rules = new CollisionRules();
		if ( existing is not null )
		{
			rules.Defaults = new( existing.Defaults );
			rules.Pairs = new( existing.Pairs );
		}
		rules.Defaults[BrushTag] = CollisionRules.Result.Ignore;
		rules.Pairs[(BrushTag, MonsterTag)] = CollisionRules.Result.Collide;
		world.CollisionRules = Json.Deserialize<CollisionRules>( Json.Serialize( rules ) );
	}

	internal static void Restrict( GameObject go )
	{
		go.Tags.Add( MonsterTag );
		if ( go.GetComponent<NavMeshAgent>( true ) is {} agent && Area is {} area )
			agent.ForbiddenAreas.Add( area );
	}
}
