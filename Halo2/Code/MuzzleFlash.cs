namespace Halo2;

public sealed record MuzzleFlashDefinition( string Marker, string Texture, int Sequence, Vector2 Pivot, Vector2 Aspect,
	float Lifetime, CurveRange Radius, CurveRange Spin, CurveRange Frame, Color StartColor, Color EndColor );

public sealed class MuzzleFlash : Component
{
	ParticleEffect effect;

	internal void Emit( MuzzleFlashDefinition data, bool firstPerson )
	{
		if ( !effect.IsValid() )
		{
			var texture = Texture.Load( data.Texture );
			if ( !texture.IsValid() || texture.IsError ) return;
			var sprite = Sprite.FromTexture( texture );
			sprite.Animations[0].Origin = data.Pivot;
			var go = new GameObject( GameObject, false, "Muzzle flash" );
			go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			effect = go.AddComponent<ParticleEffect>();
			effect.Lifetime = data.Lifetime;
			effect.LocalSpace = 1;
			var renderer = go.AddComponent<ParticleSpriteRenderer>();
			renderer.Sprite = sprite;
			renderer.Scale = 2;
			renderer.Additive = true;
			renderer.FogStrength = 0;
			renderer.RenderOptions.Game = !firstPerson;
			renderer.RenderOptions.Overlay = firstPerson;
			void Appearance( Particle p )
			{
				float t = p.LifeDelta;
				p.Size = data.Aspect * data.Radius.Evaluate( t, p.Rand( 1, 0 ) );
				p.Color = Color.Lerp( data.StartColor, data.EndColor, t );
				p.Sequence = data.Sequence;
				p.SequenceTime = new Vector3( data.Frame.Evaluate( t, 0 ), 0, 0 );
				p.Angles.roll = p.StartAngles.roll + data.Spin.Evaluate( p.Rand( 2, 0 ), 0 ) * t * data.Lifetime;
			}
			effect.OnParticleCreated += p =>
			{
				p.StartAngles.roll = p.Rand( 3, 0 ) * 360;
				Appearance( p );
			};
			effect.OnPostStep += _ =>
			{
				foreach ( var p in effect.Particles ) Appearance( p );
			};
			go.Enabled = true;
		}
		effect.Emit( WorldPosition, 0 );
	}
}
