using Sandbox.Npcs;

namespace NS2;

public abstract class Ns2Npc : BaseNpc
{
	Vector3? lastOrigin;
	Vector3 engagementPoint;

	internal Vector3 EngagementPoint
	{
		get
		{
			var origin = WorldPosition;
			if ( lastOrigin != origin )
			{
				engagementPoint = GetEngagementPoint();
				lastOrigin = origin;
			}
			return engagementPoint;
		}
	}

	protected virtual Vector3 GetEngagementPoint() => WorldPosition;

	protected override Vector3 CalculateAimPoint( GameObject target ) =>
		target.GetComponent<Ns2Npc>()?.EngagementPoint ?? base.CalculateAimPoint( target );
}

internal static class DamageTraces
{
	internal static SceneTrace UseDamageHitboxes( this SceneTrace trace ) =>
		trace.WithoutTags( "ns2_movement" ).UseHitboxes();
}
