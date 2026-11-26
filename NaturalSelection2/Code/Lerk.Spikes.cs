using Sandbox.Npcs;
using System;

namespace NS2;

public sealed partial class Lerk
{
	const float SpikeCost = 1.4f;
	bool shootLeft, firingSpikes;
	TimeUntil nextSpikeSound;

	internal static bool IsPlayerTarget( GameObject target ) => target.Tags.HasAny( "ns2_marine", "ns2_exo", "ns2_lifeform" ) ||
		target.GetComponentInParent<PlayerController>().IsValid();

	static Vector3 SpikeDirection( Rotation aim, float angleSample, float radiusSample )
	{
		float angle = angleSample * MathF.Tau;
		float radius = radiusSample * MathF.Tan( 1.65f.DegreeToRadian() );
		return (aim.Forward + (aim.Left * MathF.Cos( angle ) + aim.Up * MathF.Sin( angle )) * radius).Normal;
	}

	void FireSpike()
	{
		energy -= SpikeCost;
		firingSpikes = true;
		var direction = SpikeDirection( View, Random.Shared.NextSingle(), Random.Shared.NextSingle() );
		var origin = EyePosition;
		var end = origin + direction * (41 * Scale);
		var trace = Scene.Trace.Ray( origin, end ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ns2_babbler" ).UseHitboxes();
		bool tracer = Random.Shared.NextSingle() < 0.75f;
		for ( int i = 0; i < 20; i++ )
		{
			var hit = trace.Run();
			if ( !hit.Hit || hit.GameObject?.Tags.Has( "world" ) == true )
			{
				// Spark retries a world hit or miss with a box perpendicular to the spike.
				var d = direction;
				var extents = new Vector3( d.WithX( 0 ).Length, d.WithY( 0 ).Length, d.WithZ( 0 ).Length ) * (0.045f * Scale);
				hit = trace.FromTo( origin, hit.EndPosition ).Size( -extents, extents ).Run();
			}
			var position = hit.EndPosition - direction * (0.13f * Scale);
			var hp = hit.GameObject?.GetComponentInParent<IDamageable>();
			var corpse = hit.GameObject?.GetComponentInParent<NpcCorpse>();
			var obj = (hp as Component)?.GameObject ?? corpse?.GameObject ?? hit.GameObject;
			bool pass = corpse.IsValid() || hp is { MaxHealth: > 0, Health: <= 0 } ||
				obj?.Tags.HasAny( "ns2_mac", "ns2_drifter", "ns2_hydra", "ns2_hallucination" ) == true;
			if ( pass ) trace = trace.IgnoreGameObjectHierarchy( obj );
			if ( hp is not null && GetDisposition( obj ) == Disposition.Hostile )
			{
				float damage = hp is not (AlienLifeform or Marine or Exo) && (IsPlayerTarget( obj ) || obj.Tags.Has( "ns2_exosuit" )) ? 10 : 5;
				var info = new DamageInfo { Damage = damage, Origin = origin, Position = position, Attacker = GameObject, Weapon = GameObject };
				info.Tags.Add( "puncture" );
				hp.OnDamage( info );
			}
			if ( tracer && (i == 0 || hit.Hit && obj.IsValid() && !obj.Tags.Has( "world" )) ) TraceSpike( position );
			if ( !pass ) break;
		}
	}

	void TraceSpike( Vector3 end )
	{
		var start = EyePosition + (View.Forward * 2 - View.Up * 0.1f + View.Left * (shootLeft ? -0.4f : 0.4f)) * Scale;
		shootLeft = !shootLeft;
		TraceEffect( start, end );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void TraceEffect( Vector3 start, Vector3 end ) => ShowSpike( start, end );

	internal static void ShowSpike( Vector3 start, Vector3 end )
	{
		if ( !Graphics.IsAvailable || start.DistanceSquared( end ) < 0.001f ) return;
		var tracer = new GameObject( false, "Spike tracer" ) { NetworkMode = NetworkMode.Never, WorldPosition = start, WorldRotation = Rotation.LookAt( end - start ) };
		tracer.AddComponent<LerkSpikeTracer>().End = end;
		tracer.Enabled = true;
		CinematicLoader.Play( "cinematics/alien/tracer.cinematic", start, tracer.WorldRotation, tracer );
	}
}

public sealed class LerkSpikeTracer : Component
{
	internal Vector3 End;
	Vector3 lastResidue;

	protected override void OnStart() => lastResidue = WorldPosition;

	protected override void OnUpdate()
	{
		float speed = 115 * MapLoader.Scale;
		float remaining = WorldPosition.Distance( End ) - speed * Time.Delta;
		if ( remaining <= 0 ) { GameObject.Destroy(); return; }
		WorldPosition = End - WorldRotation.Forward * remaining;
		if ( remaining > speed * 0.005f && lastResidue.DistanceSquared( WorldPosition ) > (0.75f * MapLoader.Scale) * (0.75f * MapLoader.Scale) )
		{
			lastResidue = WorldPosition;
			CinematicLoader.Play( "cinematics/alien/tracer_residue.cinematic", WorldPosition, WorldRotation );
		}
	}
}
