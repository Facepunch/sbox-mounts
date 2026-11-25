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
		waveDirection = ViewRotation.Forward.WithZ( 0 ).Normal;
		waveOrigin = WorldPosition + Vector3.Up * (0.2f * Scale) + waveDirection * (0.4f * Scale);
		waveAge = 0;
		nextPulse = 0.1f;
		(waveVictims ??= []).Clear();
		var origin = WorldPosition + Vector3.Up * (0.4f * Scale);
		foreach ( var obj in Scene.FindInPhysics( new Sphere( origin, 4 * Scale ) ) )
			BlastProp( obj, origin, 4 * Scale );
		PlayCue( "stomp" );
		ShowStompLaunch( WorldPosition, WorldRotation );
	}

	void UpdateStompWave()
	{
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
		// The visible wave reaches floors below gaps that are too deep to step down.
		if ( !floor.Hit ) floor = TraceWorld( waveOrigin, waveOrigin - Vector3.Up * (3 * Scale) );
		if ( !floor.Hit ) return;
		var center = floor.EndPosition;
		ShowStompCrack( center, waveDirection );
		if ( waveOrigin.z - center.z > Scale ) return;
		foreach ( var obj in Scene.FindInPhysics( new Sphere( center, 2.2f * Scale ) ) )
		{
			BlastProp( obj, center + Vector3.Up * (0.4f * Scale), 2.2f * Scale );
			if ( obj.GetComponentInParent<IDamageable>() is not Component victim || waveVictims.Contains( victim ) ||
				GetDisposition( victim.GameObject ) != Disposition.Hostile ) continue;
			var position = victim.WorldPosition;
			if ( Math.Abs( position.z - center.z ) >= 0.8f * Scale || position.DistanceSquared( center ) > 2.2f * 2.2f * Scale * Scale ) continue;
			var height = Vector3.Up * (0.4f * Scale);
			if ( TraceWorld( center + height, position + height ).Hit ) continue;
			waveVictims.Add( victim );
			ShowStompImpact( position, victim.WorldRotation );
			var player = victim.GetComponent<PlayerController>();
			if ( player.IsValid() ) WebPlayer( player );
			DealDamage( (IDamageable)victim, 50, position, "stomp" );
		}
	}

	void BlastProp( GameObject obj, Vector3 origin, float radius )
	{
		var body = obj.GetComponentInParent<Rigidbody>();
		if ( !IsLooseProp( body ) || waveVictims.Contains( body ) ) return;
		var point = body.FindClosestPoint( origin );
		float strength = 1 - point.Distance( origin ) / radius;
		if ( strength <= 0 || !CanReachProp( origin, point, body.GameObject ) ) return;
		var outward = (body.PhysicsBody.MassCenter - origin).WithZ( 0 ).Normal;
		var launch = (outward + Vector3.Up * 0.7f).Normal * (16 * Scale * strength);
		waveVictims.Add( body );
		PushProp( body, point, launch );
	}

	static void PlayStompEffect( string name, Vector3 position, Rotation facing )
	{
		if ( Graphics.IsAvailable && Sandbox.Mounting.Directory.Get( "ns2" ) is GameMount mount )
			mount.PlayCinematic( $"cinematics/alien/onos/{name}.cinematic", position, facing );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowStompLaunch( Vector3 position, Rotation facing )
	{
		if ( !Graphics.IsAvailable ) return;
		CameraEffectSystem.Get( Scene ).AddShake( position, 25 * Scale, 12, 24, 0.45f );
		PlayStompEffect( "stomp_trimmed", position, facing );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowStompCrack( Vector3 position, Vector3 direction )
	{
		PlayStompEffect( "shockwave_segment", position, Rotation.LookAt( direction ) );
		SparkDecal.Spawn( "Stomp crack", "shockwave_crack", position, Rotation.LookAt( Vector3.Down, direction ),
			Random.Shared.Next( 2 ) * 180, 2.7f, 6 );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowStompImpact( Vector3 position, Rotation facing )
	{
		if ( !Graphics.IsAvailable ) return;
		PlayStompEffect( "shockwave_hit", position, facing );

		// Shared decals use the victim's up axis and the client's default twelve-second lifetime.
		var tangent = new Vector3( Random.Shared.Float( -1, 1 ), Random.Shared.Float( -0.9f, 1.1f ), Random.Shared.Float( -1.1f, 0.9f ) );
		SparkDecal.Spawn( "Stomp impact", "shockwave_hit", position, Rotation.LookAt( -facing.Up, tangent ), 0, 2, 12 );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void WebPlayer( PlayerController player )
	{
		if ( player.IsValid() && !player.IsProxy )
			player.GameObject.GetOrAddComponent<OnosWebbed>().Apply( player );
	}
	SceneTraceResult TraceWorld( Vector3 from, Vector3 to ) => Scene.Trace.Ray( from, to )
		.WithoutTags( "trigger", "npc", "player", "playercontroller" ).IgnoreGameObjectHierarchy( GameObject ).Run();
}
