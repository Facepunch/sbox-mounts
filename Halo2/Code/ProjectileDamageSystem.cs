namespace Halo2;

public sealed class ProjectileDamageSystem( Scene scene ) : GameObjectSystem( scene ), Component.IDamageable.IEvents
{
	internal const string Headshot = "halo2_precision_hit";
	internal const string Direct = "halo2_direct_hit";
	internal const string Charged = "halo2_charged_hit";
	internal const string ProtectedBody = "halo2_protected_body";

	void Component.IDamageable.IEvents.OnDamaging( Component.IDamageable.DamageEvent hit )
	{
		if ( hit.Cancelled || hit.AcceptedDamage <= 0 || hit.DamageInfo?.Tags is not {} tags ) return;
		bool precision = tags.Has( Headshot );
		bool charged = tags.Has( Charged );
		if ( !precision && !charged && !tags.Has( Direct ) ) return;
		var vitality = CharacterVitality( hit.Victim, out var renderer );
		if ( hit.DamageInfo.Weapon?.GetComponent<Weapon>( true ) is not {} weapon )
		{
			// A projectile can outlive its weapon. Its hit already carries body-scaled damage in that case.
			if ( precision && vitality is not null && hit.AcceptedDamage > vitality.Armour ) hit.AcceptedDamage += vitality.Health;
			return;
		}
		var scales = charged && weapon is PlasmaPistol pistol ? pistol.ChargedShot?.BodyScales : weapon.PelletBodyScales;
		float bodyDamage = hit.AcceptedDamage;
		float shieldDamage = AbsorbShield( vitality, renderer, weapon.PlayerShieldMaterial, scales, ref bodyDamage );
		if ( tags.Has( ProtectedBody ) ) bodyDamage = 0;
		else if ( bodyDamage > 0 && scales is { Count: > 0 } )
		{
			foreach ( var tag in tags.TryGetAll() )
			{
				if ( !scales.TryGetValue( tag, out var scale ) ) continue;
				bodyDamage *= scale;
				break;
			}
		}
		if ( bodyDamage > 0 && vitality is not null && precision ) bodyDamage += vitality.Health;
		hit.AcceptedDamage = shieldDamage + bodyDamage;
	}

	internal static Component.IDamageable CharacterVitality( GameObject target, out ModelRenderer renderer )
	{
		var npc = target?.GetComponent<Sandbox.Npcs.BaseNpc>();
		if ( npc.IsValid() )
		{
			renderer = npc.Renderer;
			return npc;
		}
		var player = target?.GetComponent<PlayerController>();
		renderer = player?.Renderer;
		return player?.GetComponents<Component.IDamageable>().FirstOrDefault( x => x.Health > 0 );
	}

	internal static float AbsorbShield( Component.IDamageable vitality, ModelRenderer renderer, string defaultMaterial,
		Dictionary<string, float> scales, ref float damage )
	{
		if ( vitality?.Armour is not > 0 ) return 0;
		var unit = renderer?.Model?.GetData<UnitDefinition>();
		float multiplier = BodyMaterial.Scale( unit is null ? defaultMaterial : unit.ShieldMaterial, scales );
		float scaled = damage * multiplier;
		float absorbed = System.MathF.Min( vitality.Armour, scaled );
		damage = scaled > absorbed && multiplier > 0.0001f ? (scaled - absorbed) / multiplier : 0;
		return absorbed;
	}
}
