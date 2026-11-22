using Sandbox.Npcs.Layers;

namespace GoldSrc.HalfLife;

[Title( "Apache" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeApache" )]
[NpcDefinition( "monster_apache", "apache", MonsterClass.HumanMilitary, 250, 52, 52, 265, MetalBody = true, Attack = NpcAttack.Bullet, Damage = 10, Range = 4092, Movement = NpcMovement.Flying, ProjectileModel = "hvr" )]
public class Apache : Aircraft
{
	internal override float ViewDot => -0.707f;
	internal override float SightRange => 4092;

	internal override float SpeedPerPitchStep => 50;
	internal override float RotorAttenuation => 0.3f;
	internal override string RotorSample => "apache/ap_rotor2.wav";
	internal override string DebrisModel => "metalplategibs_green";
	internal override float HoverClearance => 240;

	readonly MountedGun _gun = new();
	[Sync( SyncFlags.FromHost )] Angles GunAim { get; set; }
	[Sync( SyncFlags.FromHost )] float NosePitch { get; set; }
	TimeUntil _nextThink, _nextRocket;
	TimeSince _tracking;
	GameObject _enemy;
	int _rockets = 10, _smokePuffs;
	bool _loaded;

	internal override void OnModelLoaded( ModelLoader loader )
	{
		_gun.Bind( loader, Renderer );
		Animation.TurnsBody = false;
		if ( Movement is FlyingLayer flight ) flight.HoverHeight = HoverClearance;
		_loaded = true;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !_loaded ) return;
		Renderer.LocalRotation = Rotation.FromPitch( NosePitch );
		_gun.Pose( Renderer, GunAim );
	}

	protected override void Think()
	{
		if ( !_loaded || _nextThink > 0 ) return;
		_nextThink = 0.1f;
		if ( _smokePuffs > 0 || Game.Random.Int( 0, 99 ) > Health )
		{
			Blast.Smoke( WorldPosition + Vector3.Down * 32, Game.Random.Float( 2, 2.9f ) );
			if ( _smokePuffs > 0 ) _smokePuffs--;
		}

		if ( _rockets % 2 == 1 ) LaunchRocket();
		var enemy = FindVisibleEnemy();
		if ( !enemy.IsValid() )
		{
			_enemy = null;
			Movement?.Stop();
			return;
		}
		if ( _enemy != enemy ) _tracking = 0;
		_enemy = enemy;
		var point = enemy.GetBounds().Center;
		var away = (WorldPosition - point).WithZ( 0 ).Normal;
		Movement?.MoveTo( point + away * 700, 24, Definition.Speed, enemy );
		var bearing = (point - WorldPosition).EulerAngles;
		WorldRotation = Rotation.Slerp( WorldRotation, Rotation.FromYaw( bearing.yaw ), 0.12f );
		var rackCenter = WorldPosition + Vector3.Down * 118;
		NosePitch = NosePitch.Approach( (point - rackCenter).EulerAngles.Normal.pitch.Clamp( -15, 35 ), 4 );
		var basis = WorldRotation * Rotation.FromPitch( NosePitch );
		var pivot = Renderer.GetAttachment( "attachment2" )?.Position ?? WorldPosition;
		var direction = (point - pivot).Normal;
		var desired = (basis.Inverse * direction).EulerAngles.Normal;
		GunAim = new Angles( GunAim.pitch.Approach( desired.pitch.Clamp( -10, 45 ), 12 ), GunAim.yaw.Approach( desired.yaw.Clamp( -90, 90 ), 12 ), 0 );
		var barrel = Renderer.GetAttachment( "attachment1" )?.Position ?? pivot;
		var aim = (barrel - pivot).Normal;
		if ( _tracking > 2 && aim.Dot( direction ) > 0.98f )
		{
			TraceBullets( barrel, barrel + aim * 8192, 1, Definition.Damage, 0.0349f, 8192, tracerFrequency: 1 );
			PlayGunshot( "turret/tu_fire1.wav", barrel, attenuation: 0.3f );
		}

		if ( _nextRocket > 0 || _rockets % 2 != 0 ) return;
		var velocity = (Movement as FlyingLayer)?.Velocity ?? Vector3.Zero;
		var course = (basis.Forward * 800 + velocity).Normal;
		if ( course.Dot( (point - rackCenter).Normal ) <= 0.965f ) return;
		var hit = Scene.Trace.Ray( rackCenter, rackCenter + course * 4096 ).IgnoreGameObjectHierarchy( GameObject ).Run();
		if ( hit.Hit && hit.EndPosition.Distance( point ) < 512 ) LaunchRocket();
	}

	void LaunchRocket()
	{
		var basis = WorldRotation * Rotation.FromPitch( NosePitch );
		var side = _rockets % 2 == 0 ? 1 : -1;
		var port = (_rockets % 5) switch { 0 => basis.Right, 1 => basis.Left, 2 => basis.Up, 3 => basis.Down, _ => Vector3.Zero };
		var source = WorldPosition + basis.Forward * 31.5f + basis.Right * (105 * side) + basis.Down * 118.5f + port * 10;
		Blast.Smoke( source, 2 );
		NpcProjectile.Fire<HelicopterRocket>( this, null, source, basis.Forward );
		_rockets--;
		if ( _rockets == 0 )
		{
			_nextRocket = 10;
			_rockets = 10;
		}
		else _nextRocket = 0.5f;
	}

	internal override void ModifyDamage( Component.IDamageable.DamageEvent damage )
	{
		var info = damage.DamageInfo;
		if ( info.Weapon?.GetComponent<HelicopterRocket>()?.Shooter == GameObject ) { damage.Cancelled = true; return; }
		if ( HitStudioModel( info ) )
		{
			if ( HitGroups( info ).Has( "hitgroup_6" ) && (info.Tags.Has( "bullet" ) || info.Tags.Has( "club" ) || info.Tags.Has( "energybeam" )) )
			{
				damage.Cancelled = true;
				return;
			}
			if ( damage.AcceptedDamage <= 50 && !HitGroups( info ).Has( "hitgroup_1" ) && !HitGroups( info ).Has( "hitgroup_2" ) )
			{
				ArmorRicochet.Show( info.Position, 2 );
				damage.Cancelled = true;
				return;
			}
			_smokePuffs = 3 + (int)(damage.AcceptedDamage / 5);
		}
		if ( info.Tags.Has( "blast" ) || info.Tags.Has( "explosion" ) ) damage.AcceptedDamage *= 2;
	}
}
