using System;

namespace Source1;

/// <summary>Draws a batch of streaks from each particle outward along its current velocity.</summary>
[Hide]
public sealed class ParticleStreaks : Renderer
{
	readonly VertexBuffer _vertices = new();
	ParticleEffect _particles;
	SceneCustomObject _batch;
	Material _material;
	ParticleFloat _length;

	internal static ParticleEffect Create( Vector3 origin, string mount, string name, int count, Vector2 lifetime,
		ParticleFloat width, ParticleFloat length, GameObject parent = null )
	{
		var material = Material.Load( $"mount://{mount}/materials/{name}.vmt.vmat" );
		if ( !material.IsValid() || material.IsError ) return null;
		var go = new GameObject( parent ?? Game.ActiveScene, false, name ) { NetworkMode = NetworkMode.Never, WorldPosition = origin };
		var particles = go.AddComponent<ParticleEffect>();
		particles.MaxParticles = count;
		particles.Lifetime = new ParticleFloat( lifetime.x, lifetime.y );
		particles.ApplyShape = true;
		particles.InitialScale = width;
		var renderer = go.AddComponent<ParticleStreaks>();
		renderer._particles = particles;
		renderer._material = material;
		renderer._length = length;
		renderer._batch = new SceneCustomObject( go.Scene.SceneWorld ) { RenderOverride = renderer.Draw };
		renderer._batch.Flags.IsOpaque = false;
		renderer._batch.Flags.IsTranslucent = true;
		renderer._batch.Flags.CastShadows = false;
		MountedEffects.UseViewModelLayer( renderer );
		renderer.RenderOptions.Apply( renderer._batch );
		particles.OnPostStep += renderer.UpdateBounds;
		go.Enabled = true;
		particles.Invoke( lifetime.y, particles.DestroyGameObject );
		return particles;
	}

	void UpdateBounds( float dt )
	{
		var bounds = _particles.ParticleBounds;
		var width = 0f;
		foreach ( var p in _particles.Particles )
		{
			bounds = bounds.AddPoint( End( p ) );
			width = MathF.Max( width, p.Size.x );
		}
		_batch.Bounds = bounds.Grow( width * 0.5f );
	}

	Vector3 End( Particle p ) => p.Position + p.Velocity * _length.Evaluate( p, 0 );

	void Draw( SceneObject obj )
	{
		if ( !_particles.IsValid() || !_material.IsValid() ) return;
		_vertices.Init( true );

		foreach ( var p in _particles.Particles )
		{
			_vertices.Default.Color = p.Color.WithAlpha( p.Color.a * p.Alpha );
			var end = End( p );
			var delta = end - p.Position;
			var across = delta.Cross( p.Position - Graphics.CameraPosition ).Normal;
			var halfWidth = across * (0.5f * MathF.Min( delta.Length, p.Size.x ));
			_vertices.AddQuad( p.Position + halfWidth, p.Position - halfWidth, end - halfWidth, end + halfWidth );
		}
		_vertices.Draw( _material );
	}

	protected override void OnEnabled()
	{
		if ( _batch.IsValid() ) _batch.RenderingEnabled = true;
	}

	protected override void OnDisabled()
	{
		if ( _batch.IsValid() ) _batch.RenderingEnabled = false;
	}

	protected override void OnDestroy()
	{
		if ( _particles.IsValid() ) _particles.OnPostStep -= UpdateBounds;
		_batch?.Delete();
	}
}
