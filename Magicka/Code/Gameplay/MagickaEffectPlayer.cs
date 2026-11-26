using System;

namespace Magicka;

/// <summary>
/// Plays a native Magicka <c>Content/Effects/**/*.xml</c> particle effect 1:1 using s&amp;box's
/// <see cref="ParticleEffect"/> and <see cref="ParticleSpriteRenderer"/> with exact sliced
/// tiles from <c>effecttextures/particlesa..d.vtex</c>.
/// </summary>
[Hide]
public sealed class MagickaEffectPlayer : Component
{
	private sealed class EmitterRuntime
	{
		public MagickaEmitterDef Def;
		public ParticleEffect Effect;
		public bool PulseFired;
		public float Accumulator;
		public Dictionary<Particle, (float StartSize, float EndSize, float BaseAlpha, float RotSpeed)> Tracked = new();
	}

	private MagickaEffectDef _def;
	private readonly List<EmitterRuntime> _runtimes = new();
	private float _scale = 1f;
	private float _elapsed;
	private bool _autoDestroy;

	internal static MagickaEffectPlayer PlayOnce( string effectPath, Vector3 position, Rotation? rotation = null, float scale = 1f )
	{
		if ( Application.IsDedicatedServer )
			return null;

		var def = MagickaVisualEffects.Load( effectPath );
		if ( def is null || def.Emitters.Length == 0 )
			return null;

		var go = new GameObject( false, "magicka_fx" );
		go.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		go.WorldPosition = position;
		go.WorldRotation = rotation ?? Rotation.Identity;

		var player = go.AddComponent<MagickaEffectPlayer>();
		player.Initialize( def, scale, autoDestroy: true );
		go.Enabled = true;
		return player;
	}

	internal static MagickaEffectPlayer Attach( GameObject parent, string effectPath, Vector3 localOffset = default, float scale = 1f )
	{
		if ( Application.IsDedicatedServer || !parent.IsValid() )
			return null;

		var def = MagickaVisualEffects.Load( effectPath );
		if ( def is null || def.Emitters.Length == 0 )
			return null;

		var go = new GameObject( parent, false, "magicka_fx_attached" );
		go.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		go.LocalPosition = localOffset;
		go.LocalRotation = Rotation.Identity;

		var player = go.AddComponent<MagickaEffectPlayer>();
		player.Initialize( def, scale, autoDestroy: !def.Looping );
		go.Enabled = true;
		return player;
	}

	private void Initialize( MagickaEffectDef def, float scale, bool autoDestroy )
	{
		_def = def;
		_scale = Math.Clamp( scale, 0.1f, 5f );
		_autoDestroy = autoDestroy;

		foreach ( var emitter in def.Emitters )
		{
			var tile = MagickaVisualEffects.GetParticleTile( emitter.ParticleIndex );
			if ( !tile.IsValid() )
				continue;

			var child = new GameObject( GameObject, true, $"emitter_{emitter.ParticleIndex}" );
			child.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;

			var pe = child.AddComponent<ParticleEffect>();
			pe.MaxParticles = Math.Clamp( emitter.IsPulse ? emitter.NrOfParticles * 2 + 8 : (int)(emitter.ParticlesPerSecond * MathF.Max( emitter.LifeTimeMax, 1f ) * 1.5f) + 16, 16, 256 );
			pe.LocalSpace = 0f;
			pe.Lifetime = (emitter.LifeTimeMin + emitter.LifeTimeMax) * 0.5f;
			pe.Damping = emitter.Drag;
			pe.ApplyRotation = true;
			pe.ApplyColor = true;
			pe.ApplyAlpha = true;
			pe.ApplyShape = false;

			if ( MathF.Abs( emitter.Gravity ) > 0.001f )
			{
				pe.Force = true;
				pe.ForceDirection = Vector3.Up * (emitter.Gravity * MagickaSpace.UnitScale * _scale);
				pe.ForceScale = 1f;
			}

			var sr = child.AddComponent<ParticleSpriteRenderer>();
#pragma warning disable CS0612
			sr.Texture = tile;
#pragma warning restore CS0612
			sr.Additive = emitter.BlendMode == MagickaBlendMode.Additive;
			sr.Lighting = false;
			sr.Shadows = false;
			sr.DepthFeather = 6f;
			sr.FaceVelocity = emitter.RotationAligned;

			var rt = new EmitterRuntime { Def = emitter, Effect = pe };
			pe.OnStep += ( p, dt ) => StepParticle( rt, p, dt );
			pe.OnParticleDestroyed += p => rt.Tracked.Remove( p );
			_runtimes.Add( rt );
		}
	}

	protected override void OnUpdate()
	{
		if ( _def is null || _runtimes.Count == 0 )
		{
			if ( _autoDestroy )
				GameObject.Destroy();
			return;
		}

		var dt = Time.Delta;
		_elapsed += dt;
		var canEmit = _def.Looping || _elapsed <= _def.Duration;
		var anyPending = false;

		foreach ( var rt in _runtimes )
		{
			var d = rt.Def;
			if ( d.IsPulse )
			{
				if ( !rt.PulseFired )
				{
					if ( _elapsed >= d.TriggerTime )
					{
						rt.PulseFired = true;
						for ( var i = 0; i < d.NrOfParticles; i++ )
							EmitOne( rt );
					}
					else
					{
						anyPending = true;
					}
				}
			}
			else if ( canEmit && d.ParticlesPerSecond > 0.01f )
			{
				anyPending = true;
				rt.Accumulator += d.ParticlesPerSecond * dt;
				var count = (int)rt.Accumulator;
				if ( count > 0 )
				{
					rt.Accumulator -= count;
					count = Math.Min( count, 12 );
					for ( var i = 0; i < count; i++ )
						EmitOne( rt );
				}
			}
		}

		if ( _autoDestroy && !anyPending )
		{
			var anyAlive = false;
			foreach ( var rt in _runtimes )
			{
				if ( rt.Tracked.Count > 0 )
				{
					anyAlive = true;
					break;
				}
			}

			if ( !anyAlive && _elapsed > 0.1f )
				GameObject.Destroy();
		}
	}

