using Halo2.Blam;
using System;

namespace Halo2;

internal sealed partial class ShotgunLoader
{
	enum ImpactLayer { Rocks, Smoke, Sparks }
	sealed record ImpactEmitter( ImpactLayer Layer, Sprite Sprite, Vector2 Count, Vector2 Lifetime, Vector2 Speed,
		Vector2 Cone, Vector2 Spin, Vector2 Size, CurveRange SmokeRadius, CurveRange SmokeAlpha, Color Tint, Color TintEnd,
		float Gravity, float Drag, float AngularDrag, Vector2 Collision );

	uint impactParticleSeed = (uint)System.Random.Shared.Next();

	ImpactEmitter[] ReadImpactEmitters( TagEntry contact, int row )
	{
		if ( !contact.TagPath.EndsWith( @"\hard_terrain_concrete", StringComparison.Ordinal ) || map.ReadInt32( row + 48 ) != 3 ) return null;
		var emitters = new ImpactEmitter[3];
		int system = Block( row + 48 );
		for ( int i = 0; i < emitters.Length; i++, system += 56 )
		{
			var name = Reference( system )?.TagPath switch
			{
				@"effects\generic\solids\rocks" => RocksName,
				@"effects\generic\smoke\smoke_soft_small_lit" => ImpactSmokeName,
				@"effects\generic\sparks\sparks_animated_soft" => SparksName,
				_ => null
			};
			if ( name is null || map.ReadInt32( system + 48 ) != 1 ) return null;
			var texture = Texture.Load( $"mount://{Halo2Mount.MountIdent}/{PrefabPath}/{name}" );
			if ( !texture.IsValid() || texture.IsError ) return null;
			var layer = name == RocksName ? ImpactLayer.Rocks : name == SparksName ? ImpactLayer.Sparks : ImpactLayer.Smoke;
			var sprite = Sprite.FromTexture( texture );
			if ( layer == ImpactLayer.Sparks ) sprite.Animations[0].Origin = new Vector2( 1 - 48f / 56f, 4f / 9f );
			int emitter = Block( system + 48 );
			Vector2 Bounds( int property )
			{
				int data = Block( emitter + property + 8 );
				return new Vector2( Float( data + 4 ), Float( data + 8 ) );
			}
			int controller = Block( Address( Reference( emitter ) ) + 12 );
			float Parameter( int control, int number ) => Float( Block( Block( control + 4 ) + number * 20 + 12 ) + 4 );
			int color = Block( emitter + 96 );
			Color Tint( int offset )
			{
				int rgb = map.ReadInt32( color + offset );
				return Color.FromBytes( (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb );
			}
			bool smoke = layer == ImpactLayer.Smoke;
			emitters[i] = new ImpactEmitter( layer, sprite, Bounds( 8 ), Bounds( 24 ), Bounds( 40 ) * 120, Bounds( 140 ), Bounds( 56 ),
				Bounds( 72 ) * 120, smoke ? ReadRange( emitter + 72, 120 ) : default, smoke ? ReadRange( emitter + 104, 1 ) : default,
				Tint( 4 ), Tint( smoke ? 16 : 4 ), Parameter( controller, 0 ), Parameter( controller, 1 ), Parameter( controller, 2 ),
				smoke ? default : new Vector2( Parameter( controller + 20, 0 ), Parameter( controller + 20, 1 ) ) );
		}
		return emitters;
	}

	float ParticleFraction() => BlamRandom.Next( ref impactParticleSeed ) * (1f / 65535f);
	static float Sample( Vector2 bounds, float t ) => bounds.x + (bounds.y - bounds.x) * t;

	void EmitImpactParticles( ImpactEmitter[] emitters, GameObject parent, Transform contact, Vector3 incoming )
	{
		// World effects initialize every system and location before allocating the first particle.
		Span<int> births = stackalloc int[emitters.Length];
		for ( int i = 0; i < emitters.Length; i++ )
		{
			float a = ParticleFraction(), b = ParticleFraction();
			BlamRandom.Next( ref impactParticleSeed ); // Location random byte
			births[i] = (int)(Sample( emitters[i].Count, emitters[i].Layer == ImpactLayer.Sparks ? b : a ) + 0.0001f);
		}
		for ( int i = 0; i < emitters.Length; i++ )
			EmitImpactLayer( emitters[i], births[i], parent, contact, incoming );
	}

	void EmitImpactLayer( ImpactEmitter data, int births, GameObject parent, Transform contact, Vector3 incoming )
	{
		if ( births == 0 ) return;
		bool smoke = data.Layer == ImpactLayer.Smoke, sparks = data.Layer == ImpactLayer.Sparks;
		var axis = -contact.Rotation.Forward;
		if ( sparks )
		{
			var ray = contact.Rotation * incoming;
			axis = ray - 2 * Vector3.Dot( ray, axis ) * axis;
		}
		var go = new GameObject( parent, false, $"Concrete impact {data.Layer}" );
		go.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		var up = Vector3.Cross( Vector3.Up, axis ).LengthSquared < 1e-8f ? Vector3.Left : Vector3.Up;
		go.WorldTransform = new Transform( contact.Position, Rotation.LookAt( axis, up ) );
		var effect = go.AddComponent<ParticleEffect>();
		effect.Collision = !smoke;
		effect.CollisionRadius = 0.01f;
		effect.Friction = data.Collision.x;
		effect.Bounce = data.Collision.y;
		effect.SnapToFrame = true;
		var renderer = go.AddComponent<ParticleSpriteRenderer>();
		renderer.Sprite = data.Sprite;
		renderer.Scale = sparks ? 1 : 2;
		renderer.Lighting = !sparks;
		renderer.Additive = sparks;
		if ( sparks ) renderer.Alignment = ParticleSpriteRenderer.BillboardAlignment.Particle;
		go.AddComponent<TemporaryEffect>().BecomeOrphan = true;

		void Appearance( Particle p )
		{
			float t = p.LifeDelta;
			float life = p.LifeTimeRemaining.Remap( data.Lifetime.x, data.Lifetime.y, 0, 1 );
			if ( smoke )
			{
				p.Size = data.SmokeRadius.Evaluate( t, life ).Clamp( data.Size.x, data.Size.y );
				p.Alpha = data.SmokeAlpha.Evaluate( t, life ).Clamp( 0, 0.4f );
				return;
			}
			float fade = ((1 - t) / 0.3f).Clamp( 0, 1 );
			p.Alpha = fade;
			if ( !sparks )
			{
				p.Size = Sample( data.Size, p.Get<float>( "size" ) );
				return;
			}
			float radius = Sample( data.Size, fade ) * ShotgunModel.SparkScale( t );
			ShotgunModel.OrientSpark( p, radius, go.Scene.Camera, go.WorldRotation.Up );
		}

		effect.OnParticleCreated += p =>
		{
			p.LifeDelta = 0;
			BlamRandom.Next( ref impactParticleSeed ); // Allocation bucket
			uint r0 = BlamRandom.Next( ref impactParticleSeed ), r1 = BlamRandom.Next( ref impactParticleSeed );
			float life = r0 * (1f / 65535f), fraction = r1 * (1f / 65535f), size = ParticleFraction();
			BlamRandom.Next( ref impactParticleSeed ); // Fourth particle random field
			p.DeathTime = p.BornTime + Sample( data.Lifetime, life );
			p.Set( "size", size );
			float cone = Sample( data.Cone, size ).DegreeToRadian();
			float azimuth = ParticleFraction() * MathF.Tau, polar = ParticleFraction() * cone;
			var direction = new Vector3( MathF.Cos( polar ), MathF.Sin( azimuth ) * MathF.Sin( polar ), MathF.Cos( azimuth ) * MathF.Sin( polar ) );
			p.Velocity = go.WorldRotation * direction.Normal * Sample( data.Speed, fraction );
			p.Color = Color.Lerp( data.Tint, data.TintEnd, fraction );
			p.Angles.roll = life * 360;
			p.Set( "spin", Sample( data.Spin, fraction ) );
			if ( data.Layer == ImpactLayer.Rocks ) p.Sequence = (int)(r1 % 4);
			Appearance( p );
		};
		effect.OnPostStep += dt =>
		{
			float drag = 1 - (data.Drag * dt).Clamp( 0, 1 );
			float gravity = 4.1712594f * 120 * data.Gravity * dt;
			foreach ( var p in effect.Particles )
			{
				p.Velocity = (p.Velocity - Vector3.Up * gravity) * drag;
				float spin = p.Get<float>( "spin" );
				p.Angles.roll += spin * dt;
				p.Set( "spin", spin * (1 - (data.AngularDrag * dt).Clamp( 0, 1 )) );
				Appearance( p );
			}
		};
		go.Enabled = true;
		for ( int i = 0; i < births; i++ ) effect.Emit( contact.Position, 0 );
	}
}
