namespace GoldSrc.HalfLife;

[Library]
internal static partial class Blast
{
	internal static void DamageInRadius( GameObject inflictor, GameObject attacker, Vector3 origin, float damage, float radius, params System.ReadOnlySpan<string> tags )
		=> DamageInRadius( inflictor, attacker, origin, damage, radius, damage, tags );

	internal static void DamageInRadius( GameObject inflictor, GameObject attacker, Vector3 origin, float damage, float radius, float deathmatchDamage, params System.ReadOnlySpan<string> tags )
	{
		var scene = inflictor.Scene;
		scene.GetSystem<Sandbox.Npcs.NpcSystem>()?.EmitStimulus( "explosion", origin, inflictor, 1024, 1 );
		var submerged = WaterBody.Find( scene, origin ).IsValid();
		origin += Vector3.Up;
		var victims = scene.FindInPhysics( new Sphere( origin, radius ) )
			.Select( obj => obj.GetComponentInParent<Component.IDamageable>() ).OfType<Component>().Distinct().ToArray();
		foreach ( var victim in victims )
		{
			if ( !victim.IsValid() || !Npc.SharesWaterLevel( victim.GameObject, submerged ) ) continue;
			var target = Npc.TargetBodyPosition( victim.GameObject );
			var hit = scene.Trace.Ray( origin, target ).IgnoreGameObjectHierarchy( inflictor ).WithoutTags( "debris" ).Run();
			if ( hit.Hit && hit.GameObject?.Root != victim.GameObject.Root ) continue;
			var amount = WeaponDamage.ForTarget( attacker, victim.GameObject, damage, deathmatchDamage )
				* (1 - origin.Distance( hit.EndPosition ) / radius);
			if ( amount <= 0 ) continue;
			var info = new DamageInfo { Attacker = attacker, Weapon = inflictor, Damage = amount, Origin = origin, Position = hit.EndPosition };
			foreach ( var tag in tags ) info.Tags.Add( tag );
			((Component.IDamageable)victim).OnDamage( info );
		}
	}
}
