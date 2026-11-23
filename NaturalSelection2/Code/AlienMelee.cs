using IDamageable = Sandbox.Component.IDamageable;
using Sandbox.Npcs;

namespace NS2;

internal static class AlienMelee
{
	// Spark samples the center, four sides and four corners of the weapon's melee box.
	internal static (SceneTraceResult Hit, IDamageable Victim) Trace( BaseNpc npc, Rotation view, float reach, float width, float height )
	{
		width *= MapLoader.Scale / 3;
		height *= MapLoader.Scale / 3;
		var size = new Vector3( width, width, height );
		System.ReadOnlySpan<int> order = [4, 1, 3, 5, 7, 0, 2, 6, 8];
		SceneTraceResult hit = default, center = default;
		IDamageable victim = null;
		var forward = view.Forward.WithZ( 0 ).Normal;
		foreach ( var cell in order )
		{
			var start = npc.EyePosition + view.Left * ((cell % 3 - 1) * width) + view.Up * ((cell / 3 - 1) * height);
			var trace = npc.Scene.Trace.Ray( start, start + view.Forward * reach ).Size( size )
				.IgnoreGameObjectHierarchy( npc.GameObject ).UseHitboxes().Run();
			if ( cell == 4 ) center = trace;
			if ( trace.GameObject?.GetComponentInParent<IDamageable>() is not { } target || npc.GetDisposition( (target as Component)?.GameObject ?? trace.GameObject ) != Disposition.Hostile ) continue;
			if ( Vector3.Dot( trace.EndPosition - npc.EyePosition, forward ) <= 0.1f * MapLoader.Scale ) continue;
			bool player = trace.GameObject.GetComponentInParent<PlayerController>() is not null;
			if ( victim is null || player ) (hit, victim) = (trace, target);
			if ( player ) break;
		}
		if ( victim is null )
		{
			hit = center;
			if ( hit.GameObject?.GetComponentInParent<IDamageable>() is Component candidate && npc.GetDisposition( candidate.GameObject ) == Disposition.Hostile )
				victim = (IDamageable)candidate;
		}
		return (hit, victim);
	}
}
