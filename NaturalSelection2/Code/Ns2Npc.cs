using Sandbox.Npcs;

namespace NS2;

public abstract class Ns2Npc : BaseNpc
{
	[Sync( SyncFlags.FromHost )] float visibleHealTime { get; set; }
	[Sync( SyncFlags.FromHost )] bool healedFromSelf { get; set; }
	float playedHealTime;

	internal void RecordHeal( HealFeedback feedback )
	{
		healedFromSelf = feedback != HealFeedback.External;
		if ( feedback != HealFeedback.Hidden ) visibleHealTime = Time.Now;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		UpdateHealFeedback();
	}

	private protected void UpdateHealFeedback()
	{
		if ( playedHealTime == visibleHealTime || Application.IsDedicatedServer ) return;
		playedHealTime = visibleHealTime;

		bool alien = AlienHealing.IsAlien( this );
		if ( alien && !(this is Drifter { CloakFraction: > 0.401f } && PlayerGait.IsEnemyListener( this )) )
		{
			var effect = this is Onos or Hive or Crag ? "cinematics/alien/heal_big.cinematic" : "cinematics/alien/heal.cinematic";
			CinematicLoader.Play( effect, WorldPosition, WorldRotation, GameObject );
		}
		if ( this is AlienLifeform && healedFromSelf ) return;
		var path = alien ? "mount://ns2/sounds/alien/common/regeneration_ability.sound" : "mount://ns2/sounds/marine/structures/mac/weld.sound";
		if ( GameResource.Load<SoundEvent>( path ) is { } sound ) GameObject.PlaySound( sound );
	}

	Vector3? lastOrigin;
	Vector3 engagementPoint;

	internal Vector3 EngagementPoint
	{
		get
		{
			var origin = WorldPosition;
			if ( lastOrigin != origin )
			{
				engagementPoint = GetEngagementPoint();
				lastOrigin = origin;
			}
			return engagementPoint;
		}
	}

	protected virtual Vector3 GetEngagementPoint() => WorldPosition;

	protected override Vector3 CalculateAimPoint( GameObject target ) =>
		target.GetComponent<Ns2Npc>()?.EngagementPoint ?? base.CalculateAimPoint( target );
}

internal static class DamageTraces
{
	internal static SceneTrace UseDamageHitboxes( this SceneTrace trace ) =>
		trace.WithoutTags( "ns2_movement" ).UseHitboxes();
}
