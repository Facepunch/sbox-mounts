namespace GoldSrc.HalfLife;

internal static partial class BloodEffects
{
	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void Stream( Npc.BloodColor color, Vector3 point, Vector3 direction, float damage )
	{
		if ( HalfLifeMount.SharedAssets is not { IsMounted: true } mount ) return;
		int speed = System.Math.Clamp( (int)damage, 0, 255 );
		var jet = CreateStreamParticles( point, 2, 0.4f );
		for ( int i = 0; i < 100; i++ )
		{
			// GoldSrc stores the declining speed in an integer, leaving a stationary tail.
			var velocity = (direction + Vector3.Up * (i * 0.005f - 0.05f)) * System.Math.Max( speed - i, 0 );
			EmitStreamParticle( jet, mount, color, point, velocity );
		}

		if ( speed < 5 ) return;
		var drops = CreateStreamParticles( point, 3, 0.2f );
		for ( int i = 0; i < speed / 5; i++ )
		{
			var random = Game.Random.Float( 0, 1 );
			var magnitude = (int)(speed * random) * random * 1.7f;
			var aim = direction + Vector3.Up * (i * 0.005f - 0.075f);
			EmitStreamParticle( drops, mount, color, point, aim * magnitude );
			for ( int j = 0; j < 2; j++ )
				EmitStreamParticle( drops, mount, color, point + Vector3.One * Game.Random.Float( -1, 1 ), (aim + Vector3.Up * 0.005f) * magnitude );
		}
	}

	static void EmitStreamParticle( ParticleEffect effect, HalfLifeMount mount, Npc.BloodColor color, Vector3 position, Vector3 velocity )
	{
		if ( effect.Emit( position, 0 ) is not { } particle ) return;
		particle.Color = mount.PaletteColor( System.Math.Min( 255, (int)color + Game.Random.Int( 0, 9 ) ) );
		particle.Velocity = velocity;
	}

	static ParticleEffect CreateStreamParticles( Vector3 position, float lifetime, float gravityScale )
	{
		var go = new GameObject( false, "Blood stream" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;
		var effect = go.AddComponent<ParticleEffect>();
		effect.Lifetime = lifetime;
		effect.ApplyShape = true;
		var camera = go.Scene.Camera;
		float depth = camera.IsValid() ? (position - camera.WorldPosition).Dot( camera.WorldRotation.Forward ) + 1 : 0;
		effect.InitialScale = depth < 20 ? 1 : 1 + depth * 0.002f;
		effect.Force = true;
		effect.ForceDirection = go.Scene.PhysicsWorld.Gravity * gravityScale;
		effect.ApplyAlpha = true;
		effect.Alpha = new ParticleFloat
		{
			Type = ParticleFloat.ValueType.Curve,
			CurveA = new Curve( new Curve.Frame( 0, 1 ), new Curve.Frame( 1 - 1 / (16 * lifetime), 1 ) { Mode = Curve.HandleMode.Linear }, new Curve.Frame( 1, 0 ) )
		};
		var sprite = go.AddComponent<ParticleSpriteRenderer>();
		sprite.Sprite = PaletteParticle.Sprite;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0;
		go.Enabled = true;
		return effect;
	}
}
