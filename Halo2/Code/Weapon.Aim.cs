namespace Halo2;

public abstract partial class Weapon
{
	protected readonly record struct AimCone( float Angle, float Range );
	readonly HashSet<Sandbox.Npcs.BaseNpc> aimCandidates = new();

	protected GameObject FindAttackTarget( PlayerController player, Ray ray, AimCone cone, out Vector3 point )
	{
		var contact = BulletTrace( ray, cone.Range, 0 ).Run();
		if ( contact.Hit && IsEnemy( contact.GameObject, player ) )
		{
			point = contact.HitPosition;
			return contact.GameObject;
		}
		FindAimTarget( player, cone, default, 1, out var target, out var offset, ray );
		point = ray.Position + offset;
		return target;
	}

	static bool IsEnemy( GameObject target, PlayerController player )
	{
		if ( !target.IsValid() ) return false;
		if ( target.GetComponentInParent<Sandbox.Npcs.BaseNpc>() is {} npc )
			return npc.Health > 0 && npc.GetDisposition( player.GameObject ) == Sandbox.Npcs.Disposition.Hostile;
		return target.GetComponentInParent<PlayerController>().IsValid();
	}

	// Both angular and distance confidence are full up to half the authored limit.
	static float Falloff( float value, float limit ) => limit > 0 ? ((limit - value) / (limit * 0.5f)).Clamp( 0, 1 ) : 0;

	protected float FindAimTarget( PlayerController player, AimCone autoAimCone, AimCone magnetismCone, float inconsequentialScale,
		out GameObject targetObject, out Vector3 targetOffset, Ray? aim = null )
	{
		targetObject = null;
		targetOffset = default;
		var ray = aim ?? player.EyeTransform.ForwardRay;
		float bestAutoAim = 0, bestMagnetism = 0;
		float bestDistance = float.PositiveInfinity, bestAngle = float.PositiveInfinity;
		foreach ( var obj in Scene.FindInPhysics( new Sphere( ray.Position, System.MathF.Max( autoAimCone.Range, magnetismCone.Range ) ) ) )
		{
			var npc = obj.GetComponentInParent<Sandbox.Npcs.BaseNpc>();
			if ( !npc.IsValid() || !aimCandidates.Add( npc ) || npc.Health <= 0 || !npc.Renderer.IsValid() ) continue;
			if ( npc.GetDisposition( player.GameObject ) != Sandbox.Npcs.Disposition.Hostile ) continue;
			var renderer = npc.Renderer;
			if ( renderer.Model is not {} model ) continue;
			foreach ( var target in ModelTarget.For( model ) )
			{
				if ( !target.TryAimPoint( renderer, ray, out var point, out _ ) ) continue;
				var offset = point - ray.Position;
				float distance = offset.Length;
				var direction = offset.Normal;
				float angle = System.MathF.Acos( Vector3.Dot( ray.Forward, direction ).Clamp( -1, 1 ) );
				float autoAim = Falloff( angle, autoAimCone.Angle ) * Falloff( distance, autoAimCone.Range );
				float magnetism = Falloff( angle, magnetismCone.Angle ) * Falloff( distance, magnetismCone.Range );
				magnetism *= target.MagnetismWeight * (target.Inconsequential ? inconsequentialScale : 1);
				if ( autoAim == 0 && magnetism == 0 ) continue;
				if ( autoAim < bestAutoAim || (autoAim == bestAutoAim && magnetism < bestMagnetism) ) continue;
				if ( autoAim == bestAutoAim && magnetism == bestMagnetism &&
					(distance > bestDistance || (distance == bestDistance && angle >= bestAngle)) ) continue;
				var obstruction = BulletTrace( new Ray( ray.Position, direction ), distance, 0 ).IgnoreGameObjectHierarchy( npc.GameObject ).Run();
				if ( obstruction.Hit || obstruction.StartedSolid ) continue;
				bestAutoAim = autoAim;
				bestMagnetism = magnetism;
				bestDistance = distance;
				bestAngle = angle;
				targetObject = npc.GameObject;
				targetOffset = offset;
			}
		}
		aimCandidates.Clear();
		return bestMagnetism;
	}
}
