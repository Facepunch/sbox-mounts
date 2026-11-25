using IDamageable = Sandbox.Component.IDamageable;
using Sandbox.Npcs;

namespace NS2;

internal static class AlienMelee
{
	internal static void PlayImpact( SceneTraceResult hit, bool gore )
	{
		if ( !hit.Hit ) return;
		var player = hit.GameObject?.GetComponentInParent<PlayerController>();
		var surface = hit.Surface?.ResourceName;
		if ( surface?.StartsWith( "ns2_" ) == true ) surface = surface[4..];
		else if ( player.IsValid() ) surface = "flesh";
		surface = surface switch
		{
			"light_metal" => "thin_metal",
			"electric" => "electronic",
			"door" or "electronic" or "thin_metal" or "rock" or "armor" or "flesh" or "membrane" or "glass" or "organic" or "infestation" or "robot" or "flame" => surface,
			_ => "metal"
		};
		var victim = hit.GameObject?.GetComponentInParent<IDamageable>() as Component;
		ShowImpact( hit.EndPosition, (hit.EndPosition - hit.StartPosition).Normal, surface, gore, victim?.GameObject );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ShowImpact( Vector3 position, Vector3 direction, string surface, bool gore, GameObject victim )
	{
		var soundPath = gore ? surface switch
		{
			"armor" or "flesh" => "alien/onos/gore_hit_marine",
			"glass" or "electronic" or "thin_metal" => "alien/onos/gore_hit_thin_metal",
			"organic" or "infestation" => "alien/onos/gore_hit_organic",
			"metal" or "door" => $"alien/onos/gore_hit_{surface}",
			"rock" or "membrane" => $"surfaces/materials/{surface}/ricochet",
			_ => "surfaces/materials/organic/ricochet"
		} : surface switch
		{
			"armor" or "flesh" => "alien/skulk/bite_hit_marine",
			"organic" or "infestation" or "membrane" => "alien/skulk/bite_hit_organic",
			"rock" or "thin_metal" => $"alien/skulk/bite_hit_{surface}",
			_ => "alien/skulk/bite_hit_metal"
		};
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{soundPath}.sound" ) is { } sound ) Sound.Play( sound, position );
		if ( !Graphics.IsAvailable ) return;
		var player = victim?.GetComponentInParent<PlayerController>();
		if ( !(player.IsValid() && !player.IsProxy) && Sandbox.Mounting.Directory.Get( "ns2" ) is GameMount mount )
			mount.PlayCinematic( $"cinematics/materials/{surface}/{(gore ? "gore" : "bite")}.cinematic", position, Rotation.Identity );

		var ray = Game.ActiveScene.Trace.Ray( position, position + direction * (3.5f * MapLoader.Scale) ).WithoutTags( "trigger" );
		if ( victim.IsValid() ) ray = ray.IgnoreGameObjectHierarchy( victim );
		var wall = ray.Run();
		if ( !wall.Hit || wall.StartedSolid ) return;
		var (material, size) = surface switch
		{
			"flesh" => ($"marine_blood_0{Game.Random.Next( 1, 5 )}", 2f),
			"robot" => ($"robot_blood_0{Game.Random.Next( 1, 5 )}", 2f),
			"organic" => ($"alien_blood_0{Game.Random.Next( 1, 5 )}", 0.75f),
			"membrane" => ("alien_blood_02", 0.75f),
			_ => gore ? ("bash_01", 1f) : ("bite_02", 0.5f)
		};
		SparkDecal.Spawn( "Melee impact", material, wall.HitPosition, Rotation.LookAt( -wall.Normal, direction ), 0, size, 12 );
	}

	// Spark samples the center, four sides and four corners of the weapon's melee box.
	internal static (SceneTraceResult Hit, IDamageable Victim) Trace( BaseNpc npc, Rotation view, float reach, float width, float height )
	{
		width *= MapLoader.Scale / 3;
		height *= MapLoader.Scale / 3;
		var size = new Vector3( width, width, height );
		System.ReadOnlySpan<int> order = [4, 1, 3, 5, 7, 0, 2, 6, 8];
		SceneTraceResult hit = default, center = default;
		IDamageable victim = null;
		var forward = view.Forward.WithZ( 0 ).Normal;
		foreach ( var cell in order )
		{
			var start = npc.EyePosition + view.Left * ((cell % 3 - 1) * width) + view.Up * ((cell / 3 - 1) * height);
			var trace = npc.Scene.Trace.Ray( start, start + view.Forward * reach ).Size( size )
				.IgnoreGameObjectHierarchy( npc.GameObject ).UseHitboxes().Run();
			if ( cell == 4 ) center = trace;
			if ( trace.GameObject?.GetComponentInParent<IDamageable>() is not { } target || npc.GetDisposition( (target as Component)?.GameObject ?? trace.GameObject ) != Disposition.Hostile ) continue;
			if ( Vector3.Dot( trace.EndPosition - npc.EyePosition, forward ) <= 0.1f * MapLoader.Scale ) continue;
			bool player = trace.GameObject.GetComponentInParent<PlayerController>() is not null;
			if ( victim is null || player ) (hit, victim) = (trace, target);
			if ( player ) break;
		}
		if ( victim is null )
		{
			hit = center;
			if ( hit.GameObject?.GetComponentInParent<IDamageable>() is Component candidate && npc.GetDisposition( candidate.GameObject ) == Disposition.Hostile )
				victim = (IDamageable)candidate;
		}
		return (hit, victim);
	}
}
