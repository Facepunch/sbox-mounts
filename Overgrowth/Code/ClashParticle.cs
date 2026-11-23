using System;

namespace Overgrowth;

public sealed class ClashParticle : Component
{
	internal sealed record Definition( Sprite Sprite, Color Tint, bool Additive, float MinSize, float MaxSize,
		float Inertia, float Gravity, float Wind, float SizeDecay, float OpacityDecay );

	Definition particle;
	SpriteRenderer sprite;
	Vector3 position, velocity;
	float diameter, age, remainder;

	internal static void Emit( Definition particle, Vector3 point )
	{
		var go = new GameObject( false, "Weapon clash particle" ) { WorldPosition = point, NetworkMode = NetworkMode.Never };
		var effect = go.AddComponent<ClashParticle>();
		effect.particle = particle;
		effect.position = point;
		effect.velocity = new Vector3( Random.Shared.Float( -200f, 200f ), Random.Shared.Float( -200f, 200f ), Random.Shared.Float( -200f, 200f ) );
		effect.diameter = Random.Shared.Float( particle.MinSize, particle.MaxSize ) * 40f;
		effect.sprite = go.AddComponent<SpriteRenderer>();
		effect.sprite.Sprite = particle.Sprite;
		effect.sprite.Color = particle.Tint;
		effect.sprite.Size = effect.diameter;
		effect.sprite.AlphaCutoff = 0f;
		effect.sprite.Additive = particle.Additive;
		effect.sprite.Lighting = !particle.Additive;
		effect.sprite.Billboard = SpriteRenderer.BillboardMode.None;
		go.Enabled = true;
	}

	protected override void OnUpdate()
	{
		age += Time.Delta;
		float size = diameter - 80f * age * particle.SizeDecay;
		float alpha = particle.Tint.a - age * particle.OpacityDecay;
		if ( size <= 0f || alpha <= 0f )
		{
			GameObject.Destroy();
			return;
		}

		const float tick = 1f / 120f;
		remainder += Time.Delta;
		while ( remainder >= tick )
		{
			position += velocity * tick;
			velocity *= particle.Inertia;
			velocity += (Vector3.Down * (392f * particle.Gravity) + ImpactDust.Wind( position / 40f, Time.Now - remainder ) * (40f * particle.Wind)) * tick;
			remainder -= tick;
		}
		WorldPosition = position + velocity * remainder;
		sprite.Color = particle.Tint.WithAlpha( alpha );
		var camera = Scene.Camera;
		if ( !camera.IsValid() ) return;
		var facing = (camera.WorldPosition - WorldPosition).Normal;
		var up = velocity.Normal;
		float stretch = 2f * MathF.Sqrt( velocity.Length / 40f );
		if ( stretch <= 0f ) return;
		var right = up.Cross( facing ).Normal;
		if ( right.IsNearlyZero() ) right = camera.WorldRotation.Right;
		float limit = Math.Clamp( 1f - 0.2f / (stretch * stretch), -1f, 1f );
		if ( MathF.Abs( facing.Dot( up ) ) > limit )
			up = Rotation.FromAxis( right, MathF.Acos( limit ).RadianToDegree() ) * facing;
		WorldRotation = Rotation.LookAt( up.Cross( right ), up );
		sprite.Size = new Vector2( size, size * stretch );
	}
}

public partial class GameMount
{
	readonly Dictionary<string, ClashParticle.Definition> clashParticles = new();
	float? clashFlashRadius;

	internal void MetalSparks( Vector3 point )
	{
		int count = Random.Shared.Next( 20 );
		EmitClashParticles( "metalspark", point, count );
		EmitClashParticles( "metalflash", point, count );
		EmitClashFlash( point );
	}

	void EmitClashFlash( Vector3 point )
	{
		if ( !clashFlashRadius.HasValue )
		{
			var light = XmlAsset.Read( FileSystem, "Data/Objects/default_light.xml", "ActorObjects" ).Element( "DynamicLightObject" );
			clashFlashRadius = 40f * MathF.Min( light.Number( "s0" ).Value, MathF.Min( light.Number( "s1" ).Value, light.Number( "s2" ).Value ) );
		}
		ClashFlash.Emit( point, clashFlashRadius.Value );
	}

	internal void EmitClashParticles( string name, Vector3 point, int count )
	{
		if ( count == 0 ) return;
		if ( !clashParticles.TryGetValue( name, out var particle ) )
		{
			var root = XmlAsset.Read( FileSystem, $"Data/Particles/{name}.xml", "particle" );
			var texture = root.Element( "textures" );
			var size = root.Element( "size" );
			var color = root.Element( "color" );
			var behavior = root.Element( "behavior" );
			var tint = new Color( color.Number( "red" ).Value, color.Number( "green" ).Value, color.Number( "blue" ).Value, color.Number( "alpha" ).Value );
			particle = new( Sprite.FromTexture( LoadTexture( texture.Attribute( "color_map" ) ) ), tint, texture.Attribute( "shader" ) == "glowsprite",
				size.Number( "min" ).Value, size.Number( "max" ).Value, behavior.Number( "inertia" ).Value, behavior.Number( "gravity" ).Value,
				behavior.Number( "wind" ).Value, behavior.Number( "size_decay_rate" ).Value, behavior.Number( "opacity_decay_rate" ).Value );
			clashParticles.Add( name, particle );
		}
		for ( int i = 0; i < count; i++ ) ClashParticle.Emit( particle, point );
	}
}
