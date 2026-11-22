using Sandbox.Engine.Resources;

namespace GoldSrc.Maps;

internal static class MonsterClip
{
	internal static NavMeshAreaDefinition Area => GameResource.Load<NavMeshAreaDefinition>( "goldsrc/monsterclip.navarea" );

	internal static void Restrict( GameObject go )
	{
		if ( go.GetComponent<NavMeshAgent>( true ) is {} agent && Area is {} area )
			agent.ForbiddenAreas.Add( area );
	}
}
