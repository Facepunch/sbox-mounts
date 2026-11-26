namespace Overgrowth;

public sealed partial class WaterHotspot : Component
{
	[Property] public BoxCollider Volume { get; set; }
	[Property] public bool Lethal { get; set; }
	readonly List<Collider> contacts = [];
	readonly HashSet<RabbitGuard> occupants = [];
	readonly HashSet<RagdollDamping> ragdolls = [];

	internal bool Contains( RabbitGuard fighter ) => Active && Volume.IsValid() && Volume.Active && occupants.Contains( fighter );

	internal bool Contains( RagdollDamping ragdoll ) => Active && Volume.IsValid() && Volume.Active && ragdolls.Contains( ragdoll );

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !Volume.IsValid() ) return;
		contacts.Clear();
		occupants.Clear();
		ragdolls.Clear();
		Volume.GetTouching( contacts );
		foreach ( var contact in contacts )
		{
			if ( !contact.IsValid() ) continue;
			if ( contact.GetComponentInParent<RagdollDamping>() is { } damping && ragdolls.Add( damping ) )
				damping.EnterWater( this );
			if ( contact.GetComponentInParent<RabbitGuard>() is { Active: true } fighter && occupants.Add( fighter ) )
				fighter.WaterIntersect( this, Time.Delta );
		}
	}

	// WaterIntersect measures signed distance along the hotspot's local vertical.
	internal float HeightAboveSurface( Vector3 point ) =>
		(WorldTransform.PointToLocal( point ).z - Volume.Center.z - Volume.Scale.z * 0.5f) * WorldScale.z / 40f;
}
