using System;

namespace Source1;

/// <summary>
/// Counter-Strike: Source's thrown grenades (CBaseCSGrenadeProjectile): a small box that bounces under
/// reduced gravity and goes off on a timer. Throw, fuse and blast values are the game's code constants.
/// </summary>
public abstract class CssGrenade : SourceProjectile, Component.ICollisionListener
{
	// GRENADE_TIMER for the HE grenade and the flashbang; the smoke grenade waits as long before it can pop.
	const float Fuse = 1.5f;
	const float HalfSize = 2;
	// CBaseCSGrenadeProjectile::GetGrenadeGravity and its bounce elasticity.
	const float GravityScale = 0.4f;
	const float Elasticity = 0.45f;
	const float MaxThrowSpeed = 750;

	/// <summary>
	/// CBaseCSGrenade::ThrowGrenade: the aim pitched 10 degrees up (scaled so straight up and down still
	/// reach), thrown at six units/s per degree below vertical up to 750, plus the thrower's velocity.
	/// </summary>
	public static Vector3 ThrowVelocity( Vector3 forward, Vector3 inherited )
	{
		var angles = Rotation.LookAt( forward ).Angles();
		var pitch = angles.pitch >= 0 ? -10 + angles.pitch * (100f / 90) : -10 + angles.pitch * (80f / 90);
		var speed = MathF.Min( (90 - pitch) * 6, MaxThrowSpeed );
		return Rotation.From( pitch, angles.yaw, 0 ).Forward * speed + inherited;
	}

	protected override float Lifetime => Fuse;
	protected override float? Physical => HalfSize;
	protected override Vector3 Spin => new Vector3( 600, Game.Random.Float( -1200, 1200 ), 0 ) * (MathF.PI / 180);
	/// <summary>The sound script played when the grenade bounces, such as HEGrenade.Bounce.</summary>
	protected abstract string BounceSound { get; }

	protected override void OnStart()
	{
		base.OnStart();
		if ( GetComponent<Rigidbody>() is { } body ) body.GravityScale = GravityScale;
		if ( GetComponent<BoxCollider>() is { } box ) box.Elasticity = Elasticity;
	}

	public void OnCollisionStart( Collision collision )
	{
		if ( Application.IsDedicatedServer || collision.Contact.Speed.Length < 50 ) return;
		if ( SourceSounds.Sound( BounceSound, MountName ) is { } sound ) Sound.Play( sound, WorldPosition );
	}
}

/// <summary>CHEGrenadeProjectile: the ordinary frag explosion, 100 damage over 3.5 times that in radius.</summary>
[Title( "CS:S HE Grenade" )]
[Category( "Counter-Strike: Source" )]
public sealed class CssHeGrenade : CssGrenade
{
	const float BlastDamage = 100;
	const float BlastRadius = BlastDamage * 3.5f;

	protected override string BounceSound => "HEGrenade.Bounce";

	protected override void Expire()
	{
		// The weapon script's Damage goes unused; the projectile's damage is code.
		Damage = BlastDamage;
		Radius = BlastRadius;
		Explode();
	}
}

/// <summary>
/// CFlashbangProjectile: RadiusFlash's 4 points of flash fall off to nothing at 1500 units and blind for that times
/// 2.5 seconds when facing the burst, 1.75 side on and 1 facing away (at 200/255), divided by 1.4 in CCSPlayer::Blind.
/// PercentageOfFlashForPlayer: none behind cover, 0.8 for each smoke cloud the view ray crosses. A 400 unit white
/// light flashes for a tenth of a second (te->DynamicLight), and Deafen rings in the ears within 1000 units.
/// CCSViewRender::PerformFlashbangEffect draws it over the HUD: white climbing 45/255 a frame to the flash's maximum, held
/// until three seconds are left and then fading with the square of the time left; under it the frame seen at the moment
/// of the flash, burnt in and fading linearly over the whole duration.
/// Approximation: the bang's glow sprite and the ring tone (a 3 kHz sine standing in for DSP presets 134-136).
/// </summary>
[Title( "CS:S Flashbang" )]
[Category( "Counter-Strike: Source" )]
public sealed class CssFlashbang : CssGrenade
{
	const float Range = 1500;
	const float FlashDamage = 4;
	const float LightRadius = 400;
	const float LightTime = 0.1f;
	const float FlashRamp = 45 / 255f;
	const float CertainBlindness = 3;

