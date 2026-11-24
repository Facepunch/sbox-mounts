using Sandbox.Npcs;

namespace Diesel;

static class PaydayPerception
{
	public static bool Armed( PlayerController player ) =>
		player.Active && player.GetComponent<BaseInventoryComponent>()?.ActiveItem is BaseCombatWeapon { IsHeld: true, IsActive: true };

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