	private void EmitOne( EmitterRuntime rt )
	{
		if ( !rt.Effect.IsValid() )
			return;

		var d = rt.Def;
		var offset = new Vector3(
			Game.Random.Float( -d.PositionOffset.x, d.PositionOffset.x ),
			Game.Random.Float( -d.PositionOffset.y, d.PositionOffset.y ),
			Game.Random.Float( -d.PositionOffset.z, d.PositionOffset.z ) );

		var worldPos = WorldTransform.PointToWorld( (d.Position + offset) * _scale );
		var p = rt.Effect.Emit( worldPos, 0f );
		if ( p is null )
			return;

		p.DeathTime = p.BornTime + RandomRange( d.LifeTimeMin, d.LifeTimeMax );
		var dir = SampleDirection( d );
		var speed = RandomRange( d.VelocityMin, d.VelocityMax ) * MagickaSpace.UnitScale * _scale;
		p.Velocity = WorldRotation * dir * speed;

		var startSize = RandomRange( d.SizeStartMin, d.SizeStartMax ) * MagickaSpace.UnitScale * _scale;
		var endSize = RandomRange( d.SizeEndMin, d.SizeEndMax ) * MagickaSpace.UnitScale * _scale;
		p.Size = Vector3.One * startSize;

		var rot = RandomRange( d.RotationMin, d.RotationMax );
		var rotSpeed = RandomRange( d.RotationSpeedMin, d.RotationSpeedMax );
		if ( Game.Random.Float( 0f, 100f ) < d.RotationPCCW )
			rotSpeed = -rotSpeed;
		p.Angles = new Angles( 0f, 0f, rot );

		var alpha = RandomRange( d.AlphaMin, d.AlphaMax );
		p.Color = SampleColor( d );
		p.Alpha = alpha;

		rt.Tracked[p] = (startSize, endSize, alpha, rotSpeed);
	}

	private static void StepParticle( EmitterRuntime rt, Particle p, float dt )
	{
		if ( !rt.Tracked.TryGetValue( p, out var data ) )
			return;

		var t = Math.Clamp( p.LifeDelta, 0f, 1f );
		var size = data.StartSize + (data.EndSize - data.StartSize) * t;
		p.Size = Vector3.One * size;

		// Smooth Magicka fade-in/fade-out alpha curve.
		var fade = t < 0.12f ? t / 0.12f : 1f - ((t - 0.12f) / 0.88f);
		p.Alpha = data.BaseAlpha * Math.Clamp( fade, 0f, 1f );

		if ( MathF.Abs( data.RotSpeed ) > 0.01f )
			p.Angles = new Angles( p.Angles.pitch, p.Angles.yaw, p.Angles.roll + data.RotSpeed * dt );
	}

	private static Vector3 SampleDirection( MagickaEmitterDef d )
	{
		if ( d.SpreadType == MagickaSpreadType.Cone )
		{
			var maxRad = d.SpreadConeAngle.DegreeToRadian();
			var theta = Game.Random.Float( 0f, MathF.PI * 2f );
			var phi = Game.Random.Float( 0f, maxRad );
			var sinPhi = MathF.Sin( phi );
			return new Vector3( MathF.Cos( phi ), MathF.Cos( theta ) * sinPhi, MathF.Sin( theta ) * sinPhi ).Normal;
		}

		// Arc spread: horizontal yaw in [-H, +H], vertical elevation in [Vmin, Vmax] (90° = straight up).
		var yaw = Game.Random.Float( -d.SpreadArcHorizontalAngle, d.SpreadArcHorizontalAngle ).DegreeToRadian();
		var vMin = MathF.Min( d.SpreadArcVerticalMin, d.SpreadArcVerticalMax );
		var vMax = MathF.Max( d.SpreadArcVerticalMin, d.SpreadArcVerticalMax );
		var elev = Game.Random.Float( vMin, vMax ).DegreeToRadian();
		var cosElev = MathF.Cos( elev );
		return new Vector3( MathF.Cos( yaw ) * cosElev, MathF.Sin( yaw ) * cosElev, MathF.Sin( elev ) ).Normal;
	}

	private static Color SampleColor( MagickaEmitterDef d )
	{
		var hue = RandomRange( d.HueMin, d.HueMax );
		var sat = Math.Clamp( RandomRange( d.SatMin, d.SatMax ), 0f, 1f );

		// When Colorize is false and Hue is ~0 with full saturation, keep the sprite's authored colors intact.
		if ( !d.Colorize && MathF.Abs( hue ) < 1f && sat > 0.95f )
			return Color.White;

		var h = ((hue % 360f) + 360f) % 360f;
		return new ColorHsv( h, sat, 1f ).ToColor();
	}

	private static float RandomRange( float a, float b )
	{
		var min = MathF.Min( a, b );
		var max = MathF.Max( a, b );
		return max - min > 0.0001f ? Game.Random.Float( min, max ) : min;
	}
}
