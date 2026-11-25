using System;

namespace NS2;

internal sealed class CinematicBurst
{
	readonly Sprite sprite;
	readonly Vector3 origin;
	readonly Rotation rotation;
	readonly int count;
	readonly float lifetime, playback, minCos;
	readonly Vector2 speed, size, opacity, roll;
	readonly Color color;
	readonly CinematicChannel sizeScale, opacityScale, rotationScale, velocityX, velocityY, velocityZ;

	internal CinematicBurst( CinematicNode node )
	{
		CinematicChannel Channel( string key, int component = 0 ) => new( node.Get( key ), component );
		float Value( string key, int component = 0 ) => Channel( key, component ).At( 0 );
		Vector2 Range( string key ) => new( Value( key ), Value( key, 1 ) );

		var material = node.Get( "Particle Material" ).AsString();
		sprite = GameResource.Load<Sprite>( $"mount://ns2/ns2/{material}.vmat/particle.sprite" );
		count = (int)Value( "Emission Rate" );
		lifetime = Value( "Particle Life Span" );
		playback = Value( "Texture Animation Scale" );
		minCos = MathF.Cos( Value( "Cone Angle" ) );
		origin = new Vector3( Value( "Origin", 2 ), Value( "Origin" ), Value( "Origin", 1 ) ) * MapLoader.Scale;
		rotation = Rotation.From( new Angles( Value( "Angles", 1 ), Value( "Angles", 2 ), Value( "Angles" ) ) * (180f / MathF.PI) );
		speed = Range( "Particle Velocity" ) * MapLoader.Scale;
		size = Range( "Particle Size" ) * MapLoader.Scale;
		roll = Range( "Particle Rotation" ) * (180f / MathF.PI);
		opacity = Range( "Particle Opacity" );
		color = new Color( Value( "Particle Color" ), Value( "Particle Color", 1 ), Value( "Particle Color", 2 ) );
		sizeScale = Channel( "Particle Size Scale" );
		opacityScale = Channel( "Particle Opacity Scale" );
		rotationScale = Channel( "Particle Rotation Scale" );
		velocityX = Channel( "Particle Velocity Scale", 2 );
		velocityY = Channel( "Particle Velocity Scale" );
		velocityZ = Channel( "Particle Velocity Scale", 1 );
	}

	internal void Play( Vector3 position, Rotation facing )
	{
		if ( sprite is null ) return;
		var emitter = new GameObject( false, "Stomp impact smoke" ) { NetworkMode = NetworkMode.Never };
		emitter.WorldPosition = position + facing * origin;
		var direction = facing * rotation;
		var particles = emitter.AddComponent<ParticleEffect>();
		particles.Lifetime = lifetime;
		particles.MaxParticles = count;
		particles.OnStep = ( p, fraction ) => Step( p, direction, fraction );
		var renderer = emitter.AddComponent<ParticleSpriteRenderer>();
		renderer.Sprite = sprite;
		renderer.PlaybackSpeed = playback;
		renderer.SortMode = ParticleSpriteRenderer.ParticleSortMode.ByDistance;
		emitter.AddComponent<TemporaryEffect>().DestroyAfterSeconds = lifetime;
		emitter.Enabled = true;

		for ( int i = 0; i < count; i++ )
		{
			var p = particles.Emit( emitter.WorldPosition );
			p.StartScale = MathX.Lerp( size.x, size.y, p.Rand( 4 ) );
			p.StartAngles.roll = MathX.Lerp( roll.x, roll.y, p.Rand( 5 ) );
			p.Color = color;
			Step( p, direction, 0 );
		}
	}

	void Step( Particle p, Rotation direction, float fraction )
	{
		float cosine = MathX.Lerp( minCos, 1, p.Rand( 1 ) );
		float radius = MathF.Sqrt( MathF.Max( 0, 1 - cosine * cosine ) );
		float angle = p.Rand( 2 ) * MathF.Tau;
		var velocity = direction * new Vector3( cosine, radius * MathF.Cos( angle ), radius * MathF.Sin( angle ) );
		var initialVelocity = velocity * MathX.Lerp( speed.x, speed.y, p.Rand( 3 ) );
		float age = fraction * lifetime;
		p.Velocity = initialVelocity * new Vector3( velocityX.At( age ), velocityY.At( age ), velocityZ.At( age ) );
		p.Size = p.StartScale * sizeScale.At( age );
		p.Angles.roll = p.StartAngles.roll * rotationScale.At( age );
		p.Alpha = Math.Clamp( MathX.Lerp( opacity.x, opacity.y, p.Rand( 6 ) ) * opacityScale.At( age ), 0, 1 );
	}
}
