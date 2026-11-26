namespace Halo2;

[Hide]
public sealed class WeaponScreen : Component
{
	[Property] public HandheldWeapon Weapon { get; set; }
	[Property] public bool UsesDigits { get; set; }
	[RequireComponent] public SkinnedModelRenderer Renderer { get; set; }
	float previous = float.NaN;

	internal static void Attach( HandheldWeapon weapon, SkinnedModelRenderer renderer )
	{
		foreach ( var material in renderer.Model.Materials )
		{
			var pass = (Blam.ShaderTag.EffectPass)material.GetFeature( "F_PASS" );
			if ( pass is not (Blam.ShaderTag.EffectPass.Meter or Blam.ShaderTag.EffectPass.AmmoDigit) ) continue;
			var screen = renderer.GameObject.AddComponent<WeaponScreen>();
			screen.Weapon = weapon;
			screen.UsesDigits = pass == Blam.ShaderTag.EffectPass.AmmoDigit;
			return;
		}
	}

	protected override void OnUpdate()
	{
		if ( !Weapon.IsValid() || !Renderer.Enabled ) return;
		float value = UsesDigits ? Weapon.Clip1 : Weapon is BatteryWeapon battery ? battery.Heat
			: Weapon.ClipMaxSize > 0 ? (float)Weapon.Clip1 / Weapon.ClipMaxSize : 0;
		if ( value == previous ) return;
		previous = value;
		if ( UsesDigits ) Renderer.Attributes.Set( "WeaponRounds", (int)value );
		else Renderer.Attributes.Set( "WeaponMeter", value );
	}
}
