using System;

namespace Overgrowth;

public sealed class BillboardParticle : Component
{
	internal sealed record Definition( Sprite Sprite, float MinSize, float MaxSize, float MinAlpha, float MaxAlpha,
		float Expansion, float? Persistence, float Inertia, float Wind, Color Color, float Gravity, float SizeDecay,
		float OpacityDecay, float OpacityRampTime, DecalDefinition Decal, DecalDefinition WetSurface, float DecalScale, string MaterialEvent, bool DestroyOnCollision,
		bool CharacterCollide, bool CharacterAddBlood, bool VelocityAxis, bool SpeedStretch, bool MinSquash, float SpeedMultiplier, float MinSpin, float MaxSpin, bool NoRotation, bool Glow );

	Definition definition;
	ParticleSpriteRenderer renderer;
	Particle particle;
	float diameter, opacity, spin, angle, age, simulated;
	Vector3 velocity, position;
	Color tint;
	bool collided;
	BillboardParticle previousDrop, nextDrop;
	internal void Connect( BillboardParticle next )
	{
		nextDrop = next;
		next.previousDrop = this;
	}
	Vector3? lastConnectedPosition;

	internal static void EmitSkid( string surface, Vector3 point, in SceneTraceResult terrainHit )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount && mount.MaterialParticle( surface, "skid" ) is { } path )
			RabbitGuard.EmitSurfaceParticle( path, point, 40f, TerrainLoader.ContactColor( terrainHit ) );
	}

	internal static BillboardParticle Emit( Definition definition, Vector3 position, Vector3 velocity, Color tint )
	{
		var go = new GameObject( false, "Overgrowth particle" ) { NetworkMode = NetworkMode.Never, WorldPosition = position };
		var cloud = go.AddComponent<BillboardParticle>();
		cloud.definition = definition;
		cloud.diameter = Random.Shared.Float( definition.MinSize, definition.MaxSize ) * 40f;
		cloud.opacity = Random.Shared.Float( definition.MinAlpha, definition.MaxAlpha );
		if ( !definition.NoRotation )
		{
			cloud.spin = Random.Shared.Float( definition.MinSpin, definition.MaxSpin );
			cloud.angle = Random.Shared.Next( -360, 360 );
		}
		cloud.position = position;
		cloud.velocity = velocity;
		cloud.tint = definition.Color * tint;
		var effect = go.AddComponent<ParticleEffect>();
		cloud.renderer = go.AddComponent<ParticleSpriteRenderer>();
		cloud.renderer.Sprite = definition.Sprite;
		cloud.renderer.Lighting = !definition.Glow;
		if ( definition.Glow ) cloud.tint *= 5f;
		// envobject fades across 0.2 to 2.2 metres at the camera.
		cloud.renderer.CameraFadeNear = 8f;
		cloud.renderer.CameraFadeFar = 88f;
		// Simulate ends the effect when its authored size or opacity reaches zero.
		effect.Lifetime = float.MaxValue;
		go.Enabled = true;
		cloud.particle = effect.Emit( position, 0f );
		cloud.Draw( position, cloud.diameter, definition.OpacityRampTime > 0f ? 0f : cloud.opacity, cloud.angle );
		effect.OnPreStep = cloud.Simulate;
		return cloud;
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
			if ( nextDrop.IsValid() ) lastConnectedPosition = nextDrop.position;
			else if ( previousDrop.IsValid() ) lastConnectedPosition = previousDrop.position;
			if ( !collided && definition.Decal is not null ) CheckCollision( last, SizeAt( simulated ) );
			if ( collided && definition.DestroyOnCollision )
			{
				GameObject.Destroy();
				return;
			}
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
			var toward = camera.View.Position - center;
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
		if ( nextDrop.IsValid() && camera.IsValid() )
		{
			var start = position + velocity * (age - simulated);
			var end = nextDrop.position + nextDrop.velocity * (nextDrop.age - nextDrop.simulated);
			var axis = end - start;
			var midpoint = (start + end) * 0.5f;
			var facing = axis.Cross( (camera.View.Position - midpoint).Cross( axis ) );
			if ( !facing.IsNearlyZero() )
			{
				renderer.Alignment = ParticleSpriteRenderer.BillboardAlignment.Particle;
				particle.Position = midpoint;
				particle.Angles = Rotation.LookAt( facing.Normal, axis.Normal ).Angles();
				particle.Size = new Vector3( SizeAt( age ), axis.Length * 2f, 1f );
				return;
			}
		}
		// The earlier drop draws the segment. Its other endpoint has no standalone sprite.
		if ( nextDrop.IsValid() || previousDrop.IsValid() )
		{
			particle.Alpha = 0f;
			return;
		}
		if ( definition.VelocityAxis && camera.IsValid() && !velocity.IsNearlyZero() )
		{
			var look = (camera.View.Position - center).Normal;
			var up = velocity.Normal;
			float length = definition.SpeedStretch ? 2f * MathF.Sqrt( velocity.Length / 40f ) * definition.SpeedMultiplier : 1f;
			var side = up - look * up.Dot( look );
			if ( side.IsNearlyZero() ) side = camera.View.Rotation.Up;
			if ( definition.MinSquash && length > 0f )
			{
				float limit = definition.SpeedStretch ? Math.Clamp( 1f - 0.2f / (length * length), -1f, 1f ) : 0.8f;
				if ( MathF.Abs( up.Dot( look ) ) > limit )
					up = look * limit + side.Normal * MathF.Sqrt( 1f - limit * limit );
			}
			var normal = look - up * look.Dot( up );
			if ( normal.IsNearlyZero() ) normal = side;
			renderer.Alignment = ParticleSpriteRenderer.BillboardAlignment.Particle;
			particle.Angles = Rotation.LookAt( normal.Normal, up ).Angles();
			particle.Size = new Vector3( size, size * length, 1f );
			return;
		}
		renderer.Alignment = ParticleSpriteRenderer.BillboardAlignment.LookAtCamera;
	}

	float SizeAt( float time ) => definition.Expansion > 0f
		? diameter * MathF.Sqrt( time * definition.Expansion ) : diameter - 80f * definition.SizeDecay * time;

	void CheckCollision( Vector3 last, float size )
	{
		var trace = Scene.Trace.Ray( last, position ).WithoutTags( "playercontroller", "trigger", CharacterCollisionRules.FighterHull );
		var hit = (definition.CharacterCollide ? trace.UseHitboxes() : trace.WithoutTags( CharacterCollisionRules.RagdollPart )).Run();
		if ( !hit.Hit || hit.StartedSolid ) return;
		collided = true;
		float gain = Math.Min( 1f, (velocity / 40f).LengthSquared * 0.2f * (size / 80f) );
		var mount = Sandbox.Mounting.Directory.Get( "overgrowth" ) as GameMount;
		if ( definition.CharacterCollide )
		{
			var actor = hit.GameObject.GetComponentInParent<RabbitGuard>();
			var character = actor?.Renderer ?? hit.GameObject.GetComponentInParent<CharacterRemains>()?.GetComponent<SkinnedModelRenderer>();
			if ( character.IsValid() )
			{
				if ( definition.CharacterAddBlood && RabbitGuard.BloodLevel == 2 )
					character.GameObject.GetOrAddComponent<CutMarks>().AddDrip( hit.EndPosition );
				mount?.PlaySurfaceEvent( character.GameObject, "default", definition.MaterialEvent, hit.EndPosition, gain, null );
				return;
			}
		}
		float width = size * 0.5f * definition.DecalScale;
		var go = new GameObject( false, "Overgrowth blood spatter" ) { NetworkMode = NetworkMode.Never,
			WorldPosition = hit.EndPosition, WorldRotation = Rotation.LookAt( -hit.Normal ) };
		var decal = go.AddComponent<Decal>();
		decal.Decals = [definition.Decal];
		decal.Size = width;
		decal.Depth = width;
		if ( lastConnectedPosition is { } neighbor )
		{
			var along = neighbor - position;
			float length = width + Math.Min( along.Length, width * 3f );
			var tangent = along - hit.Normal * along.Dot( hit.Normal );
			if ( !tangent.IsNearlyZero() ) go.WorldRotation = Rotation.LookAt( -hit.Normal, tangent.Normal );
			go.WorldPosition = (position + neighbor) * 0.5f;
			decal.Size = new Vector2( width, length );
			decal.Depth = length;
		}
		if ( definition.WetSurface is { } wetSurface )
			go.AddComponent<BloodSpatter>().Initialize( decal, wetSurface, RabbitGuard.BloodColor, lastConnectedPosition.HasValue ? 0f : null );
		else
			decal.ColorTint = tint;
		decal.Transient = true;
		go.AddComponent<TemporaryEffect>();
		go.Enabled = true;
		if ( mount is not null )
		{
			var surface = TerrainLoader.ContactSurface( hit.Collider as ModelCollider, hit.EndPosition, hit.Surface )?.ResourceName;
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

	internal BillboardParticle EmitBillboard( string path, Vector3 position, Vector3 velocity, Color tint ) =>
		BillboardParticle.Emit( GetBillboard( path ), position, velocity, tint );

	internal BillboardParticle.Definition GetBillboard( string path )
	{
		if ( !billboards.TryGetValue( path, out var definition ) )
		{
			var xml = XmlAsset.Read( FileSystem, path, "particle" );
			var size = xml.Element( "size" );
			var color = xml.Element( "color" );
			var behavior = xml.Element( "behavior" );
			var collision = xml.Element( "collision" );
			var stretch = xml.Element( "stretch" );
			var rotation = xml.Element( "rotation" );
			var textures = xml.Element( "textures" );
			string shader = textures.Attribute( "soft_shader" ) ?? textures.Attribute( "shader" );
			DecalDefinition decal = null;
			DecalDefinition wetSurface = null;
			if ( collision?.Attribute( "decal" ) is { } decalPath )
			{
				var asset = XmlAsset.Read( FileSystem, decalPath, "DecalObject" );
				decal = new() { ColorTexture = LoadTexture( asset.Element( "ColorMap" ).Value ), NormalTexture = LoadTexture( asset.Element( "NormalMap" ).Value ), Width = 1f, Height = 1f };
				if ( asset.Element( "SpecialType" )?.Value == "2" )
					wetSurface = BloodSpatter.WetSurface( decal.ColorTexture );
			}
			definition = new( Sprite.FromTexture( LoadTexture( textures.Attribute( "color_map" ) ) ),
				size.Number( "min" ).Value, size.Number( "max" ).Value, color.Number( "alpha" ) ?? color.Number( "alpha_min" ) ?? 1f, color.Number( "alpha" ) ?? color.Number( "alpha_max" ) ?? 1f,
				xml.Element( "quadratic_expansion" )?.Number( "speed" ) ?? 0f, xml.Element( "quadratic_dispersion" )?.Number( "persistence" ),
				behavior.Number( "inertia" ).Value, behavior.Number( "wind" ).Value,
				new Color( color.Number( "red" ) ?? 1f, color.Number( "green" ) ?? 1f, color.Number( "blue" ) ?? 1f ),
				behavior.Number( "gravity" ) ?? 0f, behavior.Number( "size_decay_rate" ) ?? 0f, behavior.Number( "opacity_decay_rate" ) ?? 0f,
				behavior.Number( "opacity_ramp_time" ) ?? 0f, decal, wetSurface, collision?.Number( "decal_size_mult" ) ?? 1f, collision?.Attribute( "materialevent" ),
				collision?.Attribute( "destroy" ) == "true", collision?.Attribute( "character_collide" ) == "true",
				collision?.Attribute( "character_add_blood" ) == "true", stretch?.Attribute( "velocity_axis" ) == "true",
				stretch?.Attribute( "speed_stretch" ) == "true", stretch?.Attribute( "min_squash" ) == "true", stretch?.Number( "speed_mult" ) ?? 1f,
				rotation?.Number( "val" ) ?? rotation?.Number( "min" ) ?? -360f, rotation?.Number( "val" ) ?? rotation?.Number( "max" ) ?? 360f,
				xml.Element( "no_rotation" ) is not null, shader?.Split( ' ' ).Contains( "#GLOW" ) == true );
			billboards.Add( path, definition );
		}
		return definition;
	}
}
