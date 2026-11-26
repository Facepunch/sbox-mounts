namespace Source1;

/// <summary>Applies the mounted model's damage modifiers before the prop loses health.</summary>
public sealed class PropDamage : Component
{
	Prop _prop;

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

		var scale = bullet ? modifiers.Bullet * (hit.Tags.Has( "buckshot" ) ? 2 : 1) : 1;
		if ( blast ) scale *= modifiers.Explosive;
		hit.Damage = System.MathF.Truncate( hit.Damage * scale );
	}
}
