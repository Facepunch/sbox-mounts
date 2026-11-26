using Sandbox.Npcs;
using System;

namespace NS2;

public abstract class AlienNpc : BaseNpc
{
	[Property, Range( 0, 12 ), Sync( SyncFlags.FromHost )] public int BiomassLevel { get; set; } = 1;
	protected virtual int HealthPerBiomass => 0;
	[Sync( SyncFlags.FromHost )] int appliedBiomassLevel { get; set; } = 1;

	protected override void OnUpdate()
	{
		if ( Networking.IsHost && Health > 0 )
		{
			UpdateBiomass();
			if ( Senses is { Active: true, VisibleHostiles.Count: > 0 } ) AlienTargeting.ForTeam( this ).ObservePlayers( this );
		}
		base.OnUpdate();
	}

	internal void UpdateBiomass()
	{
		int level = Math.Clamp( BiomassLevel, 0, 12 );
		if ( level == appliedBiomassLevel ) return;
		AdjustHealthLimit( HealthPerBiomass * (Math.Max( level, 1 ) - Math.Max( appliedBiomassLevel, 1 )) );
		appliedBiomassLevel = level;
		OnBiomassChanged();
	}

	protected virtual void OnBiomassChanged() { }

	protected void AdjustHealthLimit( float delta )
	{
		if ( delta == 0 ) return;
		float fraction = Health / MaxHealth;
		MaxHealth += delta;
		Health = MaxHealth * fraction;
	}
}
