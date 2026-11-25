using Sandbox.Npcs;

namespace NS2;

internal static class AlienFootsteps
{
	internal static void Register( MountContext context, string lifeform )
	{
		foreach ( var surface in new[] { "metal", "thin_metal", "organic", "rock" } )
		{
			Add( context, $"materials/{surface}/{lifeform}_step" );
			Add( context, $"materials/{surface}/{lifeform}_step_for_enemy" );
		}

		static void Add( MountContext mount, string id ) => mount.Add( ResourceType.GameResource, $"sounds/{id}.sound", new SparkSoundLoader( id ) );
	}

	internal static void OnTag( BaseNpc npc, string lifeform, string tag )
	{
		if ( tag is not ("step" or "step_run" or "step_sprint") || !npc.Animation.IsGrounded || npc.Health <= 0 ||
			npc.Animation.MoveVelocity.LengthSquared <= 0.75f * 0.75f * MapLoader.Scale * MapLoader.Scale ) return;
		var surface = npc.GroundSurface?.ResourceName switch
		{
			"ns2_rock" => "rock",
			"ns2_thin_metal" or "ns2_light_metal" => "thin_metal",
			"ns2_organic" or "ns2_infestation" => "organic",
			_ => "metal"
		};
		Play( npc, $"materials/{surface}/{lifeform}_step", npc.WorldPosition );
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	static void Play( BaseNpc npc, string cue, Vector3 position )
	{
		if ( !npc.IsValid() || Application.IsHeadless ) return;
		if ( PlayerGait.IsEnemyListener( npc ) ) cue += "_for_enemy";
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{cue}.sound" ) is { } sound ) Sound.Play( sound, position );
	}
}
