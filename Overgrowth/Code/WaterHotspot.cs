namespace Overgrowth;

public sealed class WaterHotspot : Component
{
	[Property] public BoxCollider Volume { get; set; }
	[Property] public bool Lethal { get; set; }
	readonly List<Collider> contacts = [];
	readonly HashSet<RabbitGuard> occupants = [];

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !Volume.IsValid() ) return;
		contacts.Clear();
		occupants.Clear();
		Volume.GetTouching( contacts );
		foreach ( var contact in contacts )
		{
			if ( contact.IsValid() && contact.GetComponentInParent<RabbitGuard>() is { Active: true } fighter && occupants.Add( fighter ) )
				fighter.WaterIntersect( this, Time.Delta );
		}
	}

	// WaterIntersect measures signed distance along the hotspot's local vertical.
	internal float HeightAboveSurface( Vector3 point ) =>
		(WorldTransform.PointToLocal( point ).z - Volume.Center.z - Volume.Scale.z * 0.5f) * WorldScale.z / 40f;
}
