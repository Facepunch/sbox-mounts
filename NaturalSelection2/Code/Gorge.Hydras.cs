using System;

namespace NS2;

public sealed partial class Gorge
{
	readonly List<Hydra> planted = new( 3 );
	TimeUntil nextPlantCheck, dropCooldown;

	bool PlantHydra( Vector3? threat )
	{
		if ( nextPlantCheck > 0 ) return false;
		nextPlantCheck = 1f / 6;
		if ( energy < 30 || underFire > 0 || dropCooldown > 0 || GameObject.Tags.Has( "ns2_hallucination" ) ||
			threat is { } position && WorldPosition.Distance( position ) <= 25 * Scale ) return false;
		planted.RemoveAll( static h => !h.IsValid() || h.Health <= 0 );
		if ( planted.Count >= 3 ) return false;
		foreach ( var harvester in Scene.GetAllComponents<Harvester>() )
		{
			if ( harvester.Faction != Faction || harvester.Health <= 0 || !harvester.IsBuilt ||
				WorldPosition.Distance( harvester.WorldPosition ) > 8 * Scale ) continue;
			int count = 0;
			foreach ( var h in Scene.GetAllComponents<Hydra>() )
				if ( h.Health > 0 && h.WorldPosition.Distance( harvester.WorldPosition ) <= 10 * Scale && ++count == 3 ) break;
			if ( count >= 3 ) continue;

			firing = false;
			Animation.SetLookTarget( null );
			Movement.Stop();
			float angle = Random.Shared.NextSingle() * MathF.Tau;
			var floor = WorldPosition + new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0 ) * (2.5f * Scale);
			var direction = (floor - EyePosition).Normal;
			if ( HydraPlacement( direction ) is { } pose )
			{
				var child = GameObject.Clone( PrefabFile.Load( "mount://ns2/entities/npcs/hydra.prefab" ), pose );
				if ( !child.IsValid() ) return true;
				planted.Add( child.GetComponent<Hydra>() );
				if ( Networking.IsActive ) child.NetworkSpawn();
				energy -= 30;
				dropCooldown = 1;
				PlayCue( "create_structure_start", WorldPosition );
				ShowStructureSpit( EyePosition, Rotation.LookAt( direction ) );
			}
			return true;
		}
		return false;
	}

	Transform? HydraPlacement( Vector3 direction )
	{
		const float range = 6.5f * Scale;
		var end = EyePosition + direction * range;
		var ray = Scene.Trace.Ray( EyePosition, end ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "ns2_lifeform", "ns2_babbler", "player", "ragdoll", "trigger" );
		var hit = ray.Run();
		if ( !hit.Hit ) hit = ray.FromTo( end, end + Vector3.Down * range ).Run();
		if ( !hit.Hit || hit.StartedSolid || hit.GameObject is null || !hit.GameObject.Tags.Has( "world" ) ||
			hit.Surface?.ResourceName == "ns2_nocling" || Vector3.Dot( hit.Normal, EyePosition - hit.EndPosition ) < 0 ) return null;
		foreach ( var map in Scene.GetAllComponents<MapLocations>() )
			if ( map.BlocksStructure( hit.EndPosition ) ) return null;

		// The original Hydra ability checks a box with half-extents of 0.5 / 2.25 metres.
		float half = 0.5f * Scale / 2.25f;
		var n = hit.Normal;
		float clearance = half * (MathF.Abs( n.x ) + MathF.Abs( n.y ) + MathF.Abs( n.z ));
		var center = hit.EndPosition + n * clearance;
		if ( Scene.Trace.Box( Vector3.One * (2 * half), center + n * (0.15f * Scale), center + n * half )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "ns2_hydra", "trigger" ).Run().Hit ) return null;
		var face = MathF.Abs( Vector3.Dot( n, direction ) ) > 0.9f ? Vector3.Cross( n, MathF.Abs( n.z ) < 0.9f ? Vector3.Up : Vector3.Forward ) : direction;
		return new Transform( hit.EndPosition, Rotation.LookAt( face - n * Vector3.Dot( n, face ), n ) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ShowStructureSpit( Vector3 origin, Rotation facing ) =>
		CinematicLoader.Play( "cinematics/alien/gorge/spit_structure.cinematic", origin, facing );
}
