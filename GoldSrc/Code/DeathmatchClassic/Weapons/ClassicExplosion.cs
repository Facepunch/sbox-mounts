using GoldSrc.HalfLife;

namespace GoldSrc.DeathmatchClassic;

[Library]
internal static class ClassicExplosion
{
	internal static DeathmatchClassicMount Mount => Sandbox.Mounting.Directory.Get( "dmc" ) as DeathmatchClassicMount;

	internal static void Damage( GameObject source, GameObject attacker, Vector3 origin, float amount, Component.IDamageable ignore = null )
	{
		var victims = source.Scene.FindInPhysics( new Sphere( origin, amount + 40 ) )
			.Select( obj => obj.GetComponentInParent<Component.IDamageable>() ).OfType<Component>().Distinct().ToArray();
		foreach ( var victim in victims )
		{
			if ( !victim.IsValid() || victim == ignore ) continue;
			var center = Npc.TargetHullCenter( victim.GameObject );
			var points = amount - origin.Distance( center ) * 0.5f;
			if ( attacker.IsValid() && victim.GameObject.Root == attacker.Root ) points *= 0.5f;
			if ( points <= 0 ) continue;
			var hit = source.Scene.Trace.Ray( origin, center ).IgnoreGameObjectHierarchy( source ).WithoutTags( "trigger" ).Run();
			if ( hit.Hit && hit.GameObject?.Root != victim.GameObject.Root ) continue;
			var damage = new DamageInfo { Damage = points, Origin = origin, Position = center, Attacker = attacker.IsValid() ? attacker : source, Weapon = source };
			damage.Tags.Add( "blast" );
			((Component.IDamageable)victim).OnDamage( damage );
			if ( victim.IsValid() && victim.GameObject.GetComponent<PlayerController>() is { } player && player.Body.IsValid() )
				player.Body.Velocity += (center - origin).Normal * (points * 8);
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void Show( Vector3 origin )
	{
		if ( Application.IsDedicatedServer || Mount is not { } mount ) return;
		NpcAudio.Play( mount.NpcAudio.Find( "weapons/r_exp3.wav" ), origin, 1, 1 );
		if ( mount.GetIndexedSprite( "zerogxplode" ) is not { } sprite ) return;
		var go = new GameObject( false, "Explosion" ) { NetworkMode = NetworkMode.Never, WorldPosition = origin };
		var renderer = go.AddComponent<SpriteRenderer>();
		renderer.Sprite = sprite;
		renderer.Additive = true;
		renderer.Size = 96;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = sprite.Animations[0].Frames.Count / 15f;
		go.Enabled = true;
	}
}
