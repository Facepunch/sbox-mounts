using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>
/// npc_strider's warp cannon, SCHED_STRIDER_RANGE_ATTACK2: stop and face the target, wait a second, charge for 1.25 seconds
/// (TASK_STRIDER_AIM: NPC_Strider.Charge and the C_StriderFX beam from BigGun), fire (NPC_Strider.Shoot), and 0.2 seconds later
/// CreateConcussiveBlast where the shot landed: 200 blast damage within 256 units, and a target the shot reached dies.
/// Source fires only at a cannon target a map assigns; here the strider picks an enemy that stands still or is large.
/// </summary>
[Title( "HL2 Strider Cannon" )]
[Category( "Half-Life 2" )]
public sealed class Hl2StriderCannon : SourceCreatureBehaviour
{
	const string Cannon = "BigGun";
	const float FaceTime = 1;
	const float AimTime = 1.25f;
	const float HitDelay = 0.2f;
	const float Recovery = 1;
	const float BlastDamage = 200;
	const float BlastRadius = 256;

	/// <summary>Approximation: the concussive blast also shows the frag explosion at this scale and shakes cameras within this distance.</summary>
	const float ExplosionScale = 2;
	const float ShakeRadius = 1024;
	const float ShakeAmplitude = 8;

	/// <summary>Gameplay choice standing in for a map's cannon target: how long between shots and what is worth one.</summary>
	const float Cooldown = 15;
	const float StillTime = 2;
	const float StillDistance = 48;
	const float LargeSize = 128;
	const float MinDistance = 512;

	enum Phase { None, Face, Aim, Fired }
	Phase _phase;
	TimeSince _time;
	TimeSince _lastShot = Cooldown;
	GameObject _target;
	GameObject _watched;
	Vector3 _watchedFrom;
	TimeSince _watchedStill;
	Vector3 _blastPoint;
	bool _reached;
	bool _blasted;

	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [];

	protected override void OnConfigure( Model model, SourceModelSequences sequences ) { }

	Vector3 Muzzle => Npc.Renderer?.GetAttachment( Cannon )?.Position ?? WorldPosition + Vector3.Up * 200;

	/// <summary>Host think; true from facing the target until a second after the blast.</summary>
	public override bool Think( GameObject target )
	{
		Watch( target );
		switch ( _phase )
		{
			case Phase.None:
				if ( !CanFire( target ) )
					return false;
				_target = target;
				Enter( Phase.Face );
				Npc.Movement?.Stop();
				return true;

			case Phase.Face:
				if ( !Face() )
					return Abort();
				if ( _time >= FaceTime )
				{
					Enter( Phase.Aim );
					PresentCharge( _target );
				}
				return true;

			case Phase.Aim:
				if ( !Face() )
					return Abort();
				if ( _time >= AimTime )
					Fire();
				return true;

			case Phase.Fired:
				if ( !_blasted && _time >= HitDelay )
					Blast();
				if ( _time < HitDelay + Recovery )
					return true;
				_phase = Phase.None;
				return false;
		}
		return false;
	}

	void Enter( Phase phase )
	{
		_phase = phase;
		_time = 0;
	}

	/// <summary>Tracks how long the current enemy has stayed within a few steps of one spot.</summary>
	void Watch( GameObject target )
	{
		if ( target == _watched && target.IsValid() && target.WorldPosition.Distance( _watchedFrom ) < StillDistance )
			return;

		_watched = target;
		_watchedFrom = target.IsValid() ? target.WorldPosition : default;
		_watchedStill = 0;
	}

