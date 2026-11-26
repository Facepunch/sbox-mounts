using System;

namespace Halo2;

public sealed record VolumeSprite( float Offset, float Radius, Vector2 Aspect, float Alpha, Color Color );
public sealed record VolumeLayer( string Texture, VolumeSprite[] Sprites, float ParallelScale, float Threshold, float Exponent );

[Hide]
public sealed class ProjectileLightVolume : Component
{
	[Property] public VolumeLayer[] Layers { get; set; }
	[Property] public float Falloff { get; set; }
	[Property] public MuzzleFlashDefinition[] Glow { get; set; }
	[Property] public WeaponSoundTrack Sound { get; set; }
	[Property] public float Cutoff { get; set; }
	readonly TrackVoice voice = new();

	protected override void OnStart()
	{
		if ( Application.IsDedicatedServer || Layers is null ) return;
		if ( Glow is { Length: > 0 } )
		{
			var glow = GameObject.AddComponent<MuzzleFlash>();
			glow.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
			uint seed = (uint)System.Random.Shared.Next();
			foreach ( var effect in Glow )
				glow.Emit( effect, false, GameObject, seed, BlamRandom.Next( ref seed ) / 65535f, false );
		}
		foreach ( var layer in Layers )
		{
			var texture = Texture.Load( layer.Texture );
			if ( !texture.IsValid() || texture.IsError ) continue;
			var go = new GameObject( GameObject, false, "Bolt sprites" );
			go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			var effect = go.AddComponent<ParticleEffect>();
			effect.MaxParticles = layer.Sprites.Length;
			effect.Lifetime = float.MaxValue;
			var renderer = go.AddComponent<ParticleSpriteRenderer>();
			renderer.Sprite = Sprite.FromTexture( texture );
			renderer.Additive = true;
			renderer.Lighting = false;
			renderer.Scale = 2;
			effect.OnPostStep += _ => PlaceSprites( effect, layer );
			go.Enabled = true;
			for ( int i = 0; i < layer.Sprites.Length; i++ ) effect.Emit( WorldPosition, 0 );
		}
	}

	protected override void OnUpdate()
	{
		if ( Sound is not null && !Application.IsDedicatedServer ) voice.Update( Sound, true, GameObject );
	}

	protected override void OnDisabled() => voice.Stop( Sound );

	void PlaceSprites( ParticleEffect effect, VolumeLayer layer )
	{
		var camera = Scene.Camera;
		if ( !camera.IsValid() ) return;
		var forward = WorldRotation.Forward;
		var toEye = camera.WorldPosition - WorldPosition;
		float opacity = Cutoff > Falloff ? ((Cutoff - toEye.Length) / (Cutoff - Falloff)).Clamp( 0, 1 ) : 1;
		float angle = MathF.Acos( MathF.Abs( toEye.Normal.Dot( forward ) ).Clamp( 0, 1 ) );
		float strength = MathF.Pow( ((angle - layer.Threshold) / (MathF.PI / 2 - layer.Threshold)).Clamp( 0, 1 ), layer.Exponent );
		var axis = camera.PointToScreenPixels( WorldPosition + forward, out _ ) - camera.PointToScreenPixels( WorldPosition, out _ );
		float roll = MathF.Atan2( -axis.y, axis.x ).RadianToDegree();
		for ( int i = 0; i < effect.Particles.Count; i++ )
		{
			var p = effect.Particles[i];
			var sprite = layer.Sprites[i];
			p.Position = WorldPosition + forward * sprite.Offset;
			var parallel = Vector2.One * layer.ParallelScale;
			p.Size = (parallel + (sprite.Aspect - parallel) * strength) * sprite.Radius;
			p.Color = sprite.Color;
			p.Alpha = (float)Math.Round( (double)(opacity * sprite.Alpha).Clamp( 0, 1 ) * 255 ) / 255;
			p.Angles.roll = roll;
		}
	}
}
