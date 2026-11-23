namespace Overgrowth;

// The native movement object survives death. Its team and skeleton still participate in AI vision.
internal sealed class CharacterRemains : Component
{
	internal bool StaticCharacter { get; set; }
	internal string[] Teams { get; set; }
	internal ModelPhysics Physics { get; set; }
	internal BodyFallSounds BodySounds;
	internal RabbitGuard Dragger { get; set; }

	internal void OnBodyContact( Collision collision )
	{
		var other = collision.Other.GameObject;
		if ( !Networking.IsHost || Dragger.IsValid() || collision.Other.IsTrigger || !other.IsValid() ||
			other.GetComponentInParent<CharacterRemains>() == this ) return;
		float impulse = RabbitGuard.BodyImpactImpulse( collision ) / 40f;
		if ( impulse <= 1f || CharacterLoader.RagdollCenter( Physics ) is not { } center ||
			RabbitGuard.WaterDepthAt( Scene, center - Vector3.Up * 18f ) >= 0.2f ) return;

		var point = collision.Contact.Point;
		var surface = TerrainLoader.ContactSurface( collision.Other.Collider as ModelCollider, point, collision.Other.Surface );
		if ( BodySounds.PlayRagdoll( impulse, point, surface?.ResourceName, PlayBodySound ) )
			RabbitGuard.ReportSound( GameObject, center, RabbitGuard.HeardSound.LoudFoley, 5f );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayBodySound( string step, Vector3 point, string surface, float gain )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount &&
			GetComponent<ModelPhysics>() is { } physics && CharacterLoader.FromModel( physics.Model ) is { } character )
			mount.PlayMaterialEvent( character, GameObject, surface, step, point, gain );
	}
}
