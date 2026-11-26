using System;

namespace NS2;

public sealed partial class Gorge
{
	TimeUntil nextHatch;

	internal BabblerPheromone ActivePheromone { get; private set; }
	TimeUntil nextThrow;
	bool throwing;

	bool CanThrowBabblers( int count ) => freeSlots.Count <= 6 - count && energy >= 7 && nextThrow <= 0 &&
		!GameObject.Tags.Has( "ns2_hallucination" );

	float StructureThrowWeight( GameObject target, Vector3 destination, Vector3? threat )
	{
		if ( !CanThrowBabblers( 4 ) || underFire > 0 || IsFleeTarget( target ) ||
			WorldPosition.DistanceSquared( destination ) > 21 * 21 * Scale * Scale ||
			threat is { } position && WorldPosition.DistanceSquared( position ) < 14 * 14 * Scale * Scale ) return 0;
		return 4 + StructureUrgency( target );
	}

	static bool IsFleeTarget( GameObject target ) => target.IsValid() &&
		(target.Tags.HasAny( "ns2_marine", "ns2_exo" ) || target.GetComponent<PlayerController>().IsValid());

	bool CanFleeThrow( GameObject threat, bool visible )
	{
		if ( !visible || !CanThrowBabblers( 2 ) ) return false;
		if ( underFire <= 0 && (!InCombat || HealthFraction( this ) >= 0.75f) ) return false;
		return IsFleeTarget( threat ) &&
			WorldPosition.DistanceSquared( threat.WorldPosition ) <= 14 * 14 * Scale * Scale;
	}

	AlienLifeform FindDonationTarget( Vector3? threat )
	{
		if ( !CanThrowBabblers( 3 ) || InCombat || threat is { } position &&
			WorldPosition.DistanceSquared( position ) < 25 * 25 * Scale * Scale ) return null;
		AlienLifeform best = null;
		float nearest = 10 * 10 * Scale * Scale;
		foreach ( var candidate in Scene.GetAllComponents<AlienLifeform>() )
		{
			if ( candidate is Gorge || candidate.Health <= 0 || !Friendly( candidate ) ) continue;
			float distance = WorldPosition.DistanceSquared( candidate.WorldPosition );
			if ( distance > nearest || candidate.DonationSpots() < 2 || WallBetween( EyePosition, CalculateAimPoint( candidate.GameObject ) ) ) continue;
			best = candidate;
			nearest = distance;
		}
		return best;
	}

	internal bool TryDonate( AlienLifeform recipient )
	{
		if ( recipient == this || !recipient.CanReceiveBabblers || !Friendly( recipient ) ) return false;
		int spots = recipient.DonationSpots();
		if ( spots == 0 ) return true;
		// NS2 prefers nearby free Babblers before taking them off their owner's shield.
		float Priority( Babbler b ) => b.WorldPosition.DistanceSquared( WorldPosition ) * (b.ClingSlot == 0 ? 1 : 6.25f);
		foreach ( var b in brood.OrderBy( Priority ) )
		{
			if ( b.Health <= 0 || b.WorldPosition.DistanceSquared( WorldPosition ) > 1000f * 1000 * Scale * Scale ) continue;
			b.Dispatch( recipient.GameObject, recipient.WorldPosition );
			if ( --spots == 0 ) break;
		}
		return true;
	}

