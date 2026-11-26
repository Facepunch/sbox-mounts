using System;

namespace Halo2;

public readonly record struct VolumeSprite( float Offset, float Radius, Vector2 Aspect, float Alpha, Color Color );
public sealed record VolumeLayer( string Texture, VolumeSprite[] Sprites, float ParallelScale, float Threshold, float Exponent )
{
	public Vector2[] Phases { get; init; }
	public VolumeCurves Curves { get; init; }
	public int Count => Phases?.Length ?? Sprites.Length;
}

public sealed record VolumeCurves( CurveRange Offset, CurveRange Radius, CurveRange Width, CurveRange Height,
	CurveRange Alpha, CurveRange Tint, Color32 StartColor, Color32 EndColor )
{
	public VolumeSprite Sample( float phase, float range ) => new( Offset.Evaluate( phase, range ), Radius.Evaluate( phase, range ) * 120,
		new( Width.Evaluate( phase, 0 ), Height.Evaluate( phase, 0 ) ), Alpha.Evaluate( phase, range ),
		MuzzleFlash.InterpolateTint( StartColor, EndColor, Tint.Evaluate( phase, range ) ) );
}

[Hide]
public sealed class ProjectileLightVolume : Component
{
	[Property] public VolumeLayer[] Layers { get; set; }
	[Property] public float Falloff { get; set; }
	[Property] public MuzzleFlashDefinition[] Glow { get; set; }
	[Property] public float Cutoff { get; set; }

	[Property] public int StartTick { get; set; }
	[Property] public int EndTick { get; set; }
	[Property] public double FiredAt { get; set; }

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
		if ( EndTick > 0 )
		{
			_ = PlayStage();
			return;
		}
		CreateSprites( GameObject );
	}

	async System.Threading.Tasks.Task PlayStage()
	{
		double clock = FiredAt > 0 ? FiredAt : Time.NowDouble;
		double start = clock + StartTick / 30.0, end = clock + EndTick / 30.0;
		while ( Time.NowDouble < start ) await Task.Frame();
		if ( Time.NowDouble >= end ) return;
		var stage = new GameObject( GameObject, true, "Projectile light volume" );
		stage.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		CreateSprites( stage );
		while ( Time.NowDouble < end ) await Task.Frame();
		stage.Enabled = false;
		stage.Destroy();
	}

	void CreateSprites( GameObject parent )
	{
		foreach ( var layer in Layers )
		{
			var texture = Texture.Load( layer.Texture );
			if ( !texture.IsValid() || texture.IsError ) continue;
			var go = new GameObject( parent, false, "Bolt sprites" );
			go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			var effect = go.AddComponent<ParticleEffect>();
			effect.MaxParticles = layer.Count;
			effect.Lifetime = float.MaxValue;
			var renderer = go.AddComponent<ParticleSpriteRenderer>();
			renderer.Sprite = Sprite.FromTexture( texture );
			renderer.Additive = true;
			renderer.Lighting = false;
			renderer.Scale = 2;
			effect.OnPostStep += _ => PlaceSprites( effect, layer );
			go.Enabled = true;
			for ( int i = 0; i < layer.Count; i++ ) effect.Emit( WorldPosition, 0 );
		}
	}

	void PlaceSprites( ParticleEffect effect, VolumeLayer layer )
	{
		var camera = Scene.Camera;
		if ( !camera.IsValid() ) return;
		var forward = WorldRotation.Forward;
		var toEye = camera.WorldPosition - WorldPosition;
		float opacity = Cutoff > Falloff ? ((Cutoff - toEye.Length) / (Cutoff - Falloff)).Clamp( 0, 1 ) : 1;
		float facing = MathF.Abs( toEye.Normal.Dot( forward ) ).Clamp( 0, 1 );
		float angle = MathF.Acos( facing );
		float strength = MathF.Pow( ((angle - layer.Threshold) / (MathF.PI / 2 - layer.Threshold)).Clamp( 0, 1 ), layer.Exponent );
		var axis = camera.PointToScreenPixels( WorldPosition + forward, out _ ) - camera.PointToScreenPixels( WorldPosition, out _ );
		float roll = MathF.Atan2( -axis.y, axis.x ).RadianToDegree();
		for ( int i = 0; i < effect.Particles.Count; i++ )
		{
			var p = effect.Particles[i];
			var sprite = layer.Phases is {} phases ? layer.Curves.Sample( MathX.Lerp( phases[i].x, phases[i].y, facing ), facing ) : layer.Sprites[i];
			p.Position = WorldPosition + forward * sprite.Offset;
			var parallel = Vector2.One * layer.ParallelScale;
			p.Size = (parallel + (sprite.Aspect - parallel) * strength) * sprite.Radius;
			p.Color = sprite.Color;
			p.Alpha = (float)Math.Round( (double)(opacity * sprite.Alpha).Clamp( 0, 1 ) * 255 ) / 255;
			p.Angles.roll = roll;
		}
	}
}
