namespace Source1;

/// <summary>Applies the mounted model's damage modifiers before the prop loses health.</summary>
public sealed class PropDamage : Component
{
	Prop _prop;
	PropFire _fire;

	internal static void Attach( Prop prop )
	{
		if ( prop.Model?.GetData<PropDamageModifiers>() is not null )
			prop.GetOrAddComponent<PropDamage>();
	}

	protected override void OnEnabled()
	{
		_prop = GetComponent<Prop>();
		if ( _prop.IsValid() ) _prop.OnPropTakeDamage += OnDamage;
	}

	protected override void OnDisabled()
	{
		if ( _prop.IsValid() ) _prop.OnPropTakeDamage -= OnDamage;
	}

	void OnDamage( DamageInfo hit )
	{
		var bullet = hit.Tags.Has( DamageTags.Bullet );
		var blast = hit.Tags.Has( DamageTags.Explosion );
		if ( !(bullet || blast) || _prop.Model?.GetData<PropDamageModifiers>() is not { } modifiers ) return;

		var projectile = blast && hit.Weapon.IsValid() ? hit.Weapon.GetComponent<Projectile>() : null;
		var scale = bullet ? modifiers.Bullet * (hit.Tags.Has( "buckshot" ) ? 2 : 1) : 1;
		if ( blast && projectile is not BlastProjectile )
		{
			scale *= modifiers.Explosive;

			if ( projectile is { Radius: > 0 } and not Css.Grenade )
			{
				var bounds = _prop.Model.PhysicsBounds.Transform( WorldTransform );
				var testZ = projectile.UnderwaterBlast ? bounds.Mins.z : bounds.Maxs.z;
				if ( SourceWaterMap.IsUnderwater( Scene, bounds.Center.WithZ( testZ ) ) != projectile.UnderwaterBlast )
				{
					hit.Damage = 0;
					return;
				}

				// The blast trace already found the exposed surface of this prop.
				if ( GameObject == GameObject.Root )
				{
					var fraction = hit.Origin.Distance( hit.Position ) / projectile.Radius;
					hit.Damage = projectile.Damage * (1 - fraction.Clamp( 0, 1 ));
				}
			}
		}
		hit.Damage = System.MathF.Truncate( hit.Damage * scale );
		if ( blast && !_fire.IsValid() && hit.Damage > 0 && hit.Damage < _prop.Health && !modifiers.FireResistant
			&& projectile is not null and not Css.Grenade )
			_fire = PropFire.Ignite( _prop );
	}
}
