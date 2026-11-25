using Sandbox.Npcs;

namespace Diesel;

static class PaydayPerception
{
	/// <summary>
	/// Holding a drawn, threatening weapon. The sandbox's Physgun, Toolgun and Camera are <see cref="BaseCombatWeapon"/>s
	/// too (via BaseSandboxWeapon/ScreenWeapon), so any held combat weapon used to count. Those tools all set
	/// <c>UsesAmmo = false</c>; firearms, launchers and grenades (including all PAYDAY guns) use ammo. Melee (fists, crowbar)
	/// is also ammo-less and so treated as unthreatening, which matches PDTH civilians reacting to guns.
	/// </summary>
	public static bool Armed( PlayerController player ) =>
		player.Active && player.GetComponent<BaseInventoryComponent>()?.ActiveItem is BaseCombatWeapon { IsHeld: true, IsActive: true, UsesAmmo: true };

	public static bool CanSee( BaseNpc observer, GameObject target, Vector3 point )
	{
		var delta = point - observer.EyePosition;
		if ( delta.Length > (observer.Senses?.SightRange ?? 1600) ) return false;
		if ( Vector3.Dot( observer.WorldRotation.Forward, delta.Normal ) < MathF.Cos( (observer.Senses?.FieldOfView ?? 140) * MathF.PI / 360 ) ) return false;
		return ClearLine( observer, target, point );
	}

	public static bool ClearLine( BaseNpc observer, GameObject target, Vector3 point )
	{
		var trace = observer.Scene.Trace.Ray( observer.EyePosition, point )
			.IgnoreGameObjectHierarchy( observer.GameObject ).WithoutTags( "trigger" ).Run();
		return !trace.Hit || PaydayInteraction.BelongsTo( trace.GameObject, target );
	}
}
