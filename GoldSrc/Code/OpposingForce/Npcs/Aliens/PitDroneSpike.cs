using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Hide]
public sealed class PitDroneSpike : NpcProjectile
{
	internal override string HitTag => "generic";
	internal override float Lifetime => _embedded ? 90 : 6;
	bool _embedded;

	internal override void PrepareLaunch() => WorldScale = Vector3.One * 0.5f;

	protected override void OnStart()
	{
		base.OnStart();
		if ( Sandbox.Mounting.Directory.Get( "opfor" ) is OpposingForceMount mount )
			ProjectileTrail.Attach( GameObject, mount, "Spike trail", "spike_trail", 0.2f, 0.5f, new Color32( 197, 194, 11, 192 ) );
	}

	internal override SceneTrace FlightTrace( Vector3 start, Vector3 end ) =>
		Scene.Trace.Box( new BBox( -Vector3.One * 4, Vector3.One * 4 ), start, end );

	protected override void OnFixedUpdate()
	{
		if ( !_embedded ) base.OnFixedUpdate();
		else if ( Networking.IsHost && Age >= Lifetime ) GameObject.Destroy();
	}

	internal override void HandleContact( SceneTraceResult hit )
	{
		var victim = hit.GameObject?.GetComponentInParent<IDamageable>();
		if ( Sandbox.Mounting.Directory.Get( "opfor" ) is OpposingForceMount mount )
			mount.NpcAudio.Emit( victim is null ? "weapons/xbow_hit1.wav" : "weapons/xbow_hitbod1.wav", hit.EndPosition, pitch: Game.Random.Float( 1.2f, 1.4f ) );
		if ( victim is not null || hit.GameObject?.GetComponentInParent<Rigidbody>().IsValid() == true )
		{
			DamageVictim( victim, hit.EndPosition, hit.Hitbox );
			GameObject.Destroy();
			return;
		}
		WorldPosition = hit.EndPosition - Velocity.Normal * 6;
		WorldRotation *= Rotation.FromRoll( Game.Random.Float( 0, 360 ) );
		Velocity = Vector3.Zero;
		Age = 0;
		_embedded = true;
	}
}
