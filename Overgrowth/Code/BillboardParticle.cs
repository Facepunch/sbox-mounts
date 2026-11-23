using System;

namespace Overgrowth;

public sealed class BillboardParticle : Component
{
	internal sealed record Definition( Sprite Sprite, float MinSize, float MaxSize, float MinAlpha, float MaxAlpha,
		float Expansion, float? Persistence, float Inertia, float Wind, Color Color, float Gravity, float SizeDecay,
		float OpacityDecay, float OpacityRampTime, DecalDefinition Decal, DecalDefinition WetSurface, float DecalScale, string MaterialEvent );

	Definition definition;
	ParticleSpriteRenderer renderer;
	Particle particle;
	float diameter, opacity, spin, angle, age, simulated;
	Vector3 velocity, position;
	Color tint;
	bool collided;

	internal static void Emit( Definition definition, Vector3 position, Vector3 velocity, Color tint )
	{
		var go = new GameObject( false, "Overgrowth particle" ) { NetworkMode = NetworkMode.Never, WorldPosition = position };
		var cloud = go.AddComponent<BillboardParticle>();
		cloud.definition = definition;
		cloud.diameter = Random.Shared.Float( definition.MinSize, definition.MaxSize ) * 40f;
		cloud.opacity = Random.Shared.Float( definition.MinAlpha, definition.MaxAlpha );
		cloud.spin = Random.Shared.Float( -360f, 360f );
		cloud.angle = Random.Shared.Next( -360, 360 );
		cloud.position = position;
		cloud.velocity = velocity;
		cloud.tint = definition.Color * tint;
		var effect = go.AddComponent<ParticleEffect>();
		cloud.renderer = go.AddComponent<ParticleSpriteRenderer>();
		cloud.renderer.Sprite = definition.Sprite;
		cloud.renderer.Lighting = true;
		// envobject fades across 0.2 to 2.2 metres at the camera.
		cloud.renderer.CameraFadeNear = 8f;
		cloud.renderer.CameraFadeFar = 88f;
		// Simulate ends the effect when its authored size or opacity reaches zero.
		effect.Lifetime = float.MaxValue;
		go.Enabled = true;
		cloud.particle = effect.Emit( position, 0f );
		cloud.Draw( position, cloud.diameter, definition.OpacityRampTime > 0f ? 0f : cloud.opacity, cloud.angle );
		effect.OnPreStep = cloud.Simulate;
	}

	void Simulate( float dt )
	{
		if ( dt <= 0f ) return;
		age += dt;
		float size = SizeAt( age );
		float ratio = 1f + size / diameter;
		float alpha = definition.Persistence is { } persistence
			? Math.Min( 1f, persistence / (ratio * ratio) ) * Math.Min( (age + 0.05f) * 10f, 1f ) * opacity - 0.1f
			: opacity - age * definition.OpacityDecay;
		if ( definition.Persistence is null && age < definition.OpacityRampTime )
			alpha *= age / definition.OpacityRampTime;
		if ( alpha <= 0f || size <= 0f )
		{
			GameObject.Destroy();
			return;
		}
		const float step = 1f / 120f;
		while ( simulated + step <= age )
		{
			simulated += step;
			var last = position;
			position += velocity * step;
			velocity = velocity * definition.Inertia + (Vector3.Down * (392f * definition.Gravity) + Wind( position / 40f, Time.Now - age + simulated ) * (40f * definition.Wind)) * step;
			angle += step * spin * 0.3f * diameter / SizeAt( simulated );
			if ( !collided && definition.Decal is not null ) CheckCollision( last, SizeAt( simulated ) );
		}
		float remainder = age - simulated;
		Draw( position + velocity * remainder, size, alpha, angle + remainder * spin * 0.3f * diameter / SizeAt( simulated + step ) );
	}

	void Draw( Vector3 center, float size, float alpha, float roll )
	{
		renderer.DepthFeather = size;
		var camera = Scene.Camera;
		if ( camera.IsValid() )
		{
			var toward = camera.WorldPosition - center;
			float distance = toward.Length;
			if ( distance > 0f )
			{
				// Particle::Draw offsets toward the eye by half the radius, preserving projected size.
				float fraction = Math.Min( 1f, size * 0.25f / distance );
				center += toward * fraction;
				size *= 1f - fraction;
			}
		}
		particle.Position = center;
		particle.Size = size;
		particle.Angles = new Angles( 0f, 0f, roll );
		particle.Color = tint.WithAlpha( 1f );
		particle.Alpha = alpha;
	}

	float SizeAt( float time ) => definition.Expansion > 0f
		? diameter * MathF.Sqrt( time * definition.Expansion ) : diameter - 80f * definition.SizeDecay * time;

