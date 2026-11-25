using System;

namespace NS2;

public sealed partial class Gorge
{
	// Gorge base health is 160. NS2 rounds 13% to 21 before dividing it between the six attachment points.
	const float ShieldPerBabbler = 3.5f;
	static readonly string[] Sockets = ["babbler_attach1", "babbler_attach2", "babbler_attach3", "babbler_attach4", "babbler_attach5", "babbler_attach6"];
	readonly List<Babbler> brood = new( 6 );
	readonly List<int> freeSlots = [1, 2, 3, 4, 5, 6];
	internal GameObject BabblerApproachPoint => freeSlots.Count > 0 ? Renderer.GetAttachmentObject( Sockets[freeSlots[0] - 1] ) : null;
	TimeUntil nextHatch;
	float babblerShield;

	void UpdateBrood()
	{
		if ( nextHatch > 0 ) return;
		nextHatch = 2.5f;
		if ( brood.Count >= 6 || underFire > 0 ) return;
		var point = BabblerApproachPoint;
		if ( !point.IsValid() ) return;
		var pose = new Transform( point.WorldPosition - Vector3.Up * (0.25f * Scale), Rotation.Identity );
		var child = GameObject.Clone( PrefabFile.Load( "mount://ns2/entities/npcs/babbler.prefab" ), pose, startEnabled: false );
		if ( !child.IsValid() ) return;
		var babbler = child.GetComponent<Babbler>( true );
		babbler.OwnerGorge = this;
		brood.Add( babbler );
		child.Enabled = true;
		if ( Networking.IsActive ) child.NetworkSpawn();
	}

	internal void AttachHatchling( Babbler babbler )
	{
		if ( freeSlots.Count == 0 ) return;
		int slot = freeSlots[^1];
		var point = Renderer.GetAttachmentObject( Sockets[slot - 1] );
		if ( !point.IsValid() ) return;
		freeSlots.RemoveAt( freeSlots.Count - 1 );
		babbler.ClingTo( point, slot );
		babblerShield += ShieldPerBabbler;
	}

	internal void BabblerRemoved( Babbler babbler )
	{
		if ( !brood.Remove( babbler ) || babbler.ClingSlot == 0 ) return;
		freeSlots.Add( babbler.ClingSlot );
		babblerShield = Math.Min( babblerShield, (6 - freeSlots.Count) * ShieldPerBabbler );
	}

	void AbsorbWithBabblers( IDamageable.DamageEvent e )
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
