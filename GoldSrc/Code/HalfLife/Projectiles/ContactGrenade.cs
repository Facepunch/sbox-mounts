namespace GoldSrc.HalfLife;

[Hide]
public sealed class ContactGrenade : NpcProjectile
{
	TimeUntil _nextWarning;

	internal override float Lifetime => 12;

	internal override void Guide()
	{
		if ( _nextWarning <= 0 )
		{
			_nextWarning = 0.2f;
			Scene.GetSystem<Sandbox.Npcs.NpcSystem>().EmitStimulus( TimedGrenade.DangerStimulus, WorldPosition + Velocity * 0.5f, GameObject, Velocity.Length, 0.2f );
		}
		Velocity += FlightGravity * (0.5f * Time.Delta);
		WorldRotation *= Rotation.FromPitch( -300 * Time.Delta );
	}

	internal override void OnImpact( IDamageable victim, Vector3 position, Vector3 normal )
		=> Blast.Detonate( GameObject, Shooter, position, normal, 100, true );
}
