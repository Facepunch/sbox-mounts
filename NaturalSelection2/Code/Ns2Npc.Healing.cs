namespace NS2;

public abstract partial class Ns2Npc
{
	[Sync( SyncFlags.FromHost )] float visibleHealTime { get; set; }
	[Sync( SyncFlags.FromHost )] bool healedFromSelf { get; set; }
	float playedHealTime;
	SkinnedModelRenderer healOverlay;

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
		if ( visibleHealTime == 0 || Application.IsDedicatedServer ) return;
		float age = Time.Now - visibleHealTime;
		bool pulse = playedHealTime != visibleHealTime;
		bool overlayActive = healOverlay.IsValid() && healOverlay.Active;
		if ( age >= 2 && !overlayActive && !pulse ) return;

		bool visible = !(this is Drifter { CloakFraction: > 0.401f } && PlayerGait.IsEnemyListener( this ));
		bool showOverlay = visible && age < 2 && Renderer.IsValid() && Renderer.Active;
		if ( showOverlay || overlayActive ) UpdateHealOverlay( showOverlay, age );

		if ( !pulse ) return;
		playedHealTime = visibleHealTime;
		bool alien = AlienHealing.IsAlien( this );
		if ( alien && visible )
		{
			var effect = this is Onos or Hive or Crag ? "cinematics/alien/heal_big.cinematic" : "cinematics/alien/heal.cinematic";
			CinematicLoader.Play( effect, WorldPosition, WorldRotation, GameObject );
		}
		if ( this is AlienLifeform && healedFromSelf ) return;
		var path = alien ? "mount://ns2/sounds/alien/common/regeneration_ability.sound" : "mount://ns2/sounds/marine/structures/mac/weld.sound";
		if ( GameResource.Load<SoundEvent>( path ) is { } sound ) GameObject.PlaySound( sound );
	}

	void UpdateHealOverlay( bool visible, float age )
	{
		if ( !healOverlay.IsValid() )
		{
			var layer = new GameObject( Renderer.GameObject, false, "Healing overlay" ) { NetworkMode = NetworkMode.Never };
			healOverlay = layer.AddComponent<SkinnedModelRenderer>();
			healOverlay.Model = Renderer.Model;
			healOverlay.BoneMergeTarget = Renderer;
			healOverlay.RenderType = ModelRenderer.ShadowRenderType.Off;
			var team = AlienHealing.IsAlien( this ) ? "alien" : "marine";
			healOverlay.MaterialOverride = Material.Load( $"mount://ns2/ns2/cinematics/vfx_materials/heal_{team}.material.vmat" );
		}
		if ( visible ) healOverlay.Attributes.Set( "Ns2HealAge", age );
		healOverlay.GameObject.Enabled = visible;
	}

	protected override void OnDisabled()
	{
		if ( healOverlay.IsValid() ) healOverlay.GameObject.Destroy();
		base.OnDisabled();
	}
}
