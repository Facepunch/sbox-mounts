using GoldSrc.HalfLife;

namespace GoldSrc.DeathmatchClassic;

[Hide]
public sealed class BouncingGrenade : NpcProjectile
{
	internal override void Guide()
	{
		if ( Age >= 2.5f ) { Explode( WorldPosition ); return; }
		Velocity += Scene.PhysicsWorld.Gravity * Time.Delta;
		WorldRotation *= Rotation.From( new Angles( 300, 300, 300 ) * Time.Delta );
	}

	internal override void HandleContact( SceneTraceResult hit )
	{
		if ( hit.GameObject?.GetComponentInParent<IDamageable>() is not null )
		{
			Explode( hit.EndPosition + hit.Normal );
			return;
		}
		WorldPosition = hit.EndPosition + hit.Normal * 0.1f;
		Velocity = (Velocity - 2 * Velocity.Dot( hit.Normal ) * hit.Normal) * 0.5f;
		if ( Velocity.Length > 20 ) ClassicExplosion.Mount?.NpcAudio.Emit( "weapons/bounce.wav", WorldPosition );
	}

	void Explode( Vector3 origin )
	{
		ClassicExplosion.Damage( GameObject, Shooter, origin, 120 );
		ClassicExplosion.Show( origin );
		GameObject.Destroy();
	}
}
