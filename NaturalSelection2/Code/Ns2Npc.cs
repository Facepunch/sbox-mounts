using Sandbox.Npcs;

namespace NS2;

public abstract partial class Ns2Npc : BaseNpc
{
	private protected static bool AllowsAttack( GameObject target ) => NpcConVars.Enabled && !AlienTargeting.IsIgnoredPlayer( target );

	private protected static ModelLoader FindModel( string path ) => Sandbox.Mounting.Directory.Get( "ns2" )?.GetByPath( path ) as ModelLoader;

	private protected static SparkAnimationGraph.Playback CreatePlayback( string path ) => FindModel( path )?.CreatePlayback();

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
		trace.WithoutTags( "ns2_movement", BabblerPheromone.BallTag ).UseHitboxes();

	internal static Vector3 SweepExtents( Vector3 direction, float radius ) =>
		new Vector3( direction.WithX( 0 ).Length, direction.WithY( 0 ).Length, direction.WithZ( 0 ).Length ) * radius;

	internal static SceneTraceResult TraceBullet( this SceneTrace trace, Vector3 start, Vector3 direction, float radius )
	{
		var hit = trace.Run();
		if ( hit.Hit && hit.GameObject?.Tags.Has( "world" ) != true ) return hit;
		var extents = SweepExtents( direction, radius );
		return trace.FromTo( start, hit.EndPosition ).Size( -extents, extents ).Run();
	}

	internal static GameObject GetBulletTarget( this SceneTraceResult hit, out Component.IDamageable health, out bool penetrable )
	{
		health = hit.GameObject?.GetComponentInParent<Component.IDamageable>();
		var corpse = hit.GameObject?.GetComponentInParent<NpcCorpse>();
		var target = (health as Component)?.GameObject ?? corpse?.GameObject ?? hit.GameObject;
		penetrable = corpse.IsValid() || target.IsValid() && (health is { MaxHealth: > 0, Health: <= 0 } || target.Tags.HasAny( "ragdoll", "ns2_mac", "ns2_drifter", "ns2_hydra", "ns2_hallucination" ));
		return target;
	}
}
