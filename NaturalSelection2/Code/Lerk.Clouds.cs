using Sandbox.Npcs;

namespace NS2;

public sealed partial class Lerk : Component.IDamageable.IEvents
{
	[Property] public bool UmbraUnlocked { get; set; } = true;
	[Property] public bool SporesUnlocked { get; set; } = true;
	const float CloudCost = 27;
	const float CloudRange = 17 * Scale;
	readonly Dictionary<BaseNpc, float> combat = [];
	Vector3 modelOrigin;
	float lastUmbra, lastSpore;
	BaseNpc umbraTarget;
	GameObject sporeTarget;

	void IDamageable.IEvents.OnDamaged( IDamageable.DamageEvent e )
	{
		if ( !Networking.IsHost || !UmbraUnlocked || e.AcceptedDamage <= 0 ) return;
		Remember( e.Victim );
		Remember( e.Instigator );

		void Remember( GameObject actor )
		{
			if ( actor.IsValid() && actor.Tags.Has( "ns2_lifeform" ) && actor.GetComponent<BaseNpc>() is { } alien && alien.Faction == Faction )
				combat[alien] = Time.Now + 3;
		}
	}

	BaseNpc ChooseUmbraTarget()
	{
		if ( !UmbraUnlocked || energy <= 1.5f * CloudCost || Time.Now - lastUmbra <= 4 || Time.Now - lastSpore <= 4 ) return null;
		BaseNpc best = null;
		float closest = CloudRange * CloudRange;
		foreach ( var (candidate, expires) in combat )
		{
			if ( expires <= Time.Now || !candidate.IsValid() || candidate.Health <= 0 )
			{
				combat.Remove( candidate );
				continue;
			}
			if ( candidate.GetComponent<UmbraProtection>().IsValid() || candidate.Faction != Faction ) continue;
			float distance = WorldPosition.DistanceSquared( candidate.WorldPosition );
			if ( distance >= CloudRange * CloudRange ) continue;
			if ( candidate == this )
			{
				if ( Time.Now - lastUmbra <= 8 ) continue;
				distance = 100 * Scale * Scale;
			}
			if ( distance < closest ) (best, closest) = (candidate, distance);
		}
		if ( best.IsValid() && best != this && Scene.Trace.Ray( EyePosition, CalculateAimPoint( best.GameObject ) )
			.IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( best.GameObject ).WithoutTags( "trigger" ).Run().Hit ) return null;
		return best;
	}

	GameObject ChooseSporeTarget()
	{
		if ( !SporesUnlocked || energy <= 1.5f * CloudCost || Time.Now - lastSpore <= 8 || Time.Now - lastUmbra <= 2 ) return null;
		GameObject best = null;
		float closest = 20 * 20 * Scale * Scale;
		foreach ( var target in Senses.VisibleHostiles )
		{
			if ( !target.IsValid() || AlienTargeting.IsIgnoredPlayer( target ) || !IsSpikeTarget( target ) ) continue;
			if ( target.GetComponentInChildren<IDamageable>() is { MaxHealth: > 0, Health: <= 0 } ) continue;
			float distance = WorldPosition.DistanceSquared( target.WorldPosition );
			if ( distance < closest ) (best, closest) = (target, distance);
		}
		if ( best.IsValid() )
			foreach ( var cloud in Scene.GetAllComponents<SporeCloud>() )
				if ( best.WorldPosition.DistanceSquared( cloud.WorldPosition ) <= 11 * 11 * Scale * Scale ) return null;
		return best;
	}

	void FireCloud( bool spores )
	{
		if ( energy < CloudCost ) return;
		var hit = Scene.Trace.Ray( EyePosition, EyePosition + View.Forward * CloudRange )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ns2_babbler" ).UseHitboxes().Run();
		var origin = Renderer.WorldTransform.PointToWorld( modelOrigin );
		var travel = hit.EndPosition - origin;
		var go = new GameObject( false, spores ? "Spore cloud" : "Umbra cloud" ) { WorldPosition = origin };
		LerkCloud cloud;
		float distance;
		if ( spores )
		{
			var gas = go.AddComponent<SporeCloud>();
			gas.Owner = this;
			cloud = gas;
			distance = System.Math.Min( CloudRange + SporeCloud.Radius, travel.Length );
			if ( hit.GameObject?.GetComponentInParent<IDamageable>() is null ) distance -= 0.75f * SporeCloud.Radius;
			lastSpore = Time.Now;
		}
		else
		{
			cloud = go.AddComponent<UmbraCloud>();
			distance = System.Math.Min( CloudRange, travel.Length );
			lastUmbra = Time.Now;
		}
		var direction = travel.Normal * distance;
		cloud.Destination = origin + direction;
		go.WorldRotation = direction.IsNearlyZero() ? View : Rotation.LookAt( spores ? -direction : direction );
		cloud.Faction = Faction;
		go.Enabled = true;
		if ( Networking.IsActive ) go.NetworkSpawn();
		energy -= CloudCost;
		PlayCloudSound( spores ? "alien/lerk/spore_spray_once" : "alien/structures/crag/umbra", WorldPosition );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void PlayCloudSound( string id, Vector3 position )
	{
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{id}.sound" ) is { } sound )
			Sound.Play( sound, position );
	}
}
