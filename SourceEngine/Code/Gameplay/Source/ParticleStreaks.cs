using System;

namespace Source1;

/// <summary>Draws a batch of streaks from each particle outward along its current velocity.</summary>
[Hide]
public sealed class ParticleStreaks : Component
{
	readonly VertexBuffer _vertices = new();
	ParticleEffect _particles;
	SceneCustomObject _batch;
	Material _material;
	ParticleFloat _length;

	internal static ParticleEffect Create( Vector3 origin, string mount, string name, int count, Vector2 lifetime,
		ParticleFloat width, ParticleFloat length, float radius )
	{
		var material = Material.Load( $"mount://{mount}/materials/{name}.vmt.vmat" );
		if ( !material.IsValid() || material.IsError ) return null;
		var go = new GameObject( false, name ) { NetworkMode = NetworkMode.Never, WorldPosition = origin };
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
		renderer._batch.Bounds = BBox.FromPositionAndSize( origin, radius * 2 );
		go.Enabled = true;
		particles.Invoke( lifetime.y, particles.DestroyGameObject );
		return particles;
	}

	void Draw( SceneObject obj )
	{
		if ( !_particles.IsValid() || !_material.IsValid() ) return;
		_vertices.Init( true );

		foreach ( var p in _particles.Particles )
		{
			_vertices.Default.Color = p.Color.WithAlpha( p.Color.a * p.Alpha );
			var delta = p.Velocity * _length.Evaluate( p, 0 );
			var across = delta.Cross( p.Position - Graphics.CameraPosition ).Normal;
			var halfWidth = across * (0.5f * MathF.Min( delta.Length, p.Size.x ));
			var end = p.Position + delta;
			_vertices.AddQuad( p.Position + halfWidth, p.Position - halfWidth, end - halfWidth, end + halfWidth );
		}
		_vertices.Draw( _material );
	}

	protected override void OnDestroy() => _batch?.Delete();
}