	void ThrowBabblers()
	{
		ActivePheromone = null;
		PlayCue( "babbler_ball_spit", WorldPosition );
		nextThrow = 3;
		throwing = false;
		primaryHeld = false;
		firing = false;
		var end = EyePosition + aim.Forward * (16 * Scale);
		var trace = Scene.Trace.Ray( EyePosition, end ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ns2_babbler" );
		var hit = trace.Run();
		if ( !hit.GameObject.IsValid() || hit.GameObject.Tags.Has( "world" ) )
		{
			var forward = aim.Forward;
			var size = DamageTraces.SweepExtents( forward, 2 * 0.315f * Scale );
			hit = Scene.Trace.Ray( EyePosition, hit.Hit ? hit.EndPosition : end ).Size( size )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ns2_babbler" ).Run();
		}
		var victim = hit.GameObject?.GetComponentInParent<IDamageable>();
		if ( victim is AlienLifeform recipient && TryDonate( recipient ) )
		{
			BabblerPheromone.Puff( hit.HitPosition );
			return;
		}
		var enemy = (victim as Component)?.GameObject;
		if ( !enemy.IsValid() || AlienTargeting.IsIgnoredPlayer( enemy ) || victim.Health <= 0 && victim.MaxHealth > 0 || GetDisposition( enemy ) != Sandbox.Npcs.Disposition.Hostile )
		{
			var start = Scene.Trace.Ray( EyePosition, EyePosition + aim.Forward * Scale ).IgnoreGameObjectHierarchy( GameObject )
				.WithoutTags( "trigger" ).UseDamageHitboxes().Run().EndPosition;
			enemy = FindPheromoneTarget( start );
			ActivePheromone = BabblerPheromone.Launch( this, start, aim );
		}
		else BabblerPheromone.Puff( hit.HitPosition );

		var heading = aim.Forward.WithZ( 0 );
		float length = heading.Length;
		var offset = length >= 1e-7f ? heading * (5 * Scale / length) : Vector3.Zero;
		DispatchBrood( enemy, WorldPosition + offset );
	}

	internal void DispatchBrood( GameObject enemy, Vector3 destination )
	{
		foreach ( var babbler in brood )
			if ( babbler.WorldPosition.DistanceSquared( WorldPosition ) <= 1000f * 1000 * Scale * Scale ) babbler.Dispatch( enemy, destination );
	}

	internal GameObject FindPheromoneTarget( Vector3 point )
	{
		var origin = WorldPosition;
		var direction = (point - origin).Normal;
		var center = (origin + point) * 0.5f;
		float radius = 15 * Scale + origin.Distance( center );
		float best = float.MaxValue;
		GameObject enemy = null;
		foreach ( var npc in Scene.GetAllComponents<Sandbox.Npcs.BaseNpc>() )
			if ( npc is Marine or Exo && npc.Health > 0 ) Consider( npc.GameObject );
		foreach ( var player in Scene.GetAllComponents<PlayerController>() )
			if ( Sandbox.Npcs.BaseNpc.IsLiveTarget( player.GameObject ) && !AlienTargeting.IsIgnoredPlayer( player.GameObject ) ) Consider( player.GameObject );
		return enemy;

		void Consider( GameObject candidate )
		{
			var position = candidate.WorldPosition;
			if ( position.DistanceSquared( center ) > radius * radius || GetDisposition( candidate ) != Sandbox.Npcs.Disposition.Hostile ) return;
			var delta = position - origin;
			float along = Vector3.Dot( delta, direction );
			float score = along + (delta - direction * along).Length * 2;
			var lift = Vector3.Up * (0.5f * Scale);
			if ( along <= 0 || score >= best || WallBetween( origin + lift, position + lift ) ) return;
			enemy = candidate;
			best = score;
		}
	}

	void UpdateBrood()
	{
		if ( nextHatch > 0 ) return;
		nextHatch = 2.5f;
		if ( brood.Count >= 6 || underFire > 0 ) return;
		var point = BabblerApproachPoint;
		if ( !point.IsValid() ) return;
		var pose = new Transform( point.WorldPosition - Vector3.Up * (0.25f * Scale), Rotation.Identity );
		var child = GameObject.Clone( PrefabFile.Load( "mount://ns2/entities/npcs/babbler.prefab" ), pose, startEnabled: false );
		if ( !child.IsValid() ) return;
		var babbler = child.GetComponent<Babbler>( true );
		babbler.BroodOwner = this;
		brood.Add( babbler );
		child.Enabled = true;
		if ( Networking.IsActive ) child.NetworkSpawn();
	}
}