	bool CanFire( GameObject target )
	{
		if ( !target.IsValid() || _lastShot < Cooldown )
			return false;

		var distance = WorldPosition.Distance( target.WorldPosition );
		if ( distance < MinDistance || distance > Npc.AttackRange )
			return false;

		var size = target.GetBounds().Size;
		if ( _watchedStill < StillTime && MathF.Max( size.z, MathF.Max( size.x, size.y ) ) < LargeSize )
			return false;

		var tr = Scene.Trace.Ray( Muzzle, SourceNpcs.CapturePoint( target ) ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
		return !tr.Hit || tr.GameObject?.Root == target.Root;
	}

	bool Face()
	{
		if ( !_target.IsValid() || _target.GetComponent<BaseNpc>() is { Health: < 1 } )
			return false;

		WorldRotation = Rotation.Slerp( WorldRotation, Rotation.LookAt( (_target.WorldPosition - WorldPosition).WithZ( 0 ) ), Time.Delta * 3 );
		return true;
	}

	/// <summary>FireCannon with no target turns the effect off (STRIDER_MSG_DEAD).</summary>
	bool Abort()
	{
		_phase = Phase.None;
		PresentStop();
		return false;
	}

	void Fire()
	{
		_lastShot = 0;
		var tr = Scene.Trace.Ray( Muzzle, SourceNpcs.CapturePoint( _target ) ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
		_reached = !tr.Hit || tr.GameObject?.Root == _target.Root;
		_blastPoint = tr.EndPosition + tr.Normal * 16;
		_blasted = false;
		Enter( Phase.Fired );
		PresentShot( tr.EndPosition );
	}

	/// <summary>CannonHitThink: the concussive blast (a DMG_BLAST RadiusDamage), then the target dies if the shot reached it and it is still alive.</summary>
	void Blast()
	{
		_blasted = true;
		var explosion = new SourceExplosion
		{
			Source = GameObject,
			Attacker = GameObject,
			Position = _blastPoint,
			Radius = BlastRadius,
			Damage = BlastDamage,
			ForceScale = 1,
			Prefab = SourceMount.Prefab( SourceMount.Of( GameObject ), "explosion" )
		}.Spawn();
		if ( explosion.IsValid() )
			explosion.WorldScale = ExplosionScale;
		PresentBlast( _blastPoint );
		if ( !_reached || !_target.IsValid() )
			return;

		var health = SourceNpcs.HealthOf( _target )?.Health ?? 0;
		if ( health > 0 )
			Npc.Damage( _target, Muzzle, _blastPoint, SourceDamageTags.Explosion, health );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PresentCharge( GameObject target )
	{
		if ( Application.IsDedicatedServer || !Npc.Renderer.IsValid() )
			return;

		Hl2StriderCannonEffect.Start( Npc.Renderer.GetAttachmentObject( Cannon ), target, SourceMount.Of( GameObject ) );
		PlaySound( "NPC_Strider.Charge", Npc.Renderer.GetAttachment( Cannon )?.Position ?? WorldPosition );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PresentShot( Vector3 end )
	{
		if ( Application.IsDedicatedServer )
			return;

		foreach ( var effect in Scene.GetAllComponents<Hl2StriderCannonEffect>().Where( x => x.Owner == GameObject ) )
			effect.Shoot( end );
		PlaySound( "NPC_Strider.Shoot", Muzzle );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PresentStop()
	{
		foreach ( var effect in Scene.GetAllComponents<Hl2StriderCannonEffect>().Where( x => x.Owner == GameObject ) )
			effect.GameObject.Destroy();
	}

	/// <summary>FX_ConcussiveExplosion's blue flares and CConcussiveBlast's lgtning shock ring (300 * 2.5 units, 0.3 seconds).</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PresentBlast( Vector3 position )
	{
		if ( Application.IsDedicatedServer )
			return;

		var mount = SourceMount.Of( GameObject );
		var flash = new GameObject( true, "Concussive blast" );
		flash.NetworkMode = NetworkMode.Never;
		flash.WorldPosition = position;
		var burst = flash.AddComponent<SourceSpriteBurst>();
		var core = burst.Add( "effects/blueflare1", mount, 0, 16, Color.White * 0.5f, 0.2f );
		burst.FadeTo( core, 64, Color.Black );
		var halo = burst.Add( "effects/blueflare1", mount, 0, 64, Color.White * 0.125f, 0.2f );
		burst.FadeTo( halo, 128, Color.Black );
		Hl2BeamRing.Create( position, 16, 750, 128, 0.3f, 24, mount );

		var camera = Scene.Camera;
		var shake = camera.IsValid() ? 1 - camera.WorldPosition.Distance( position ) / ShakeRadius : 0;
		if ( shake > 0 )
			camera.AddShake( ShakeAmplitude * shake, 40, 1 );
	}

	void PlaySound( string name, Vector3 position )
	{
		if ( SourceSounds.Sound( name, SourceMount.Of( GameObject ) ) is { } sound )
			Sound.Play( sound, position );
	}
}

/// <summary>
/// C_StriderFX: on the cannon a blue flare and afterglow, along the line of sight a narrow beam that thickens through
/// the charge and a wide beam that shrinks from the gun into the target after the shot, timed by g_StriderCannonEnvelope.
/// The refracting pinch on the gun is left out, and the refracting bubble that swells at the target is drawn as a faint blue flare.
/// </summary>
[Title( "HL2 Strider Cannon Effect" )]
[Category( "Half-Life 2" )]
public sealed class Hl2StriderCannonEffect : Component
{
	const float BigShotTime = 1.25f;
	const float EndTime = 4;

	enum Key { Linear, Spline, Accelerate, Decelerate }

	static readonly (float Time, Key Kind, float Value)[] NarrowColor = [(0, Key.Linear, 0), (1.25f, Key.Accelerate, 1), (1.5f, Key.Spline, 0)];
	static readonly (float Time, Key Kind, float Value)[] NarrowSize = [(0, Key.Linear, 0), (0.5f, Key.Accelerate, 1), (1.25f, Key.Linear, 1), (1.5f, Key.Decelerate, 2)];
	static readonly (float Time, Key Kind, float Value)[] WideColor = [(1.25f, Key.Linear, 0), (1.5f, Key.Spline, 1), (1.75f, Key.Linear, 1), (2.1f, Key.Spline, 0)];
	static readonly (float Time, Key Kind, float Value)[] WideLength = [(1.25f, Key.Linear, 1), (1.5f, Key.Accelerate, 0), (2.1f, Key.Linear, 0)];
	static readonly (float Time, Key Kind, float Value)[] FlareColor = [(0, Key.Linear, 0), (0.5f, Key.Linear, 0), (1.25f, Key.Accelerate, 1), (1.5f, Key.Linear, 1), (2, Key.Spline, 0)];
	static readonly (float Time, Key Kind, float Value)[] FlareSize = [(0, Key.Linear, 0), (1, Key.Linear, 1), (2, Key.Linear, 1)];
	static readonly (float Time, Key Kind, float Value)[] Afterglow = [(1, Key.Linear, 0), (1.25f, Key.Spline, 1), (3, Key.Linear, 1), (3.5f, Key.Accelerate, 0)];
	static readonly (float Time, Key Kind, float Value)[] BubbleSize = [(1.3f, Key.Linear, 0.5f), (2, Key.Decelerate, 2)];
	static readonly (float Time, Key Kind, float Value)[] BubbleRefract = [(1.3f, Key.Linear, 1), (2, Key.Linear, 0)];

	/// <summary>NARROW_BEAM_WIDTH, WIDE_BEAM_WIDTH, FLARE_SIZE and AFTERGLOW_SIZE; beam widths are radii.</summary>
	const float NarrowWidth = 2;
	const float WideWidth = 16;
	const float FlareRadius = 128;
	const float AfterglowRadius = 64;
	const float BubbleWidth = 256;

	/// <summary>How bright the stand-in for the refracting bubble is at full refraction.</summary>
	const float BubbleBrightness = 0.35f;

	public GameObject Owner { get; private set; }
	GameObject _gun;
	GameObject _target;
	Vector3 _end;
	bool _shot;
	float _t;
	LineRenderer _narrow;
	LineRenderer _wide;
	SpriteRenderer _flare;
	SpriteRenderer _afterglow;
	SpriteRenderer _bubble;

	public static void Start( GameObject gun, GameObject target, string mount )
	{
		if ( !gun.IsValid() )
			return;

		var owner = gun.Root;
		foreach ( var old in owner.Scene.GetAllComponents<Hl2StriderCannonEffect>().Where( x => x.Owner == owner ).ToArray() )
			old.GameObject.Destroy();

		var go = new GameObject( true, "Strider cannon" );
		go.NetworkMode = NetworkMode.Never;
		var effect = go.AddComponent<Hl2StriderCannonEffect>();
		effect.Owner = owner;
		effect._gun = gun;
		effect._target = target;
		effect._end = target.IsValid() ? target.WorldPosition : gun.WorldPosition;
		effect._narrow = SourceMountedEffects.AddBeam( go, "sprites/bluelaser1", mount );
		effect._wide = SourceMountedEffects.AddBeam( go, "effects/blueblacklargebeam", mount );
		effect._flare = SourceMountedEffects.AddSprite( go, "effects/blueblackflash", mount );
		effect._afterglow = SourceMountedEffects.AddSprite( go, "effects/blueblackflash", mount );
		effect._bubble = SourceMountedEffects.AddSprite( go, "effects/blueflare1", mount );
		effect.Draw();
	}

	/// <summary>STRIDER_MSG_BIG_SHOT: the clock jumps to the shot and runs out to the end of the envelope.</summary>
	public void Shoot( Vector3 end )
	{
		_end = end;
		_shot = true;
		_t = MathF.Max( _t, BigShotTime );
	}

	protected override void OnUpdate()
	{
		if ( !_gun.IsValid() )
		{
			GameObject.Destroy();
			return;
		}

		_t = MathF.Min( _t + Time.Delta, _shot ? EndTime : BigShotTime );
		if ( !_shot && _target.IsValid() )
			_end = _target.WorldPosition;
		if ( _t >= EndTime )
		{
			GameObject.Destroy();
			return;
		}
		Draw();
	}

	void Draw()
	{
		var gun = _gun.WorldPosition;
		SourceMountedEffects.SetBeam( _narrow, gun, _end, NarrowWidth * 2 * (Interp( NarrowSize, _t ) ?? 0), 255 * (Interp( NarrowColor, _t ) ?? 0) );

		var wideStart = Vector3.Lerp( _end, gun, Interp( WideLength, _t ) ?? 1 );
		SourceMountedEffects.SetBeam( _wide, wideStart, _end, WideWidth * 2, 255 * (Interp( WideColor, _t ) ?? 0) );

		if ( _bubble.IsValid() )
		{
			_bubble.WorldPosition = wideStart;
			SetRadius( _bubble, BubbleWidth * (Interp( BubbleSize, _t ) ?? 0), BubbleBrightness * (Interp( BubbleRefract, _t ) ?? 0) );
		}

		if ( _flare.IsValid() )
		{
			_flare.WorldPosition = gun;
			SetRadius( _flare, FlareRadius * (Interp( FlareSize, _t ) ?? 0), Interp( FlareColor, _t ) ?? 0 );
		}

		if ( _afterglow.IsValid() )
		{
			_afterglow.WorldPosition = gun;
			SetRadius( _afterglow, AfterglowRadius, Interp( Afterglow, _t ) ?? 0 );
		}
	}

	static void SetRadius( SpriteRenderer sprite, float size, float brightness )
	{
		sprite.Size = size;
		sprite.Color = (Color.White * brightness).WithAlpha( 1 );
		sprite.Enabled = size > 0 && brightness > 0;
	}

	/// <summary>CSimpleKeyList::Interp: nothing outside the keys, else eased into each key by that key's curve.</summary>
	static float? Interp( (float Time, Key Kind, float Value)[] keys, float t )
	{
		if ( t < keys[0].Time || t > keys[^1].Time )
			return null;

		for ( var i = 1; i < keys.Length; i++ )
		{
			var (from, to) = (keys[i - 1], keys[i]);
			if ( t > to.Time )
				continue;

			var f = to.Time > from.Time ? (t - from.Time) / (to.Time - from.Time) : 1;
			f = to.Kind switch
			{
				Key.Spline => f * f * (3 - 2 * f),
				Key.Accelerate => f * f,
				Key.Decelerate => 1 - (1 - f) * (1 - f),
				_ => f
			};
			return float.Lerp( from.Value, to.Value, f );
		}
		return keys[^1].Value;
	}
}

/// <summary>
/// npc_strider's skewer, MeleeAttack1Conditions and StompHit: every 5 seconds an NPC enemy within 260 units, no more than 72 to its
/// right and 64 above or below the ground, gets ACT_STRIDER_STOMPL. At the stomp event (STRIDER_AE_STOMPHITL) a victim within 260
/// of the left skewer takes 500 crush damage with NPC_Strider.Skewer, and a body it kills hangs on the skewer for 10 seconds.
/// The SDK aims the leg at the victim with IK; here the stomp plays as authored and hits a victim still in reach.
/// </summary>
[Title( "HL2 Strider Stomp" )]
[Category( "Half-Life 2" )]
public sealed class Hl2StriderStomp : SourceCreatureBehaviour
{
	const int StompHitLeft = 110;
	const string Skewer = "left skewer";
	const float StompRange = 260;
	const float CrossOver = 72;
	const float MaxStep = 64;
	const float StompDamage = 500;
	const float Cooldown = 5;
	const float ImpaleTime = 10;

	/// <summary>SF_CAN_STOMP_PLAYER: off by default, since "no more stabbing players".</summary>
	[Property] public bool StompsPlayers { get; set; }

	[Sync( SyncFlags.FromHost )] bool Stomping { get; set; }
	string _clip;
	float _hitTime;
	TimeSince _time;
	TimeSince _lastStomp = Cooldown;
	GameObject _target;
	bool _hit;
	(Vector3 At, Vector3 Stab)? _victim;

	public override string Animation => Stomping ? _clip : null;
	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(_clip, false)];

	protected override void OnConfigure( Model model, SourceModelSequences sequences )
	{
		_clip = sequences.FindActivity( "ACT_STRIDER_STOMPL" );
		var hit = sequences.Get( _clip )?.Events.FirstOrDefault( e => e.Id == StompHitLeft );
		_hitTime = (hit?.Cycle ?? 0.5f) * Duration( _clip, 0 );
	}

	/// <summary>Host think; true while stomping.</summary>
	public override bool Think( GameObject target )
	{
		if ( _clip is null )
			return false;

		if ( Stomping )
		{
			if ( !_hit && _time >= _hitTime )
			{
				_hit = true;
				Hit();
			}
			if ( _victim is { } victim && Npc.Renderer?.GetAttachmentObject( Skewer ) is { } leg && Hl2Impaled.Pin( Scene, victim.At, leg, victim.Stab, ImpaleTime ) )
				_victim = null;
			if ( _time < Duration( _clip, 1 ) )
				return true;
			Stomping = false;
			_lastStomp = 0;
			_victim = null;
			return false;
		}

		if ( !CanStomp( target ) )
			return false;

		_target = target;
		_hit = false;
		_time = 0;
		Stomping = true;
		Npc.Movement?.Stop();
		return true;
	}

	bool CanStomp( GameObject target )
	{
		if ( !target.IsValid() || _lastStomp < Cooldown )
			return false;

		var player = SourceNpcs.IsPlayer( target );
		if ( player ? !StompsPlayers : !target.GetComponent<BaseNpc>().IsValid() )
			return false;

		var delta = target.WorldPosition - WorldPosition;
		return Distance( target ) <= StompRange && delta.Dot( WorldRotation.Right ) <= CrossOver && MathF.Abs( delta.z ) <= MaxStep;
	}

	/// <summary>StriderEnemyDistance: centre to centre, with no height difference where the two boxes overlap vertically.</summary>
	float Distance( GameObject target )
	{
		var mine = GameObject.GetBounds();
		var theirs = target.GetBounds();
		var delta = theirs.Center - mine.Center;
		var overlap = (mine.Size.z + theirs.Size.z) * 0.5f;
		delta.z = delta.z > overlap ? delta.z - overlap : delta.z < -overlap ? delta.z + overlap : 0;
		return delta.Length;
	}

	void Hit()
	{
		if ( !_target.IsValid() )
			return;

		// StompHit checks the victim against the skewer, which IK has put on it; without that IK the victim only has to still be in reach.
		var victimAt = _target.WorldPosition;
		if ( Distance( _target ) > StompRange )
			return;

		var stab = (_target.GetBounds().Center + SourceNpcs.CapturePoint( _target )) * 0.5f;
		PlaySound( victimAt );
		var npc = _target.GetComponent<BaseNpc>();
		var wasNpc = npc.IsValid();
		Npc.Damage( _target, Npc.Renderer?.GetAttachment( Skewer )?.Position ?? WorldPosition, stab, SourceDamageTags.Crush, StompDamage );
		if ( wasNpc && (!npc.IsValid() || npc.Health < 1) )
			_victim = (victimAt, stab);
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlaySound( Vector3 position )
	{
		if ( SourceSounds.Sound( "NPC_Strider.Skewer", SourceMount.Of( GameObject ) ) is { } sound )
			Sound.Play( sound, position );
	}
}

/// <summary>CreateServerRagdollAttached: the victim's ragdoll hangs from the skewer by its body nearest the stab, then drops.</summary>
[Title( "HL2 Impaled" )]
[Category( "Half-Life 2" )]
public sealed class Hl2Impaled : Component
{
	GameObject _skewer;
	Rigidbody _body;
	Vector3 _stab;
	TimeUntil _release;

	/// <summary>Pins the ragdoll Npc.Die left where the victim stood, once it has its bodies; false until then. Host only.</summary>
	public static bool Pin( Scene scene, Vector3 victimAt, GameObject skewer, Vector3 stab, float time )
	{
		var ragdoll = scene.GetAllComponents<ModelPhysics>().Where( x => x.Tags.Has( "ragdoll" ) && x.WorldPosition.Distance( victimAt ) < 32 )
			.OrderBy( x => x.WorldPosition.Distance( victimAt ) ).FirstOrDefault();
		var body = ragdoll?.Bodies.Select( x => x.Component ).Where( x => x.IsValid() ).OrderBy( x => x.WorldPosition.Distance( stab ) ).FirstOrDefault();
		if ( !body.IsValid() )
			return false;

		var pin = ragdoll.GameObject.AddComponent<Hl2Impaled>();
		pin.Flags |= ComponentFlags.NotNetworked;
		pin._skewer = skewer;
		pin._body = body;
		pin._stab = body.WorldTransform.PointToLocal( stab );
		pin._release = time;
		body.MotionEnabled = false;
		return true;
	}

	protected override void OnUpdate()
	{
		if ( !_body.IsValid() )
		{
			Destroy();
			return;
		}

		if ( !_skewer.IsValid() || _release )
		{
			_body.MotionEnabled = true;
			Destroy();
			return;
		}

		_body.WorldPosition = _skewer.WorldPosition - _body.WorldRotation * _stab;
	}
}
