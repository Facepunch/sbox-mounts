namespace GoldSrc.HalfLife;

[Hide]
public sealed class SparkShower : Component
{
	ParticleEffect _sparks;
	Vector3 _velocity;
	bool _classic;
	TimeUntil _nextBurst, _expires;

	internal static void Spawn( Vector3 origin, Vector3 normal )
	{
		var go = new GameObject( false, "Explosion spark shower" ) { NetworkMode = NetworkMode.Never, WorldPosition = origin };
		var shower = go.AddComponent<SparkShower>();
		shower._sparks = StudioSparks.CreateEmitter( go );
		if ( shower._sparks is null ) { go.Destroy(); return; }
		var velocity = normal * Game.Random.Float( 200, 300 );
		velocity.x += Game.Random.Float( -100, 100 );
		velocity.y += Game.Random.Float( -100, 100 );
		velocity.z += velocity.z >= 0 ? 200 : -200;
		shower._velocity = velocity;
		shower._classic = ClassicBounce.Enabled;
		shower._nextBurst = 0.1f;
		shower._expires = Game.Random.Float( 0.5f, 1.5f );
		go.Enabled = true;
	}

	protected override void OnFixedUpdate()
	{
		var step = Time.Delta;
		var gravity = ClassicBounce.GravityFor( Scene, _classic ) * 0.5f;
		var end = WorldPosition + (_velocity + gravity * (step * 0.5f)) * step;
		_velocity += gravity * step;
		var hit = Scene.Trace.Ray( WorldPosition, end ).IgnoreDynamic().WithoutTags( "trigger" ).Run();
		WorldPosition = hit.Hit ? hit.EndPosition + hit.Normal * 0.1f : end;
		if ( hit.Hit )
		{
			_velocity = Vector3.Reflect( _velocity, hit.Normal ) * (hit.Normal.z > 0.7f ? 0.1f : 0.6f);
			if ( _velocity.WithZ( 0 ).LengthSquared < 10 ) _expires = 0;
		}
		if ( _nextBurst > 0 ) return;
		StudioSparks.Emit( _sparks, WorldPosition, 6 );
		_nextBurst = 0.1f;
		if ( _expires > 0 ) return;
		GameObject.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0.35f;
		Enabled = false;
	}
}
