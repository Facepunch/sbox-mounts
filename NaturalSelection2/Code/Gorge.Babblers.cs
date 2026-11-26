using System;

namespace NS2;

public sealed partial class Gorge
{
	// Gorge base health is 160. NS2 rounds 13% to 21 before dividing it between the six attachment points.
	const float ShieldPerBabbler = 3.5f;
	static readonly string[] Sockets = ["babbler_attach1", "babbler_attach2", "babbler_attach3", "babbler_attach4", "babbler_attach5", "babbler_attach6"];
	readonly List<Babbler> brood = new( 6 );
	readonly List<int> freeSlots = [1, 2, 3, 4, 5, 6];
	internal GameObject BabblerApproachPoint => freeSlots.Count > 0 ? Renderer.GetAttachmentObject( Sockets[freeSlots[0] - 1] ) : null;
	TimeUntil nextHatch;
	float babblerShield;

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

	void ThrowBabblers()
	{
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
			var size = new Vector3( forward.WithX( 0 ).Length, forward.WithY( 0 ).Length, forward.WithZ( 0 ).Length ) * (2 * 0.315f * Scale);
			hit = Scene.Trace.Ray( EyePosition, hit.Hit ? hit.EndPosition : end ).Size( size )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ns2_babbler" ).Run();
		}
		var enemy = (hit.GameObject?.GetComponentInParent<IDamageable>() as Component)?.GameObject;
		if ( !enemy.IsValid() || AlienTargeting.IsIgnoredPlayer( enemy ) || GetDisposition( enemy ) != Sandbox.Npcs.Disposition.Hostile )
		{
			enemy = null;
			var origin = WorldPosition;
			var start = EyePosition + aim.Forward * Scale;
			var dir = (start - origin).Normal;
			float best = float.MaxValue;
			foreach ( var obj in Senses.VisibleHostiles )
			{
				if ( !IsFleeTarget( obj ) || AlienTargeting.IsIgnoredPlayer( obj ) ) continue;
				var delta = obj.WorldPosition - origin;
				if ( obj.WorldPosition.Distance( (origin + start) * 0.5f ) > 15 * Scale + origin.Distance( start ) * 0.5f ) continue;
				float along = Vector3.Dot( delta, dir );
				float score = along + (delta - dir * along).Length * 2;
				if ( along <= 0 || score >= best || WallBetween( origin + Vector3.Up * (0.5f * Scale), obj.WorldPosition + Vector3.Up * (0.5f * Scale) ) ) continue;
				enemy = obj;
				best = score;
			}
		}
		foreach ( var babbler in brood )
			if ( babbler.WorldPosition.DistanceSquared( WorldPosition ) <= 1000f * 1000 * Scale * Scale ) babbler.Dispatch( enemy );
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
		babbler.OwnerGorge = this;
		brood.Add( babbler );
		child.Enabled = true;
		if ( Networking.IsActive ) child.NetworkSpawn();
	}

	internal void AttachHatchling( Babbler babbler )
	{
		if ( freeSlots.Count == 0 ) return;
		int slot = freeSlots[^1];
		var point = Renderer.GetAttachmentObject( Sockets[slot - 1] );
		if ( !point.IsValid() ) return;
		freeSlots.RemoveAt( freeSlots.Count - 1 );
		babbler.ClingTo( point, slot );
		babblerShield += ShieldPerBabbler;
	}

	internal void BabblerRemoved( Babbler babbler )
	{
		if ( !brood.Remove( babbler ) || babbler.ClingSlot == 0 ) return;
		ReleaseSlot( babbler.ClingSlot );
	}

	internal void ReleaseSlot( int slot )
	{
		freeSlots.Add( slot );
		babblerShield = Math.Min( babblerShield, (6 - freeSlots.Count) * ShieldPerBabbler );
	}

	protected override void AbsorbBabblerShield( IDamageable.DamageEvent e )
	{
		if ( babblerShield <= 0 || e.AcceptedDamage <= 0 ||
			e.DamageInfo.Tags.HasAny( "nervegas", "corrode", "armoronly", "structuresonly", "splash" ) ) return;
		float absorbed = Math.Min( babblerShield, e.AcceptedDamage );
		e.AcceptedDamage -= absorbed;
		babblerShield -= absorbed;
		underFire = 3;
		while ( freeSlots.Count < 6 && babblerShield <= (5 - freeSlots.Count) * ShieldPerBabbler )
			BreakBabbler( brood.Find( b => b.ClingSlot != 0 ) );
	}

	void BreakBabbler( Babbler babbler )
	{
		BabblerRemoved( babbler );
		babbler.Kill();
	}
}
