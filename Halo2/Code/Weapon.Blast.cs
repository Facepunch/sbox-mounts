namespace Halo2;

public sealed record BlastDamage( float InnerRadius, float OuterRadius, float CoreRadius, float Minimum,
	float Maximum, float Acceleration, Dictionary<string, float> BodyScales );

public abstract partial class Weapon
{
	[Property] public BlastDamage ImpactBlast { get; set; }

	[Property] public string DetonationEffectPath { get; set; }

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void PresentDetonation( string path, GameObject target, Vector3 point, Vector3 normal, Vector3 incoming )
	{
		if ( Application.IsDedicatedServer || !target.IsValid() ) return;
		GameObject.GetPrefab( path )?.GetComponent<PelletImpactEffect>()?.Place( null, target, point, normal, incoming );
	}

	static Vector3 OutsideSurface( Vector3 origin, Vector3 normal )
	{
		// Start visibility rays on the exposed side of the impact plane. The hit coordinate
		// can round back inside the collider, especially on an axis-aligned wall.
		static float Outside( float coordinate, float axis ) => axis > 0 ? System.MathF.BitIncrement( coordinate )
			: axis < 0 ? System.MathF.BitDecrement( coordinate ) : coordinate;
		return new Vector3( Outside( origin.x, normal.x ), Outside( origin.y, normal.y ), Outside( origin.z, normal.z ) );
	}

	void Detonate( Scene scene, BlastDamage blast, Vector3 origin, Vector3 normal, GameObject attacker )
	{
		var exposed = OutsideSurface( origin, normal );
		var nearest = new Dictionary<GameObject, (Collider Collider, Vector3 Point, float Distance)>();
		foreach ( var obj in scene.FindInPhysics( new Sphere( origin, blast.OuterRadius ) ) )
		{
			var target = (obj.GetComponentInParent<Component.IDamageable>() as Component)?.GameObject
				?? obj.GetComponentInParent<Rigidbody>()?.GameObject;
			if ( !target.IsValid() ) continue;
			var itemBody = target.GetComponent<BaseInventoryItem>() is { Inventory: null } ? target.GetComponent<Rigidbody>() : null;
			foreach ( var collider in obj.GetComponentsInChildren<Collider>( true ) )
			{
				if ( !collider.Active || collider.IsTrigger ) continue;
				var point = itemBody.IsValid() ? itemBody.WorldTransform.PointToWorld( itemBody.MassCenter )
					: collider.FindClosestPoint( origin );
				float distance = point.Distance( origin );
				if ( distance >= blast.OuterRadius || (nearest.TryGetValue( target, out var previous ) && previous.Distance <= distance) ) continue;
				nearest[target] = (collider, point, distance);
			}
		}

		// A biped's movement capsule can extend past its model colliders.
		void IncludeBiped( GameObject target, ModelRenderer renderer, bool crouching )
		{
			if ( renderer?.Model?.GetData<UnitDefinition>() is not { Radius: > 0, StandingHeight: > 0 } unit ) return;
			var transform = target.WorldTransform;
			var local = transform.PointToLocal( origin );
			float height = crouching ? unit.CrouchingHeight : unit.StandingHeight;
			float radius = unit.Radius;
			var axis = Vector3.Up * local.z.Clamp( radius, System.MathF.Max( radius, height - radius ) );
			var delta = local - axis;
			var point = delta.LengthSquared <= radius * radius ? origin : transform.PointToWorld( axis + delta.Normal * radius );
			float distance = point.Distance( origin );
			if ( distance >= blast.OuterRadius )
			{
				nearest.Remove( target );
				return;
			}
			var collider = nearest.TryGetValue( target, out var previous ) ? previous.Collider
				: target.GetComponentsInChildren<Collider>( true ).FirstOrDefault( c => c.Active && !c.IsTrigger );
			if ( collider.IsValid() ) nearest[target] = (collider, point, distance);
		}

		foreach ( var npc in scene.GetAllComponents<Sandbox.Npcs.BaseNpc>() )
			IncludeBiped( npc.GameObject, npc.Renderer, false );
		foreach ( var player in scene.GetAllComponents<PlayerController>() )
			IncludeBiped( player.GameObject, player.Renderer, player.IsDucking );

		foreach ( var (target, contact) in nearest )
		{
			if ( !target.IsValid() || !contact.Collider.IsValid() ) continue;
			var delta = contact.Point - origin;
			var direction = delta.LengthSquared > 0 ? delta.Normal : (target.WorldPosition - origin).Normal;
			bool Visible( Vector3 start, Vector3 end ) => !scene.Trace.Ray( start, end ).WithCollisionRules( "bullet" )
				.IgnoreGameObjectHierarchy( target ).Run().Hit;
			bool biped = target.GetComponent<PlayerController>().IsValid() || target.GetComponent<Sandbox.Npcs.BaseNpc>().IsValid();
			if ( contact.Distance > 0 && biped && blast.CoreRadius > 0 )
			{
				var side = Perpendicular( direction ) * blast.CoreRadius;
				var up = Vector3.Cross( direction, side );
				bool Open( Vector3 offset )
				{
					var clipped = scene.Trace.Ray( exposed, exposed + offset ).WithCollisionRules( "bullet" ).IgnoreGameObjectHierarchy( target ).Run();
					return Visible( clipped.EndPosition, contact.Point );
				}
				if ( !Open( side ) && !Open( -side ) && !Open( up ) && !Open( -up ) ) continue;
			}
			else if ( contact.Distance > 0 && !Visible( exposed, contact.Point ) ) continue;

			float fraction = ((blast.OuterRadius - contact.Distance) / (blast.OuterRadius - blast.InnerRadius)).Clamp( 0, 1 );
			var hit = new SceneTraceResult { Hit = true, StartPosition = origin, HitPosition = contact.Point,
				GameObject = contact.Collider.GameObject, Collider = contact.Collider, Surface = contact.Collider.Surface, Direction = direction };
			ApplyBlastHit( hit, blast, MathX.Lerp( blast.Minimum, blast.Maximum, fraction ), attacker );
		}
	}

	void ApplyBlastHit( SceneTraceResult hit, BlastDamage blast, float damage, GameObject attacker )
	{
		var receiver = hit.GameObject.GetComponentInParent<Component.IDamageable>() as Component;
		var vitality = ProjectileDamageSystem.CharacterVitality( receiver?.GameObject, out var renderer );
		float shield = ProjectileDamageSystem.AbsorbShield( vitality, renderer, PlayerShieldMaterial, blast.BodyScales, ref damage );
		damage *= BodyMaterial.DamageScale( hit, blast.BodyScales, true );
		ApplyDelayedHit( hit, shield + damage, blast.Acceleration, attacker, new TagSet { "explosion" }, false );
	}
}
