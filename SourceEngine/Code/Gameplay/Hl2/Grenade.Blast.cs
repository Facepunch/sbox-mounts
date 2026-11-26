using System;

namespace Source1.Hl2;

public sealed partial class Grenade
{
	internal void ApplyBlast()
	{
		if ( Radius <= 0 || Damage <= 0 ) return;
		var origin = WorldPosition + Vector3.Up;
		var targets = new HashSet<GameObject>();
		foreach ( var hit in Scene.FindInPhysics( new Sphere( origin, Radius ) ) )
		{
			var victim = hit.GetComponentInParent<Component.IDamageable>() as Component;
			var body = hit.GetComponentInParent<Rigidbody>();
			if ( victim.IsValid() ) targets.Add( victim.GameObject );
			if ( body.IsValid() && body.MotionEnabled ) targets.Add( body.GameObject );
		}

		foreach ( var target in targets )
		{
			if ( !target.IsValid() || target == GameObject ) continue;
			var prop = target.GetComponent<Prop>();
			var bounds = prop.IsValid() && prop.Model.IsValid() ? prop.Model.PhysicsBounds.Transform( target.WorldTransform ) : target.GetBounds();
			if ( SourceWaterMap.IsUnderwater( Scene, bounds.Center.WithZ( UnderwaterBlast ? bounds.Mins.z : bounds.Maxs.z ) ) != UnderwaterBlast ) continue;
			var grenade = target.GetComponent<Grenade>();
			var center = grenade.IsValid() ? target.WorldPosition : bounds.Center;
			var direction = (center - origin).Normal;
			var trace = Scene.Trace.Ray( origin, center ).WithoutTags( "trigger", "debris" ).IgnoreGameObjectHierarchy( GameObject );
			var hit = trace.Run();
			var position = hit.StartedSolid ? origin : hit.EndPosition;
			var transmission = 1f;
			if ( hit.Hit && hit.GameObject.IsValid() && !target.IsDescendant( hit.GameObject ) )
			{
				if ( trace.WithTag( "world" ).Run().Hit ) continue;
				var blocker = hit.GameObject.GetComponentInParent<Rigidbody>();
				transmission = blocker.IsValid() ? 1 - (blocker.Mass / 350).Clamp( 0, 1 ) : 0.75f;
				position = center;
			}

			if ( grenade.IsValid() && direction != Vector3.Zero && !hit.StartedSolid && hit.GameObject == target )
			{
				// Grenades take blast damage on their four-unit box, while their model hull handles bouncing.
				var axis = direction.Abs();
				var surface = HalfSize / MathF.Max( axis.x, MathF.Max( axis.y, axis.z ) );
				position = center - direction * MathF.Min( origin.Distance( center ), surface );
			}

			var damage = Damage * (1 - origin.Distance( position ) / Radius) * transmission;
			if ( damage <= 0 ) continue;
			var impulse = direction * (MathF.Min( damage, 100 ) * 300 * Random.Shared.Float( 0.85f, 1.15f ));
			if ( prop?.Model?.GetData<PropDamageModifiers>() is { } modifiers )
			{
				damage *= modifiers.Explosive;
				if ( prop.IsProxy && !modifiers.FireResistant && damage >= 1 && (int)damage < prop.Health )
					PropFire.Ignite( prop );
			}
			if ( target.Network.IsProxy ) RemoteBlastHit( target, origin, position, damage, impulse );
			else ApplyBlastHit( target, origin, position, damage, impulse );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void RemoteBlastHit( GameObject target, Vector3 origin, Vector3 position, float damage, Vector3 impulse )
	{
		if ( target.IsValid() && (Networking.IsHost || !target.Network.IsProxy) )
			ApplyBlastHit( target, origin, position, damage, impulse );
	}

	void ApplyBlastHit( GameObject target, Vector3 origin, Vector3 position, float damage, Vector3 impulse )
	{
		var body = target.GetComponent<Rigidbody>();
		if ( body.IsValid() && !body.IsProxy && body.MotionEnabled )
		{
			var grenade = target.GetComponent<Grenade>();
			body.ApplyImpulseAt( origin, grenade.IsValid() ? impulse * 2 : impulse );
			if ( grenade.IsValid() ) body.Velocity = body.Velocity.ClampLength( MaxPhysicsSpeed );
		}

		if ( target.GetComponent<Prop>() is { IsProxy: false } prop ) PropDamage.Attach( prop );
		var info = new DamageInfo { Damage = damage, Origin = origin, Position = position, Weapon = GameObject, Attacker = Attacker.IsValid() ? Attacker : GameObject };
		info.Tags.Add( DamageTags.Explosion );
		foreach ( var victim in target.GetComponents<Component.IDamageable>() ) victim.OnDamage( info );
	}
}
