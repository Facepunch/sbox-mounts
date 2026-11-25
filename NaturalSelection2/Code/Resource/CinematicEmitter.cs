using System;

namespace NS2;

internal sealed class CinematicEmitter
{
	readonly Sprite sprite;
	readonly CinematicChannel originX, originY, originZ;
	readonly Rotation rotation;
	readonly string name;
	readonly Model model;
	readonly Texture normal;
	readonly CinematicValue visibility;
	readonly int count, capacity, alignment;
	readonly bool additive, continuous, loop;
	readonly float duration, clipDuration, stretch, radius, bounce, friction, maxSpeed;
	readonly Vector3 force;
	readonly float lifetime, playback, minCos;
	readonly Vector2 speed, size, opacity, roll;
	readonly Color color;
	readonly CinematicChannel sizeScale, opacityScale, rotationScale, velocityX, velocityY, velocityZ;

	internal CinematicEmitter( CinematicNode node, float clipDuration )
	{
		CinematicChannel Channel( string key, int component = 0 ) => new( node.Get( key ), component );
		float Value( string key, int component = 0 ) => Channel( key, component ).At( 0 );
		Vector2 Range( string key ) => new( Value( key ), Value( key, 1 ) );

		this.clipDuration = clipDuration;
		name = node.Get( "Name" ).AsString();
		visibility = node.Get( "Visible" );
		duration = Value( "Emitter Life Span" );
		count = (int)Value( "Emission Rate" );
		continuous = node.Get( "Emission Behavior" ).AsInt() == 0;
		loop = node.Get( "Life Span Behavior" ).AsInt() == 0;
		capacity = Math.Max( 1, node.Get( "max_particles" ).AsInt( count ) );
		minCos = MathF.Cos( Value( "Cone Angle" ) );
		originX = Channel( "Origin", 2 );
		originY = Channel( "Origin" );
		originZ = Channel( "Origin", 1 );
		rotation = Rotation.From( new Angles( Value( "Angles", 1 ), Value( "Angles", 2 ), Value( "Angles" ) ) * (180f / MathF.PI) );

		if ( node.Name == "Physics Emitter" )
		{
			model = Model.Load( $"mount://ns2/ns2/{node.Get( "particle_model" ).AsString()}.vmdl" );
			lifetime = Value( "particle_life_span" );
			radius = Value( "particle_radius" ) * MapLoader.Scale;
			bounce = Value( "particle_restitution" );
			friction = Value( "particle_dynamic_friction" );
			maxSpeed = Value( "max_particle_speed" ) * MapLoader.Scale;
			speed = Range( "particle_velocity" ) * MapLoader.Scale;
			force = new Vector3( Value( "particle_force", 2 ), Value( "particle_force" ), Value( "particle_force", 1 ) ) * MapLoader.Scale;
			return;
		}

		var material = $"mount://ns2/ns2/{node.Get( "Particle Material" ).AsString()}.vmat";
		sprite = GameResource.Load<Sprite>( $"{material}/particle.sprite" );
		if ( sprite is null ) normal = Material.Load( material )?.GetTexture( "g_tNormalMap" );
		lifetime = Value( "Particle Life Span" );
		playback = Value( "Texture Animation Scale" );
		alignment = node.Get( "Particle Alignment" ).AsInt();
		additive = node.Get( "blendMode" ).AsInt() == 0;
		stretch = Value( "Particle Motion Stretch" );
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
		if ( sprite is null && normal is null && model is null ) return;
		var emitter = new GameObject( false, name ) { NetworkMode = NetworkMode.Never };
		emitter.WorldPosition = position;
		var direction = facing * rotation;
		var particles = emitter.AddComponent<ParticleEffect>();
		particles.Lifetime = lifetime;
		particles.MaxParticles = capacity;
		Component renderer;
		if ( model is not null )
		{
			particles.Collision = true;
			particles.CollisionRadius = radius;
			particles.Bounce = bounce;
			particles.Friction = friction;
			particles.Force = true;
			particles.ForceDirection = force;
			var models = emitter.AddComponent<ParticleModelRenderer>();
			models.Choices = [model];
			renderer = models;
		}
		else
		{
			particles.OnStep = ( p, fraction ) => Step( p, direction, fraction );
			var sprites = emitter.AddComponent<ParticleSpriteRenderer>();
			sprites.Sprite = sprite;
			sprites.PlaybackSpeed = playback;
			sprites.Additive = additive && normal is null;
			sprites.Refraction = normal is not null;
			sprites.NormalTexture = normal;
			sprites.FaceVelocity = alignment == 1;
			if ( alignment == 2 )
			{
				sprites.Alignment = ParticleSpriteRenderer.BillboardAlignment.Object;
				emitter.WorldRotation = direction;
			}
			sprites.SortMode = ParticleSpriteRenderer.ParticleSortMode.ByDistance;
			renderer = sprites;
		}
		renderer.Enabled = visibility.IsVisibleAt( 0 );
		emitter.AddComponent<TemporaryEffect>().DestroyAfterSeconds = loop ? clipDuration : duration;
		emitter.Enabled = true;

		if ( !continuous ) Emit( count, 0 );
		if ( !loop && !continuous && !visibility.HasVisibilityKeys ) return;

		float elapsed = 0;
		int scheduled = 0;
		particles.OnPreStep += dt =>
		{
			elapsed += dt;
			renderer.Enabled = visibility.IsVisibleAt( elapsed );
			float end = loop ? clipDuration : duration;
			float time = Math.Min( elapsed, end );
			int total = continuous ? (int)(time * count) : loop && duration > 0 ? (int)(time / duration) : 0;
			if ( total > scheduled ) Emit( continuous ? total - scheduled : count, time );
			scheduled = total;
		};

		void Emit( int amount, float time )
		{
			var offset = new Vector3( originX.At( time ), originY.At( time ), originZ.At( time ) );
			var spawn = position + facing * (offset * MapLoader.Scale);
			int available = Math.Min( amount, capacity - particles.ParticleCount );
			for ( int i = 0; i < available; i++ )
			{
				var p = particles.Emit( spawn );
				if ( model is not null )
				{
					p.Size = Vector3.One;
					p.Velocity = InitialVelocity( p, direction ).ClampLength( maxSpeed );
					continue;
				}
				p.StartScale = MathX.Lerp( size.x, size.y, p.Rand( 4 ) );
				p.StartAngles.roll = MathX.Lerp( roll.x, roll.y, p.Rand( 5 ) );
				p.Color = color;
				Step( p, direction, 0 );
			}
		}
	}

	Vector3 InitialVelocity( Particle p, Rotation direction )
	{
		float cosine = MathX.Lerp( minCos, 1, p.Rand( 1 ) );
		float radius = MathF.Sqrt( MathF.Max( 0, 1 - cosine * cosine ) );
		float angle = p.Rand( 2 ) * MathF.Tau;
		var velocity = direction * new Vector3( cosine, radius * MathF.Cos( angle ), radius * MathF.Sin( angle ) );
		return velocity * MathX.Lerp( speed.x, speed.y, p.Rand( 3 ) );
	}

	void Step( Particle p, Rotation direction, float fraction )
	{
		float age = fraction * lifetime;
		p.Velocity = InitialVelocity( p, direction ) * new Vector3( velocityX.At( age ), velocityY.At( age ), velocityZ.At( age ) );
		p.Size = p.StartScale * sizeScale.At( age );
		if ( alignment == 1 ) p.Size.y *= stretch;
		p.Angles.roll = p.StartAngles.roll * rotationScale.At( age );
		p.Alpha = Math.Clamp( MathX.Lerp( opacity.x, opacity.y, p.Rand( 6 ) ) * opacityScale.At( age ), 0, 1 );
	}
}
