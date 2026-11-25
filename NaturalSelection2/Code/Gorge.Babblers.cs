using System;

namespace NS2;

public sealed partial class Gorge
{
	// Gorge base health is 160. NS2 rounds 13% to 21 before dividing it between the six attachment points.
	const float ShieldPerBabbler = 3.5f;
	readonly List<Babbler> brood = new( 6 );
	TimeUntil nextHatch;
	float babblerShield;

	void UpdateBrood()
	{
		if ( nextHatch > 0 ) return;
		nextHatch = 2.5f;
		if ( brood.Count >= 6 || underFire > 0 ) return;
		for ( int slot = 6; slot > 0; slot-- )
		{
			if ( brood.Exists( b => b.ClingSlot == slot ) ) continue;
			var point = Renderer.GetAttachmentObject( $"babbler_attach{slot}" );
			if ( !point.IsValid() ) return;
			var child = GameObject.Clone( PrefabFile.Load( "mount://ns2/entities/npcs/babbler.prefab" ), point.WorldTransform, point, false );
			if ( !child.IsValid() ) return;
			var babbler = child.GetComponent<Babbler>( true );
			babbler.ClingTo( this, slot );
			brood.Add( babbler );
			babblerShield += ShieldPerBabbler;
			child.Enabled = true;
			if ( Networking.IsActive ) child.NetworkSpawn();
			return;
		}
	}

	internal void BabblerRemoved( Babbler babbler )
	{
		if ( brood.Remove( babbler ) ) babblerShield = Math.Min( babblerShield, brood.Count * ShieldPerBabbler );
	}

	void AbsorbWithBabblers( IDamageable.DamageEvent e )
	{
		if ( babblerShield <= 0 || e.AcceptedDamage <= 0 ||
			e.DamageInfo.Tags.HasAny( "nervegas", "corrode", "armoronly", "structuresonly", "splash" ) ) return;
		float absorbed = Math.Min( babblerShield, e.AcceptedDamage );
		e.AcceptedDamage -= absorbed;
		babblerShield -= absorbed;
		underFire = 3;
		while ( brood.Count > 0 && babblerShield <= (brood.Count - 1) * ShieldPerBabbler )
			BreakBabbler();
	}

	void BreakBabbler()
	{
		var babbler = brood[0];
		BabblerRemoved( babbler );
		babbler.BreakShield();
	}
}
