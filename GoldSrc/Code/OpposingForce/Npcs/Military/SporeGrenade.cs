using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Hide]
public sealed class SporeGrenade : NpcProjectile
{
	internal override float Lifetime => 5;
	internal override string HitTag => "generic";
	TimeUntil _nextBounce;
	internal override void Guide()
	{
		if ( Age >= 4 ) Detonate( WorldPosition );
		else Velocity += Scene.PhysicsWorld.Gravity * Time.Delta;
	}

	internal override void HandleContact( SceneTraceResult hit )
	{
		if ( hit.GameObject?.GetComponentInParent<IDamageable>() is { } victim )
		{
			DamageVictim( victim, hit.EndPosition );
			Detonate( hit.EndPosition );
			return;
		}
		WorldPosition = hit.EndPosition + hit.Normal;
		Velocity = (Velocity - 2 * Velocity.Dot( hit.Normal ) * hit.Normal) * 0.5f;
		if ( _nextBounce > 0 || Velocity.Length < 40 ) return;
		_nextBounce = 0.2f;
		Mount?.NpcAudio.Emit( "weapons/splauncher_bounce.wav", WorldPosition, volume: 0.25f );
	}

	static OpposingForceMount Mount => Sandbox.Mounting.Directory.Get( "opfor" ) as OpposingForceMount;

	void Detonate( Vector3 position )
	{
		Blast.DamageInRadius( GameObject, Shooter, position, Damage, 200, "blast", "alwaysgib" );
		Burst( position );
		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Burst( Vector3 position )
	{
		Mount?.NpcAudio.Emit( "weapons/splauncher_impact.wav", position );
		if ( Mount?.GetIndexedSprite( "spore_exp_01" ) is not { } sprite ) return;
		var go = new GameObject( false, "Spore burst" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;
		var renderer = go.AddComponent<SpriteRenderer>();
		renderer.Sprite = sprite;
		renderer.Additive = true;
		renderer.Size = 80;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = sprite.Animations[0].Frames.Count / 10f;
		go.Enabled = true;
	}
}
