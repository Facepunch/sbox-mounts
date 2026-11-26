namespace Halo2;

public sealed record MuzzleFlashDefinition( string Marker, MuzzleFlashLayer[] Layers );

public sealed record MuzzleSmoke( CurveRange Radius, CurveRange Alpha, Vector2 Opacity );

public sealed record MuzzleFlashLayer( string Texture, Vector2 Pivot, Vector2 Aspect,
	Vector2 Lifetime, CurveRange Radius, CurveRange Spin, CurveRange Frame, Color StartColor, Color EndColor,
	Vector3 Offset, Rotation Direction, bool Parallel, int CameraMode, bool LocalSpace,
	Vector2 BirthCount, Vector2 SpawnRadius, Vector2 Speed, bool RandomSize, MuzzleSmoke Smoke, CasingSounds Casing, float ConeDegrees );

public sealed class MuzzleFlash : Component
{
	ParticleEffect[] layers;
	uint seed = (uint)System.Random.Shared.Next();

	float Sample( Vector2 bounds ) => bounds.x == bounds.y ? bounds.x : MathX.Lerp( bounds.x, bounds.y, BlamRandom.Next( ref seed ) / 65535f );

	internal void Emit( MuzzleFlashDefinition data, bool firstPerson, GameObject holder )
	{
		layers ??= new ParticleEffect[data.Layers.Length];
		for ( int i = 0; i < layers.Length; i++ )
		{
			var spec = data.Layers[i];
			if ( spec.CameraMode == (firstPerson ? 2 : 1) ) continue;
			if ( !layers[i].IsValid() ) layers[i] = CreateLayer( spec, firstPerson, holder );
			var layer = layers[i];
			if ( !layer.IsValid() ) continue;
			if ( spec.Casing is not null ) layer.WorldTransform = WorldTransform.ToWorld( new Transform( spec.Offset, spec.Direction ) );
			int count = (int)(Sample( spec.BirthCount ) + 0.0001f);
			for ( int j = 0; j < count; j++ ) layer.Emit( layer.WorldPosition, 0 );
		}
	}

	ParticleEffect CreateLayer( MuzzleFlashLayer data, bool firstPerson, GameObject holder )
	{
		var texture = Texture.Load( data.Texture );
		if ( !texture.IsValid() || texture.IsError ) return null;
		var sprite = Sprite.FromTexture( texture );
		sprite.Animations[0].Origin = data.Pivot;
		var go = data.Casing is not null ? Scene.CreateObject( false ) : new GameObject( GameObject, false, "Muzzle flash" );
		if ( data.Casing is not null )
		{
			go.Name = "Ejected casings";
			go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0;
		}
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.LocalPosition = data.Offset;
		go.LocalRotation = data.Direction;
		var effect = go.AddComponent<ParticleEffect>();
		effect.Lifetime = new ParticleFloat( data.Lifetime.x, data.Lifetime.y );
		effect.LocalSpace = data.LocalSpace ? 1 : 0;
		var renderer = go.AddComponent<ParticleSpriteRenderer>();
		renderer.Sprite = sprite;
		renderer.Scale = 2;
		renderer.Additive = data.Smoke is null && data.Casing is null;
		renderer.Lighting = data.Casing is not null;
		if ( data.Parallel ) renderer.Alignment = ParticleSpriteRenderer.BillboardAlignment.Particle;
		renderer.FogStrength = 0;
		renderer.RenderOptions.Game = data.Casing is not null || !firstPerson;
		renderer.RenderOptions.Overlay = data.Casing is null && firstPerson;
		void Appearance( Particle p )
		{
			float t = p.LifeDelta;
			p.Size = data.Aspect * data.Radius.Evaluate( data.RandomSize ? p.Rand( 1, 0 ) : t, p.Rand( 1, 0 ) );
			if ( data.Smoke is {} smoke )
			{
				p.Size *= smoke.Radius.Evaluate( t, p.Rand( 4, 0 ) );
				p.Alpha = smoke.Alpha.Evaluate( t, 0 ).Clamp( 0, 1 ) * MathX.Lerp( smoke.Opacity.x, smoke.Opacity.y, p.Rand( 5, 0 ) );
			}
			p.Color = Color.Lerp( data.StartColor, data.EndColor, t );
			p.Sequence = 0;
			p.SequenceTime = new Vector3( data.Frame.Evaluate( t, 0 ), 0, 0 );
			if ( data.Parallel && Scene.Camera.IsValid() )
			{
				var axis = data.LocalSpace ? go.WorldRotation.Forward : p.Velocity.Normal;
				var up = Vector3.Cross( Scene.Camera.View.Position - p.Position, axis );
				up = up.LengthSquared < 0.000001f ? go.WorldRotation.Up : up.Normal;
				p.Angles = Rotation.LookAt( Vector3.Cross( axis, up ), up ).Angles();
			}
			else p.Angles.roll = p.StartAngles.roll + data.Spin.Evaluate( p.Rand( 2, 0 ), 0 ) * p.Age;
		}
		effect.OnParticleCreated += p =>
		{
			p.StartAngles.roll = p.Rand( 3, 0 ) * 360;
			if ( data.Casing is not null )
			{
				float azimuth = p.Rand( 6, 0 ) * System.MathF.Tau;
				float angle = p.Rand( 7, 0 ) * data.ConeDegrees.DegreeToRadian();
				var radial = new Vector3( 0, System.MathF.Sin( azimuth ), System.MathF.Cos( azimuth ) );
				var direction = Vector3.Forward * System.MathF.Cos( angle ) + radial * System.MathF.Sin( angle );
				p.Velocity = go.WorldRotation * direction * p.LifeTimeRemaining.Remap( data.Lifetime.x, data.Lifetime.y, data.Speed.x, data.Speed.y );
			}
			else p.Velocity = go.WorldRotation.Forward * Sample( data.Speed );
			if ( data.SpawnRadius.y > 0 ) p.Position += go.WorldRotation * Spread.RandomDirection( ref seed ) * Sample( data.SpawnRadius );
			Appearance( p );
		};
		effect.OnPostStep += dt =>
		{
			foreach ( var p in effect.Particles ) Appearance( p );
			if ( data.Casing is not null ) CasingParticles.Advance( effect, holder, data.Casing, dt );
		};
		go.Enabled = true;
		return effect;
	}
}
