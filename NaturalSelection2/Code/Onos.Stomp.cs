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
		ShowStompCrack( center, waveDirection );
		foreach ( var obj in Scene.FindInPhysics( new Sphere( center, 2.2f * Scale ) ) )
		{
			if ( obj.GetComponentInParent<IDamageable>() is not Component victim || waveVictims.Contains( victim ) ||
				GetDisposition( victim.GameObject ) != Disposition.Hostile ) continue;
			var position = victim.WorldPosition;
			if ( Math.Abs( position.z - center.z ) >= 0.8f * Scale || position.DistanceSquared( center ) > 2.2f * 2.2f * Scale * Scale ) continue;
			var height = Vector3.Up * (0.4f * Scale);
			if ( TraceWorld( center + height, position + height ).Hit ) continue;
			waveVictims.Add( victim );
			var player = victim.GetComponent<PlayerController>();
			if ( player.IsValid() ) WebPlayer( player );
			DealDamage( (IDamageable)victim, 50, position, "stomp" );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowStompCrack( Vector3 position, Vector3 direction )
	{
		if ( !Graphics.IsAvailable ) return;
		var definition = GameResource.Load<DecalDefinition>( "mount://ns2/ns2/cinematics/vfx_materials/decals/shockwave_crack.material.vmat/projected.decal" );
		if ( definition?.ColorTexture is null ) return;

		var crack = new GameObject( false, "Stomp crack" )
		{
			NetworkMode = NetworkMode.Never,
			WorldPosition = position,
			WorldRotation = Rotation.LookAt( Vector3.Down, direction )
		};
		var decal = crack.AddComponent<Decal>();
		decal.Decals.Add( definition );
		decal.Size = 2.7f * Scale;
		decal.Rotation = Random.Shared.Next( 2 ) * 180;
		decal.LifeTime = 6;
		decal.Transient = true;
		crack.AddComponent<TemporaryEffect>();
		crack.Enabled = true;
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