	void CheckCollision( Vector3 last, float size )
	{
		var hit = Scene.Trace.Ray( last, position ).WithoutTags( "playercontroller", "trigger", LevelItem.FighterHull ).Run();
		if ( !hit.Hit || hit.StartedSolid ) return;
		collided = true;
		float width = size * 0.5f * definition.DecalScale;
		var go = new GameObject( false, "Overgrowth blood spatter" ) { NetworkMode = NetworkMode.Never,
			WorldPosition = hit.EndPosition, WorldRotation = Rotation.LookAt( -hit.Normal ) };
		var decal = go.AddComponent<Decal>();
		decal.Decals = [definition.Decal];
		decal.Size = width;
		decal.Depth = width;
		if ( definition.WetSurface is { } wetSurface )
			go.AddComponent<BloodSpatter>().Initialize( decal, wetSurface, RabbitGuard.BloodColor );
		else
			decal.ColorTint = tint;
		decal.Transient = true;
		go.AddComponent<TemporaryEffect>();
		go.Enabled = true;
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount )
		{
			var surface = TerrainLoader.ContactSurface( hit.Collider as ModelCollider, hit.EndPosition, hit.Surface )?.ResourceName;
			float gain = Math.Min( 1f, (velocity / 40f).LengthSquared * 0.2f * (size / 80f) );
			mount.PlaySurfaceEvent( go, surface, definition.MaterialEvent, hit.EndPosition, gain, null );
		}
	}

	internal static Vector3 Wind( Vector3 p, float time )
	{
		float x = p.y + time * 0.7f, y = p.z + time * 0.3f, z = p.x + time * 0.5f;
		return new Vector3( MathF.Sin( x * 1.6f ) + MathF.Cos( y * 0.5f ) + MathF.Sin( z * 1.2f ),
			MathF.Sin( x ) + MathF.Cos( y * 1.3f ) + MathF.Sin( z * 3f ),
			MathF.Sin( x * 1.2f ) + MathF.Cos( y * 1.8f ) + MathF.Sin( z * 0.8f ) );
	}
}

public partial class GameMount
{
	readonly Dictionary<string, BillboardParticle.Definition> billboards = new();

	internal void EmitImpactDust( string path, Vector3 position ) => EmitBillboard( path, position, Vector3.Zero, Color.White );

	internal void EmitBillboard( string path, Vector3 position, Vector3 velocity, Color tint )
	{
		if ( !billboards.TryGetValue( path, out var definition ) )
		{
			var xml = XmlAsset.Read( FileSystem, path, "particle" );
			var size = xml.Element( "size" );
			var color = xml.Element( "color" );
			var behavior = xml.Element( "behavior" );
			var collision = xml.Element( "collision" );
			DecalDefinition decal = null;
			DecalDefinition wetSurface = null;
			if ( collision?.Attribute( "decal" ) is { } decalPath )
			{
				var asset = XmlAsset.Read( FileSystem, decalPath, "DecalObject" );
				decal = new() { ColorTexture = LoadTexture( asset.Element( "ColorMap" ).Value ), NormalTexture = LoadTexture( asset.Element( "NormalMap" ).Value ), Width = 1f, Height = 1f };
				if ( asset.Element( "SpecialType" )?.Value == "2" )
					wetSurface = BloodSpatter.WetSurface( decal.ColorTexture );
			}
			definition = new( Sprite.FromTexture( LoadTexture( xml.Element( "textures" ).Attribute( "color_map" ) ) ),
				size.Number( "min" ).Value, size.Number( "max" ).Value, color.Number( "alpha_min" ).Value, color.Number( "alpha_max" ).Value,
				xml.Element( "quadratic_expansion" )?.Number( "speed" ) ?? 0f, xml.Element( "quadratic_dispersion" )?.Number( "persistence" ),
				behavior.Number( "inertia" ).Value, behavior.Number( "wind" ).Value,
				new Color( color.Number( "red" ) ?? 1f, color.Number( "green" ) ?? 1f, color.Number( "blue" ) ?? 1f ),
				behavior.Number( "gravity" ) ?? 0f, behavior.Number( "size_decay_rate" ) ?? 0f, behavior.Number( "opacity_decay_rate" ) ?? 0f,
				behavior.Number( "opacity_ramp_time" ) ?? 0f, decal, wetSurface, collision?.Number( "decal_size_mult" ) ?? 1f, collision?.Attribute( "materialevent" ) );
			billboards.Add( path, definition );
		}
		BillboardParticle.Emit( definition, position, velocity, tint );
	}
}