	protected override string BounceSound => "Flashbang.Bounce";

	protected override void Expire()
	{
		Burst( WorldPosition, MountName );
		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Burst( Vector3 position, string mount )
	{
		if ( Application.IsDedicatedServer ) return;
		// RadiusFlash lifts the source an inch in case the grenade lies on the ground.
		position += Vector3.Up;
		if ( SourceSounds.Sound( "Flashbang.Explode", mount ) is { } sound ) Sound.Play( sound, position );
		var go = new GameObject( true, "Flashbang burst" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;
		var light = go.AddComponent<PointLight>();
		light.Radius = LightRadius;
		light.LightColor = Color.White * 4;
		light.Shadows = false;
		var glow = SourceMountedEffects.AddSprite( go, "sprites/light_glow02", mount );
		SourceMountedEffects.SetSprite( glow, 2, 255 );
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = LightTime;
		var camera = Game.ActiveScene?.Camera;
		if ( !camera.IsValid() ) return;
		var eye = camera.WorldPosition;
		var distance = eye.Distance( position );
		if ( distance > Range ) return;
		var tr = Game.ActiveScene.Trace.Ray( position, eye ).WithoutTags( "player", "projectile", "trigger" ).Run();
		if ( tr.Hit && tr.EndPosition.Distance( eye ) > 32 ) return;
		var forward = camera.WorldRotation.Forward;
		var percentage = CssSmokeCloud.FlashScale( Game.ActiveScene, eye, forward );
		var facing = forward.Dot( (position - eye).Normal );
		var flash = FlashDamage * (1 - distance / Range);
		var seconds = flash * (facing >= 0.5f ? 2.5f : facing >= -0.5f ? 1.75f : 1) * percentage / 1.4f;
		var white = Color.White.WithAlpha( facing >= -0.5f ? 1 : 200 / 255f );
		SourceScreenFade.Show( camera, white, seconds - CertainBlindness, CertainBlindness, 2, FlashRamp, true );
		if ( distance < 1000 ) CssEarRing.Play( distance < 600 ? 1.6f : distance < 800 ? 0.2f : 0.1f );
	}
}

/// <summary>
/// CSmokeGrenadeProjectile: once the fuse has run and the canister has stopped rolling, pops a
/// <see cref="CssSmokeCloud"/> where it lies with BaseSmokeEffect.Sound. The canister stays until the smoke is gone.
/// </summary>
[Title( "CS:S Smoke Grenade" )]
[Category( "Counter-Strike: Source" )]
public sealed class CssSmokeGrenade : CssGrenade
{
	protected override string BounceSound => "SmokeGrenade.Bounce";
	bool _popped;
	TimeSince _sincePop;

	protected override void Expire()
	{
		if ( _popped )
		{
			if ( _sincePop > CssSmokeCloud.FadeEnd ) GameObject.Destroy();
			return;
		}
		if ( GetComponent<Rigidbody>() is { } body && body.Velocity.Length > 0.1f ) return;
		_popped = true;
		_sincePop = 0;
		Cloud( WorldPosition, MountName );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Cloud( Vector3 position, string mount )
	{
		if ( Application.IsDedicatedServer ) return;
		if ( SourceSounds.Sound( "BaseSmokeEffect.Sound", mount ) is { } sound ) Sound.Play( sound, position );
		var go = new GameObject( true, "Smoke grenade cloud" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;
		go.AddComponent<CssSmokeCloud>().MountName = mount;
	}
}

/// <summary>
/// C_ParticleSmokeGrenade: a 4x4x4 grid of puffs 80 units in radius, spaced over 240 units, in a sphere that grows
/// to 240 in a second (puffs past half of it wait at that edge, so they push out as it grows), solid in its core and
/// thinning at the edge. Each puff spins at up to 0.1 radians a second and keeps trading places with a free
/// neighbour over 10 to 20 seconds, eased, so the cloud churns. Faded out between 15 and 20 seconds
/// (SetFadeTime( 15, 20 )), and the view inside it is greyed by DrawSmokeFogOverlay.
/// Approximations: puffs are kept out of walls and floors by tracing from the centre (the SDK's own trace fade is
/// disabled), lit per pixel by the scene's light probes and lights instead of a sampled lightmap colour, and
/// softened where they meet geometry.
/// </summary>
[Title( "CS:S Smoke Cloud" )]
[Category( "Counter-Strike: Source" )]
public sealed class CssSmokeCloud : Component
{
	const int PerDimension = 4;
	const float ParticleRadius = 80;
	const float ParticleSize = 80;
	const float Overlap = 20;
	const float SpacingRadius = (ParticleRadius - Overlap) * PerDimension * 0.5f;
	const float ExpandTime = 1;
	const float RotationSpeed = 0.1f;
	const float TradeMin = 10;
	const float TradeMax = 20;
	public const float FadeStart = 15;
	public const float FadeEnd = 20;
	const float CoreCutoff = 0.3f;
	const float MinColor = 0.5f;
	const float MaxColor = 0.6f;
	const float WallMargin = ParticleSize * 0.5f;
	const float NearFade = 10;
	static readonly int[] Offsets = [-1, 0, 1];

	public string MountName { get; set; }

	sealed class Puff
	{
		public Particle Particle;
		public Vector3 Local;
		public float Roll;
		public float RollSpeed;
	}

	sealed class Slot
	{
		public Vector3 Home;
		public Puff Puff;
		public int Trade = -1;
		public float Clock;
		public float Duration;
	}

	readonly Slot[] _slots = new Slot[PerDimension * PerDimension * PerDimension];
	ParticleEffect _effect;
	TimeSince _age;
	float _expand;
	float _fade;

	protected override void OnStart()
	{
		_age = 0;
		// particle_smokegrenade1, the material CS:S draws the cloud with.
		_effect = CssParticles.Emitter( GameObject, "particle/particle_smokegrenade_thick", MountName, false, true, _slots.Length )
			?? CssParticles.Emitter( GameObject, "particle/particle_smokegrenade", MountName, false, true, _slots.Length );
		if ( _effect is null ) return;
		var renderer = _effect.GetComponent<ParticleSpriteRenderer>();
		renderer.DepthFeather = 24;
		var center = WorldPosition;
		var trace = Scene.Trace.IgnoreDynamic().WithoutTags( "player", "projectile", "trigger", "debris" );
		for ( var i = 0; i < _slots.Length; i++ )
		{
			var (x, y, z) = Coordinates( i );
			var home = new Vector3( x, y, z ) / (PerDimension - 1) * SpacingRadius * 2 - SpacingRadius;
			var p = _effect.Emit( center + home, 0 );
			p.Velocity = Vector3.Zero;
			p.Size = ParticleSize * 2;
			p.Alpha = 0;
			var grey = MinColor + (MaxColor - MinColor) * Game.Random.Float();
			// Desaturated halfway to 0.5 as RenderParticles does, as the albedo the scene's light falls on; halved, as a
			// sprite takes all of a light that only the lit side of a puff would get.
			p.Color = Color.White * ((grey + 0.5f) / 4);
			_slots[i] = new Slot { Home = Conform( trace, center, home ), Puff = new Puff { Particle = p, Roll = Game.Random.Float( -6, 6 ), RollSpeed = Game.Random.Float( -RotationSpeed, RotationSpeed ) } };
			_slots[i].Puff.Local = _slots[i].Home;
		}
	}

	/// <summary>Slides a puff's home out of the surface between it and the centre, then clamps it to the open space.</summary>
	static Vector3 Conform( SceneTrace trace, Vector3 center, Vector3 home )
	{
		var tr = trace.Ray( center, center + home ).Run();
		if ( !tr.Hit ) return home;
		var depth = (tr.HitPosition - (center + home)).Dot( tr.Normal );
		var slid = home + tr.Normal * (depth + WallMargin);
		var again = trace.Ray( center, center + slid ).Run();
		if ( !again.Hit ) return slid;
		var length = MathF.Max( again.Distance - WallMargin, 0 );
		return slid.Normal * length;
	}

	static (int X, int Y, int Z) Coordinates( int index ) => (index % PerDimension, index / PerDimension % PerDimension, index / (PerDimension * PerDimension));
	static int Index( int x, int y, int z ) => (z * PerDimension + y) * PerDimension + x;

	void FindTrade( int index )
	{
		var (x, y, z) = Coordinates( index );
		var ox = Game.Random.Int( 0, 2 );
		var oy = Game.Random.Int( 0, 2 );
		var oz = Game.Random.Int( 0, 2 );
		for ( var i = 0; i < 3; i++ )
			for ( var j = 0; j < 3; j++ )
				for ( var k = 0; k < 3; k++ )
				{
					var tx = x + Offsets[(i + ox) % 3];
					var ty = y + Offsets[(j + oy) % 3];
					var tz = z + Offsets[(k + oz) % 3];
					if ( (tx == x && ty == y && tz == z) || tx < 0 || ty < 0 || tz < 0 || tx >= PerDimension || ty >= PerDimension || tz >= PerDimension ) continue;
					var other = Index( tx, ty, tz );
					if ( _slots[other].Trade != -1 ) continue;
					_slots[index].Trade = other;
					_slots[other].Trade = index;
					_slots[index].Clock = 0;
					_slots[other].Clock = 0;
					_slots[index].Duration = Game.Random.Float( TradeMin, TradeMax );
					_slots[other].Duration = _slots[index].Duration;
					return;
				}
	}

	void UpdateTrade( int index, float dt )
	{
		var slot = _slots[index];
		var other = _slots[slot.Trade];
		// Each pair moves once a frame, from its lower index.
		if ( slot.Trade < index ) return;
		slot.Clock += dt;
		other.Clock = slot.Clock;
		if ( slot.Clock >= slot.Duration )
		{
			slot.Puff.Local = other.Home;
			other.Puff.Local = slot.Home;
			(slot.Puff, other.Puff) = (other.Puff, slot.Puff);
			other.Trade = -1;
			slot.Trade = -1;
			return;
		}
		var percent = MathF.Cos( slot.Clock * MathF.PI / slot.Duration ) * 0.5f + 0.5f;
		slot.Puff.Local = slot.Home + (other.Home - slot.Home) * (1 - percent);
		other.Puff.Local = slot.Home + (other.Home - slot.Home) * percent;
	}

	/// <summary>ClientThink's share of the fog overlay: all of it in the core, nothing past the sphere's edge.</summary>
	public float FogAlpha( Vector3 eye )
	{
		var distance = eye.Distance( WorldPosition );
		if ( distance >= _expand ) return 0;
		var core = _expand * CoreCutoff;
		return distance < core ? _fade : (1 - (distance - core) / (_expand - core)) * _fade;
	}

	/// <summary>PercentageOfFlashForPlayer: each cloud not yet fading that the view ray crosses lets 80% of a flash through.</summary>
	public static float FlashScale( Scene scene, Vector3 eye, Vector3 forward )
	{
		var scale = 1f;
		foreach ( var cloud in scene.GetAllComponents<CssSmokeCloud>() )
		{
			if ( cloud._age > FadeStart ) continue;
			var toCenter = cloud.WorldPosition - eye;
			var closest = toCenter - forward * toCenter.Dot( forward );
			if ( closest.Length < ParticleRadius ) scale *= 0.8f;
		}
		return scale;
	}

	protected override void OnUpdate()
	{
		if ( _effect is null ) return;
		var age = (float)_age;
		var dt = Time.Delta;
		_expand = SpacingRadius * 2 * MathF.Sin( MathF.Min( age, ExpandTime ) * MathF.PI * 0.5f / ExpandTime );
		_fade = age < FadeStart ? 1 : age < FadeEnd ? MathF.Cos( (age - FadeStart) / (FadeEnd - FadeStart) * MathF.PI ) * 0.5f + 0.5f : 0;
		_fade *= _expand / (SpacingRadius * 2);
		for ( var i = 0; i < _slots.Length; i++ )
		{
			if ( _slots[i].Trade == -1 ) FindTrade( i );
			else UpdateTrade( i, dt );
		}
		var camera = Scene.Camera;
		foreach ( var slot in _slots )
		{
			var puff = slot.Puff;
			var p = puff.Particle;
			puff.Roll += puff.RollSpeed * dt;
			p.Angles = new Angles( 0, 0, puff.Roll.RadianToDegree() );
			var length = puff.Local.Length;
			if ( length > _expand || _expand <= 0 )
			{
				p.Alpha = 0;
				continue;
			}
			var local = length > _expand * 0.5f ? puff.Local * (_expand * 0.5f / length) : puff.Local;
			p.Position = WorldPosition + local;
			var edge = 1 - length / _expand;
			var alpha = (edge > CoreCutoff ? 1 : edge / CoreCutoff) * _fade;
			// GetAlphaDistanceFade( 0, 10 ): puffs right in front of the eye vanish.
			if ( camera.IsValid() ) alpha *= Math.Clamp( (p.Position - camera.WorldPosition).Dot( camera.WorldRotation.Forward ) / NearFade, 0, 1 );
			p.Alpha = alpha;
		}
		CssSmokeFog.Ensure( Scene );
		if ( age > FadeEnd ) GameObject.Destroy();
	}
}

/// <summary>DrawSmokeFogOverlay: the screen greyed to 0.3 by the summed fog of every cloud the eye is inside, under the HUD.</summary>
[Title( "CS:S Smoke Fog" )]
[Category( "Counter-Strike: Source" )]
public sealed class CssSmokeFog : PanelComponent
{
	const float Grey = 0.3f;

	public static void Ensure( Scene scene )
	{
		if ( scene.GetAllComponents<CssSmokeFog>().Any() ) return;
		var go = new GameObject( true, "Smoke fog overlay" );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.AddComponent<ScreenPanel>().ZIndex = 0;
		go.AddComponent<CssSmokeFog>();
	}

	protected override void OnUpdate()
	{
		var camera = Scene.Camera;
		var visible = Panel is not null && camera.IsValid();
		var anyClouds = false;
		double alpha = 0;
		foreach ( var cloud in Scene.GetAllComponents<CssSmokeCloud>() )
		{
			anyClouds = true;
			if ( visible ) alpha += cloud.FogAlpha( camera.WorldPosition );
		}
		if ( !anyClouds )
		{
			GameObject.Destroy();
			return;
		}
		if ( Panel is null ) return;
		Panel.Style.Position = Sandbox.UI.PositionMode.Absolute;
		Panel.Style.Width = Sandbox.UI.Length.Percent( 100 );
		Panel.Style.Height = Sandbox.UI.Length.Percent( 100 );
		Panel.Style.BackgroundColor = new Color( Grey, Grey, Grey );
		Panel.Style.Opacity = Math.Clamp( (float)alpha, 0, 1 );
	}
}

/// <summary>
/// CCSPlayer::Deafen: DSP presets 134 to 136 (FLASHBANG MUFFLE LONG, MEDIUM, SHORT) mix a 3 kHz sine into the game's
/// sound for 1.6, 0.2 or 0.1 seconds, then fade. Approximation: the tone alone, played as a stream.
/// </summary>
static class CssEarRing
{
	const int Rate = 44100;
	const float Frequency = 3000;
	const float Gain = 0.05f;
	const float Fade = 1.5f;

	public static void Play( float hold )
	{
		var count = (int)((hold + Fade) * Rate);
		var samples = new short[count];
		for ( var i = 0; i < count; i++ )
		{
			var t = i / (float)Rate;
			var envelope = t < hold ? 1 : MathF.Exp( -4 * (t - hold) / Fade );
			samples[i] = (short)(MathF.Sin( t * Frequency * MathF.PI * 2 ) * envelope * Gain * short.MaxValue);
		}
		var stream = new SoundStream( Rate, 1 );
		stream.WriteData( samples );
		var handle = stream.Play();
		if ( handle is not null ) handle.ListenLocal = true;
		_ = Release( stream, hold + Fade + 1 );
	}

	static async Task Release( SoundStream stream, float seconds )
	{
		await GameTask.DelaySeconds( seconds );
		stream.Dispose();
	}
}
