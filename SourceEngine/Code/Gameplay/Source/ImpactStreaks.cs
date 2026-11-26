using System;

namespace Source1;

/// <summary>Draws a batch of impact streaks from each particle outward along its current velocity.</summary>
[Hide]
public sealed class ImpactStreaks : Component
{
	readonly VertexBuffer _vertices = new();
	ParticleEffect _particles;
	SceneCustomObject _batch;
	Material _material;
	ParticleFloat _length;

	internal void Initialize( ParticleEffect particles, Material material, ParticleFloat length, float radius )
	{
		_particles = particles;
		_material = material;
		_length = length;
		_batch = new SceneCustomObject( Scene.SceneWorld ) { RenderOverride = Draw };
		_batch.Flags.IsOpaque = false;
		_batch.Flags.IsTranslucent = true;
		_batch.Flags.CastShadows = false;
		// Bounds include travel and the forward extension at maximum launch speed.
		_batch.Bounds = BBox.FromPositionAndSize( WorldPosition, radius * 2 );
	}

	void Draw( SceneObject obj )
	{
		if ( !_particles.IsValid() || !_material.IsValid() ) return;
		_vertices.Init( true );
		_vertices.Default.Color = Color.White;
		foreach ( var p in _particles.Particles )
		{
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
