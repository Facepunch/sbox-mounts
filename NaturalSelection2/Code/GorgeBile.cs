using Sandbox.Npcs;
using System;

namespace NS2;

/// <summary>Bile coats the targets exposed at impact. They carry the damage with them for five seconds.</summary>
public sealed class GorgeBile : Component
{
	const float Radius = 6 * MapLoader.Scale;
	// NS2 checks the 0.4 second damage interval on the server actor update (4 Hz).
	const float ServerUpdate = 0.25f, DamageInterval = 0.4f;
	readonly List<(Component Target, Vector3 LocalPoint, float Damage)> targets = [];
	Gorge owner;
	TimeUntil expires, nextUpdate;
	TimeSince lastTick;

	internal static void Splash( Gorge gorge, Vector3 origin )
	{
		var marker = new GameObject( false, "Bile splash" ) { WorldPosition = origin, NetworkMode = NetworkMode.Never };
		var bile = marker.AddComponent<GorgeBile>();
		bile.owner = gorge;
		bile.expires = 5;
		bile.nextUpdate = ServerUpdate;
		bile.lastTick = 0;
		var seen = new HashSet<Component>();
		foreach ( var obj in marker.Scene.FindInPhysics( new Sphere( origin, Radius ) ) )
		{
			if ( obj.GetComponentInParent<IDamageable>() is not Component target || !seen.Add( target ) ||
				target.WorldPosition.DistanceSquared( origin ) > Radius * Radius || gorge.GetDisposition( target.GameObject ) != Disposition.Hostile ) continue;
			var end = target.GetComponentInChildren<Collider>()?.GetWorldBounds().Center ?? target.WorldPosition;
			var trace = marker.Scene.Trace.Ray( origin, end ).WithoutTags( "trigger" );
			var wall = trace.WithTag( "world" ).Run();
			var point = target.WorldPosition;
			var hit = trace.UseHitboxes().RunAll().FirstOrDefault( x => x.GameObject?.GetComponentInParent<IDamageable>() == (IDamageable)target );
			if ( hit.Hit && (!wall.Hit || hit.Fraction <= wall.Fraction) ) point = hit.HitPosition;
			else if ( wall.Hit && wall.Fraction <= 0.9f ) continue;

			float fraction = Math.Clamp( point.Distance( origin ) / Radius, 0, 1 );
			float damage = DamageInterval * Math.Max( 0.1f, 55 * MathF.Cos( fraction * MathF.PI * 0.5f ) );
			bile.targets.Add( (target, target.WorldTransform.PointToLocal( point ), damage) );
		}
		if ( bile.targets.Count == 0 ) marker.Destroy();
		else marker.Enabled = true;
	}

	protected override void OnFixedUpdate()
	{
		if ( expires <= 0 ) { GameObject.Destroy(); return; }
		if ( nextUpdate > 0 ) return;
		nextUpdate = ServerUpdate;
		if ( lastTick <= DamageInterval ) return;
		lastTick = 0;
		foreach ( var (target, point, damage) in targets )
		{
			if ( !target.IsValid() || target is not IDamageable hp || hp.MaxHealth > 0 && hp.Health <= 0 ) continue;
			float amount = damage;
			if ( target.GameObject.Tags.HasAny( "ns2_exo", "ns2_exosuit" ) ) amount *= 0.4f;
			else if ( target.GameObject.Tags.HasAny( "ns2_lifeform", "ns2_marine" ) || target.GetComponentInParent<PlayerController>().IsValid() )
			{
				hp.Armour = Math.Max( 0, hp.Armour - damage * 0.12f * 0.5f );
				continue;
			}
			var hit = new DamageInfo { Damage = amount, Position = target.WorldTransform.PointToWorld( point ), Origin = WorldPosition,
				Attacker = owner.IsValid() ? owner.GameObject : null, Weapon = GameObject };
			hit.Tags.Add( "corrode" );
			hp.OnDamage( hit );
		}
	}
}
