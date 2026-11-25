namespace Overgrowth;

// The native movement object survives death. Its team and skeleton still participate in AI vision.
internal sealed class CharacterRemains : Component
{
	internal bool StaticCharacter { get; set; }
	internal string[] Teams { get; set; }
	internal ModelPhysics Physics { get; set; }
	internal BodyFallSounds BodySounds;
	[Sync] internal bool Burning { get; set; }
	internal RabbitGuardAnimations FireRig { get; set; }
	SoundHandle fireLoop;
	CharacterFireParticles fireParticles;

	protected override void OnUpdate()
	{
		if ( !Burning )
		{
			fireLoop?.Stop();
			fireLoop = null;
			return;
		}
		if ( !Application.IsDedicatedServer ) fireParticles.Update( GetComponent<SkinnedModelRenderer>(), null );
		var physics = GetComponent<ModelPhysics>();
		if ( physics is not { Active: true } || CharacterLoader.RagdollCenter( physics ) is not { } center ) return;
		if ( !fireLoop.IsValid() ) fireLoop = RabbitGuard.PlayFireSound( "character_on_fire_loop_small", GameObject, center );
		else fireLoop.LocalTransform = WorldTransform.ToLocal( new Transform( center ) );
	}

	protected override void OnDisabled() => fireLoop?.Stop();

	internal void SetOnFire( bool burning )
	{
		if ( Burning == burning ) return;
		Burning = burning;
		PlayFireCue( burning );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayFireCue( bool burning )
	{
		var physics = GetComponent<ModelPhysics>();
		if ( physics is { Active: true } && CharacterLoader.RagdollCenter( physics ) is { } center )
			RabbitGuard.PlayFireSound( burning ? "character_catch_fire_small" : "character_fire_extinguish_small_shortened", GameObject, center );
	}

	internal void EnterWater( WaterHotspot volume )
	{
		if ( !Burning || FireRig is null || GetComponent<SkinnedModelRenderer>() is not { } renderer ||
			!renderer.TryGetBoneTransform( FireRig.HeadBone, out var head ) ) return;
		if ( volume.HeightAboveSurface( head.Position - head.Rotation.Forward * (FireRig.Head.HeadHalfLength * WorldScale.z) ) < 0.7f )
			SetOnFire( false );
	}

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
		var surface = TerrainLoader.ContactSurface( collision.Other.Collider as ModelCollider, point, collision.Other.Surface, out var terrainHit );
		BillboardParticle.EmitSkid( surface?.ResourceName, point, terrainHit );
		if ( BodySounds.PlayRagdoll( impulse, point, surface?.ResourceName, PlayBodySound ) )
			RabbitGuard.ReportSound( GameObject, center, RabbitGuard.HeardSound.LoudFoley, 5f );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayBodySound( string step, Vector3 point, string surface, float gain )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount &&
			GetComponent<ModelPhysics>() is { } physics && CharacterLoader.FromModel( physics.Model ) is { } character )
			mount.PlayMaterialEvent( character, GameObject, surface ?? "default", step, point, gain );
	}
}
