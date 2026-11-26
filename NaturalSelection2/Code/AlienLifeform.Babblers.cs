using System;

namespace NS2;

public abstract partial class AlienLifeform
{
	static readonly string[] Sockets = ["babbler_attach1", "babbler_attach2", "babbler_attach3", "babbler_attach4", "babbler_attach5", "babbler_attach6"];
	private protected readonly List<Babbler> brood = new( 6 );
	private protected readonly List<int> freeSlots = [1, 2, 3, 4, 5, 6];
	protected float babblerShield;
	float ShieldPerBabbler => Math.Min( 85, MathF.Round( BaseHealth * (this switch
	{
		Skulk => 0.28f, Gorge => 0.13f, Lerk => 0.14f, Fade => 0.16f, _ => 0.1f
	}), MidpointRounding.AwayFromZero ) ) / 6;

	internal GameObject BabblerApproachPoint => freeSlots.Count > 0 && Renderer.IsValid()
		? Renderer.GetAttachmentObject( Sockets[freeSlots[0] - 1] ) : null;

	internal bool CanReceiveBabblers => Health > 0 && freeSlots.Count > 0 && !GameObject.Tags.Has( "ns2_hallucination" );

	internal int DonationSpots()
	{
		int spots = 6 - brood.Count;
		if ( spots <= 0 || !CanReceiveBabblers ) return 0;
		foreach ( var b in Scene.GetAllComponents<Babbler>() )
			if ( b.ClingTarget == this && --spots == 0 ) break;
		return spots;
	}

	internal void AttachHatchling( Babbler babbler )
	{
		if ( !CanReceiveBabblers || !babbler.IsValid() || babbler.Health <= 0 || babbler.ClingSlot != 0 ) return;
		if ( babbler.BroodOwner != this && brood.Count >= 6 ) return;
		int slot = freeSlots[^1];
		var point = Renderer.GetAttachmentObject( Sockets[slot - 1] );
		if ( !point.IsValid() ) return;
		freeSlots.RemoveAt( freeSlots.Count - 1 );
		if ( babbler.BroodOwner != this )
		{
			if ( babbler.BroodOwner.IsValid() ) babbler.BroodOwner.BabblerRemoved( babbler );
			babbler.BroodOwner = this;
			brood.Add( babbler );
		}
		babbler.ClingTo( point, slot );
		babblerShield += ShieldPerBabbler;
	}

	internal void BabblerRemoved( Babbler babbler )
	{
		if ( !brood.Remove( babbler ) || babbler.ClingSlot == 0 ) return;
		ReleaseSlot( babbler.ClingSlot );
	}

	internal void ReleaseSlot( int slot )
	{
		freeSlots.Add( slot );
		babblerShield = Math.Min( babblerShield, (6 - freeSlots.Count) * ShieldPerBabbler );
	}

	void AbsorbBabblerShield( IDamageable.DamageEvent e )
	{
		if ( babblerShield <= 0 || e.AcceptedDamage <= 0 ||
			e.DamageInfo.Tags.HasAny( "nervegas", "corrode", "armoronly", "structuresonly", "splash" ) ) return;
		float absorbed = Math.Min( babblerShield, e.AcceptedDamage );
		e.AcceptedDamage -= absorbed;
		babblerShield -= absorbed;
		underFire = 3;
		while ( freeSlots.Count < 6 && babblerShield <= (5 - freeSlots.Count) * ShieldPerBabbler )
			BreakBabbler( brood.Find( b => b.ClingSlot != 0 ) );
	}

	void BreakBabbler( Babbler babbler )
	{
		BabblerRemoved( babbler );
		babbler.Kill();
	}
}
