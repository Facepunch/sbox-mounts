using System;

namespace Overgrowth;

// The fast and slow clouds in aschar share the quadratic particle simulation.
public sealed class ImpactDust : Component
{
	internal sealed record Definition( Sprite Sprite, float MinSize, float MaxSize, float MinAlpha, float MaxAlpha,
		float Expansion, float Persistence, float Inertia, float Wind );

	Definition definition;
	SpriteRenderer renderer;
	float diameter, opacity, spin, angle, age, simulated;
	Vector3 velocity, position;

	internal static void Emit( Definition definition, Vector3 position )
	{
		var go = new GameObject( false, "Overgrowth impact dust" ) { NetworkMode = NetworkMode.Never, WorldPosition = position };
		var cloud = go.AddComponent<ImpactDust>();
		cloud.definition = definition;
		cloud.diameter = Random.Shared.Float( definition.MinSize, definition.MaxSize ) * 40f;
		cloud.opacity = Random.Shared.Float( definition.MinAlpha, definition.MaxAlpha );
		cloud.spin = Random.Shared.Float( -360f, 360f );
		cloud.angle = Random.Shared.Next( -360, 360 );
		cloud.position = position;
		cloud.renderer = go.AddComponent<SpriteRenderer>();
		cloud.renderer.Sprite = definition.Sprite;
		cloud.renderer.Size = cloud.diameter;
		cloud.renderer.Color = Color.White.WithAlpha( cloud.opacity );
		go.WorldRotation = Rotation.FromRoll( cloud.angle );
		cloud.renderer.AlphaCutoff = 0f;
		cloud.renderer.Lighting = true;
		go.Enabled = true;
	}

	protected override void OnUpdate()
	{
		float dt = Time.Delta;
		if ( dt <= 0f ) return;
		age += dt;
		float expansion = MathF.Sqrt( age * definition.Expansion );
		float dispersion = definition.Persistence / ((1f + expansion) * (1f + expansion));
		float alpha = Math.Min( 1f, dispersion ) * Math.Min( (age + 0.05f) * 10f, 1f ) * opacity - 0.1f;
		if ( alpha <= 0f )
		{
			GameObject.Destroy();
			return;
		}
		renderer.Size = diameter * expansion;
		renderer.Color = Color.White.WithAlpha( alpha );
		const float step = 1f / 120f;
		while ( simulated + step <= age )
		{
			simulated += step;
			position += velocity * step;
			velocity = velocity * definition.Inertia + Wind( position / 40f, Time.Now - age + simulated ) * (40f * definition.Wind * step);
			angle += step * spin * 0.3f / MathF.Sqrt( simulated * definition.Expansion );
		}
		float remainder = age - simulated;
		WorldPosition = position + velocity * remainder;
		WorldRotation = Rotation.FromRoll( angle + remainder * spin * 0.3f / MathF.Sqrt( (simulated + step) * definition.Expansion ) );
	}

	static Vector3 Wind( Vector3 p, float time )
	{
		float x = p.y + time * 0.7f, y = p.z + time * 0.3f, z = p.x + time * 0.5f;
		return new Vector3( MathF.Sin( x * 1.6f ) + MathF.Cos( y * 0.5f ) + MathF.Sin( z * 1.2f ),
			MathF.Sin( x ) + MathF.Cos( y * 1.3f ) + MathF.Sin( z * 3f ),
			MathF.Sin( x * 1.2f ) + MathF.Cos( y * 1.8f ) + MathF.Sin( z * 0.8f ) );
	}
}

public partial class GameMount
{
	readonly Dictionary<string, ImpactDust.Definition> impactDust = new();

	internal void EmitImpactDust( string path, Vector3 position )
	{
		if ( !impactDust.TryGetValue( path, out var definition ) )
		{
			var xml = XmlAsset.Read( FileSystem, path, "particle" );
			var size = xml.Element( "size" );
			var color = xml.Element( "color" );
			var behavior = xml.Element( "behavior" );
			definition = new( Sprite.FromTexture( LoadTexture( xml.Element( "textures" ).Attribute( "color_map" ) ) ),
				size.Number( "min" ).Value, size.Number( "max" ).Value, color.Number( "alpha_min" ).Value, color.Number( "alpha_max" ).Value,
				xml.Element( "quadratic_expansion" ).Number( "speed" ).Value, xml.Element( "quadratic_dispersion" ).Number( "persistence" ).Value,
				behavior.Number( "inertia" ).Value, behavior.Number( "wind" ).Value );
			impactDust.Add( path, definition );
		}
		ImpactDust.Emit( definition, position );
	}
}
