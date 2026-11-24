using System;
using Sandbox.Npcs;

namespace NS2;

public sealed partial class Onos
{
	Vector3 waveOrigin, waveDirection;
	TimeSince waveAge;
	TimeUntil nextPulse;
	HashSet<Component> waveVictims;

	void StartWave()
	{
		stompHit = true;
		waveDirection = WorldRotation.Forward.WithZ( 0 ).Normal;
		waveOrigin = WorldPosition + Vector3.Up * (0.2f * Scale) + waveDirection * (0.4f * Scale);
		waveAge = 0;
		nextPulse = 0.1f;
		(waveVictims ??= []).Clear();
		PlayCue( "stomp" );
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( !Networking.IsHost || waveDirection == Vector3.Zero ) return;
		if ( waveAge >= 0.7f )
		{
			waveDirection = Vector3.Zero;
			return;
		}
		var next = waveOrigin + waveDirection * (24 * Scale * Time.Delta);
		var floor = TraceWorld( next + Vector3.Up * (0.8f * Scale), next - Vector3.Up * (1.5f * Scale) );
		if ( floor.Hit ) next.z = floor.EndPosition.z + 0.2f * Scale;
		var lift = Vector3.Up * (0.3f * Scale);
		if ( floor.StartedSolid || TraceWorld( waveOrigin + lift, next + lift ).Hit )
		{
			waveDirection = Vector3.Zero;
			return;
		}
		waveOrigin = next;
		if ( nextPulse > 0 ) return;
		nextPulse = 0.1f;
		if ( !floor.Hit ) return;
		var center = floor.EndPosition;
		foreach ( var obj in Scene.FindInPhysics( new Sphere( center, 2.2f * Scale ) ) )
		{
			if ( obj.GetComponentInParent<IDamageable>() is not Component victim || waveVictims.Contains( victim ) ||
				GetDisposition( victim.GameObject ) != Disposition.Hostile ) continue;
			var position = victim.WorldPosition;
			if ( Math.Abs( position.z - center.z ) >= 0.8f * Scale || position.DistanceSquared( center ) > 2.2f * 2.2f * Scale * Scale ) continue;
			var height = Vector3.Up * (0.4f * Scale);
			if ( TraceWorld( center + height, position + height ).Hit ) continue;
			waveVictims.Add( victim );
			DealDamage( (IDamageable)victim, 50, position, "stomp" );
		}
	}

	SceneTraceResult TraceWorld( Vector3 from, Vector3 to ) => Scene.Trace.Ray( from, to )
		.WithoutTags( "trigger", "npc", "player", "playercontroller" ).IgnoreGameObjectHierarchy( GameObject ).Run();
}
