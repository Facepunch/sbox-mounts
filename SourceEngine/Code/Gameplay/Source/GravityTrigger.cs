namespace Source1;

[Hide]
public sealed class GravityTrigger : Component
{
	[Property] public float Gravity { get; set; }
	[Property] public List<PhysicsCollider> Volumes { get; set; } = [];

	protected override void OnFixedUpdate()
	{
		foreach ( var volume in Volumes )
		{
			if ( !volume.IsValid() || !volume.Enabled ) continue;
			foreach ( var other in volume.Touching )
			{
				if ( other.GetComponentInParent<PlayerController>() is { IsProxy: false, Body: { } body } )
					body.GravityScale = Gravity == 0 ? 1 : Gravity;
			}
		}
	}
}
