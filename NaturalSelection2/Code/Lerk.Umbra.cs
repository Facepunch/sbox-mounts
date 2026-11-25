using Sandbox.Npcs;

namespace NS2;

public sealed partial class Lerk : Component.IDamageable.IEvents
{
	[Property] public bool UmbraUnlocked { get; set; } = true;
	const float UmbraCost = 27;
	const float UmbraRange = 17 * Scale;
	readonly Dictionary<BaseNpc, float> combat = [];
	float lastUmbra;
	BaseNpc umbraTarget;

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
		if ( !UmbraUnlocked || energy <= 1.5f * UmbraCost || Time.Now - lastUmbra <= 4 ) return null;
		BaseNpc best = null;
		float closest = UmbraRange * UmbraRange;
		foreach ( var (candidate, expires) in combat )
		{
			if ( expires <= Time.Now || !candidate.IsValid() || candidate.Health <= 0 )
			{
				combat.Remove( candidate );
				continue;
			}
			if ( candidate.GetComponent<UmbraProtection>().IsValid() || candidate.Faction != Faction ) continue;
			float distance = WorldPosition.DistanceSquared( candidate.WorldPosition );
			if ( distance >= UmbraRange * UmbraRange ) continue;
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

	void FireUmbra()
	{
		if ( energy < UmbraCost ) return;
		energy -= UmbraCost;
		lastUmbra = Time.Now;
		var hit = Scene.Trace.Ray( EyePosition, EyePosition + View.Forward * UmbraRange )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ns2_babbler" ).UseHitboxes().Run();
		var travel = (hit.EndPosition - WorldPosition).ClampLength( UmbraRange );
		var go = new GameObject( false, "Umbra cloud" ) { WorldPosition = WorldPosition, WorldRotation = View };
		var cloud = go.AddComponent<UmbraCloud>();
		cloud.Destination = WorldPosition + travel;
		cloud.Faction = Faction;
		go.Enabled = true;
		if ( Networking.IsActive ) go.NetworkSpawn();
		PlayUmbraSound( WorldPosition );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void PlayUmbraSound( Vector3 position )
	{
		if ( GameResource.Load<SoundEvent>( "mount://ns2/sounds/alien/structures/crag/umbra.sound" ) is { } sound )
			Sound.Play( sound, position );
	}
}
