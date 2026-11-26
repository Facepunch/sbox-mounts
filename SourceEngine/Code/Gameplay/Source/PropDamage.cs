namespace Source1;

/// <summary>Applies the mounted model's bullet resistance before the prop loses health.</summary>
public sealed class PropDamage : Component
{
	Prop _prop;

	internal static void Attach( Prop prop )
	{
		if ( prop.Model?.GetData<PropDamageModifiers>() is { Bullet: not 1 } )
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
		if ( hit.Tags.Has( DamageTags.Bullet ) && _prop.Model?.GetData<PropDamageModifiers>() is { Bullet: not 1 } modifiers )
			hit.Damage = System.MathF.Truncate( hit.Damage * modifiers.Bullet );
	}
}
