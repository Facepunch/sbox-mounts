namespace GoldSrc.HalfLife;

[Hide]
public sealed class PlayerRocket : RocketProjectile
{
	internal float? NormalDamage { get; set; }
	internal override float IgnitionDelay => 0.4f;
	internal override float SmokeLifetime => 4;
	internal override float BurnTime => 1;
	internal override float Lifetime => float.PositiveInfinity;
	internal RocketLauncher Launcher { get; set; }
	float _nextSteer;

	internal override void PrepareLaunch()
	{
		var forward = WorldRotation.Forward;
		var angles = WorldRotation.Angles();
		angles.pitch -= 30;
		Velocity = angles.ToRotation().Forward * Velocity.Length;
		if ( Shooter.GetComponent<PlayerController>() is { } player )
			Velocity += forward * player.Velocity.Dot( forward );
		_nextSteer = IgnitionDelay + 0.1f;
	}

	internal override void Guide()
	{
		if ( Age < IgnitionDelay )
		{
			Velocity += Scene.PhysicsWorld.Gravity * (0.5f * Time.Delta);
			return;
		}
		if ( Age < _nextSteer ) return;
		_nextSteer += 0.1f;
		var desired = FindSpotDirection();
		WorldRotation = Rotation.LookAt( desired );
		var underwater = WaterBody.Find( Scene, WorldPosition ).IsValid();
		if ( Age < IgnitionDelay + BurnTime )
		{
			Velocity = Velocity * 0.2f + desired * (Velocity.Length * 0.8f + 400);
			var limit = underwater ? 300 : 2000;
			if ( Velocity.Length > limit ) Velocity = Velocity.Normal * limit;
			if ( underwater ) ShowBubbles( WorldPosition - Velocity * 0.1f, WorldPosition );
		}
		else
		{
			Velocity = Velocity * 0.2f + desired * (Velocity.Length * 0.798f);
			if ( !underwater && Velocity.Length < 1500 )
			{
				Blast.Detonate( GameObject, Shooter, WorldPosition, Vector3.Up, Damage, false, NormalDamage );
				GameObject.Destroy();
				return;
			}
		}
		if ( Launcher.IsValid() && (Age > IgnitionDelay + 6 || WorldPosition.DistanceSquared( Launcher.WorldPosition ) > 8192 * 8192) )
			DetachLauncher();
	}

	Vector3 FindSpotDirection()
	{
		var forward = WorldRotation.Forward;
		var direction = forward;
		var best = 4096f;
		foreach ( var launcher in Scene.GetAllComponents<RocketLauncher>() )
		{
			if ( !launcher.IsActive || launcher.SpotPosition is not { } spot ) continue;
			var delta = spot - WorldPosition;
			var dot = forward.Dot( delta.Normal );
			var score = delta.Length * (1 - dot);
			if ( dot <= 0 || score >= best ) continue;
			var trace = Scene.Trace.Ray( WorldPosition, spot ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
			if ( trace.Fraction < 0.9f ) continue;
			best = score;
			direction = delta.Normal;
		}
		return direction;
	}

	void DetachLauncher()
	{
		if ( Launcher.IsValid() ) Launcher.ActiveRockets--;
		Launcher = null;
	}

	protected override void OnDestroy()
	{
		DetachLauncher();
		base.OnDestroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ShowBubbles( Vector3 start, Vector3 end ) => Blast.BubbleTrail( Game.ActiveScene, start, end, 4, "Rocket bubbles" );

	internal override void OnImpact( IDamageable victim, Vector3 position, Vector3 normal )
		=> Blast.Detonate( GameObject, Shooter, position, normal, Damage, true, NormalDamage );
}
