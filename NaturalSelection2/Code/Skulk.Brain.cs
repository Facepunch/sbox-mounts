using System;
using Sandbox.Npcs;

namespace NS2;

public sealed partial class Skulk
{
	readonly AlienTargeting targeting = new();
	readonly AlienAim.Acquisition acquisition = new( 105 );
	GameObject sighted;
	TimeUntil nextJuke, repath;
	float jukeSide = 1;
	TimeUntil dealtDamage;
	TimeSince sinceFought = 10;
	bool sneaky, sneaking;
	Stimulus? lastHeard;
	MapLocations locations;
	float AimTurnRate => acquisition.TurnRate;
	[Property, Range( 1, 7 )] public int AimSkillTier { get; set; } = 1;

	bool TrackAim( GameObject target, Vector3 aimPoint ) =>
		acquisition.TrackView( target, EyePosition, aimPoint, ViewRotation, underFire > 0 || dealtDamage > 0 ? 1.1f : 1, AimSkillTier );

	GameObject FindEnemy( out Vector3? lastKnownPosition )
	{
		if ( Senses.Disturbance is { Kind: StimulusKinds.Gunshot } shot && shot != lastHeard )
		{
			lastHeard = shot;
			if ( shot.Source.IsValid() && GetDisposition( shot.Source ) == Disposition.Hostile &&
				WorldPosition.Distance( shot.Position ) <= 14 * MapLoader.Scale )
				targeting.Remember( shot.Source, shot.Position );
		}
		var enemy = targeting.FindEnemy( this, out lastKnownPosition );
		if ( lastKnownPosition.HasValue ) enemy = null;
		if ( enemy.IsValid() )
		{
			sneaking = false;
			sinceFought = 0;
		}
		if ( sighted != enemy )
		{
			sighted = enemy;
			repath = 0;
		}
		return enemy;
	}

	void Investigate( Vector3? lastKnownPosition )
	{
		foreach ( var mark in Scene.GetAllComponents<Parasited>() )
		{
			if ( AlienTargeting.IsIgnoredPlayer( mark.GameObject ) || GetDisposition( mark.GameObject ) != Disposition.Hostile ) continue;
			sneaking = false;
			Pursue( mark.WorldPosition );
			return;
		}
		if ( lastKnownPosition is { } position )
		{
			float distance = WorldPosition.Distance( position ) / MapLoader.Scale;
			sneaking = sneaky && sinceFought >= 10 && distance is > 4 and < 20 && !GetComponent<Parasited>().IsValid();
			Pursue( position );
			return;
		}
		sneaking = false;
		GameObject.GetOrAddComponent<AlienExploration>().Explore( Reach * 0.6f );
	}

	void Pursue( Vector3 point )
	{
		if ( repath > 0 ) return;
		Movement.MoveTo( point, Reach * 0.6f, CurrentMoveSpeed );
		repath = 0.2f;
	}

	bool UpdateTravel()
	{
		if ( mover.MoveTarget is not { } goal ) return false;
		var origin = sighted.IsValid() ? EyePosition : WorldPosition;
		var offset = goal - origin;
		float distance = offset.Length;
		if ( distance > 2.5f * MapLoader.Scale && Math.Abs( offset.z ) * 1.5f > distance )
			Animation.SetLookTarget( goal + Vector3.Up * (Math.Sign( offset.z ) * distance) );

		if ( !locations.IsValid() ) locations = Scene.GetAllComponents<MapLocations>().FirstOrDefault();
		var next = mover.NextCorner ?? goal;
		mover.NearVent = locations.IsValid() && (locations.IsVent( WorldPosition ) ||
			WorldPosition.DistanceSquared( next ) <= 25 * MapLoader.Scale * MapLoader.Scale && locations.IsVent( next ));
		if ( !LeapUnlocked || energy <= 80 || sneaking || mover.NearVent || distance <= 3 * MapLoader.Scale ||
			Vector3.Dot( GetComponent<Rigidbody>().Velocity.Normal, offset.Normal ) <= 0.6f ) return false;
		Animation.SetLookTarget( EyePosition + (ViewRotation.Forward + Vector3.Up * 0.8f) * MapLoader.Scale );
		return true;
	}

	void ApproachEnemy( GameObject target, Vector3 aimPoint, float distance )
	{
		if ( repath > 0 ) return;
		float scale = MapLoader.Scale;
		if ( distance >= 45 * scale ||
			(!target.GetComponent<PlayerController>().IsValid() && !target.GetComponent<BaseNpc>().IsValid()) )
		{
			Pursue( target.WorldPosition );
			return;
		}

		var velocity = target.GetComponent<Rigidbody>()?.Velocity ?? Vector3.Zero;
		var lead = velocity.Length > 4 * scale ? velocity * (0.6f * Math.Min( distance / scale, 1 )) : Vector3.Zero;
		if ( distance <= 5 * scale )
		{
			Pursue( target.WorldPosition + lead );
			return;
		}
		if ( nextJuke <= 0 )
		{
			jukeSide = -Math.Sign( jukeSide ) * Random.Shared.Float( 0.3f, 1 );
			nextJuke = Random.Shared.Float( 0.35f, 1 );
		}

		var side = Vector3.Cross( aimPoint + lead - EyePosition, Vector3.Up ).Normal;
		var offset = aimPoint + lead + side * (jukeSide * Math.Clamp( distance / 2.5f, 3 * scale, 7 * scale ));
		var center = Scene.NavMesh.GetClosestPoint( aimPoint );
		var flank = Scene.NavMesh.GetClosestPoint( offset );
		if ( center.HasValue && flank.HasValue && center.Value.Distance( aimPoint ) * 1.5f < flank.Value.Distance( aimPoint ) )
			Pursue( flank.Value );
		else
			Pursue( target.WorldPosition + lead );
	}
}
