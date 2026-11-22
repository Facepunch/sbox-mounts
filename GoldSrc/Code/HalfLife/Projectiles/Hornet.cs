namespace GoldSrc.HalfLife;

[Hide]
public class Hornet : NpcProjectile, Component.IDamageable
{
	internal float? DeathmatchDamage { get; set; }
	internal override float LaunchSpeed => 300;
	internal override string HitTag => "bullet";
	internal override float Lifetime => _dashEndsAt > 0 ? _dashEndsAt : Networking.MaxPlayers > 1 ? 3.5f : 5f;
	float _dashEndsAt;
	bool PlayerFired => Shooter.IsValid() && Shooter.GetComponent<PlayerController>().IsValid();
	internal MonsterClass Classification => PlayerFired ? MonsterClass.PlayerWeapon : MonsterClass.AlienWeapon;

	[Property] public bool Red { get; set; }
	bool _trailStarted;
	float _health = 1;
	TimeUntil _nextTurn;
	Vector3 _lastKnown;
	float FlySpeed => Red ? 600 : 800;

	internal override void PrepareLaunch()
	{
		GameObject.Tags.Add( "hornet" );
		GameObject.AddComponent<BoxCollider>().Scale = Vector3.One * 8;
		Red = Game.Random.Int( 1, 5 ) <= 2;
		_nextTurn = 0.2f;
		_lastKnown = WorldPosition + WorldRotation.Forward * 512;
	}

	protected override void OnUpdate()
	{
		if ( _trailStarted || Age < 0.1f || Application.IsDedicatedServer ) return;
		_trailStarted = true;
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ) return;
		Color tint = Red ? new Color32( 179, 39, 14, 128 ) : new Color32( 255, 128, 0, 128 );
		ProjectileTrail.Attach( GameObject, mount, "Hornet trail", "laserbeam", 1, 2, tint );
	}

	internal override void Guide()
	{
		if ( _dashEndsAt > 0 || _nextTurn > 0 ) return;
		_nextTurn = Red ? Game.Random.Float( 0.1f, 0.3f ) : 0.1f;
		if ( !Sandbox.Npcs.BaseNpc.IsLiveTarget( Target ) )
			Target = FindPrey();

		if ( Target.IsValid() && Visible( Target ) ) _lastKnown = Npc.TargetBodyPosition( Target );
		else _lastKnown += Velocity * (FlySpeed * 0.1f);
		var aim = (_lastKnown - WorldPosition).Normal;
		var current = Velocity.Length < 0.1f ? aim : Velocity.Normal;
		var alignment = Vector3.Dot( current, aim );
		if ( alignment < 0.5f ) PlaySound( "hornet/ag_buzz", WorldPosition, 0.8f );
		var speed = Red ? 600 * (alignment <= 0 ? 0.25f : alignment) : 800;
		var heading = (aim + current).Normal;
		if ( Shooter.IsValid() && Shooter.GetComponent<Sandbox.Npcs.BaseNpc>().IsValid() )
			heading += new Vector3( Game.Random.Float( -0.1f, 0.1f ), Game.Random.Float( -0.1f, 0.1f ), Game.Random.Float( -0.1f, 0.1f ) );
		Velocity = heading * speed;
		WorldRotation = Rotation.LookAt( Velocity );

		if ( Networking.MaxPlayers <= 1 && Target.IsValid() && alignment >= 0.4f && WorldPosition.DistanceSquared( _lastKnown ) <= 300 * 300 )
		{
			Velocity *= 2;
			_dashEndsAt = (float)Age + 1;
			PlaySound( "hornet/ag_buzz", WorldPosition, 0.8f );
			ShowPuff( WorldPosition );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ShowPuff( Vector3 position )
	{
		var puff = Blast.SpriteEffect( "Hornet attack puff", "muz1", position, 0.2f, 10 );
		if ( puff.IsValid() ) puff.GetComponent<SpriteRenderer>().Color = Color.White.WithAlpha( 0.5f );
	}

	GameObject FindPrey()
	{
		GameObject nearest = null;
		var distance = 512f * 512f;
		foreach ( var obj in Scene.FindInPhysics( new Sphere( WorldPosition, 512 ) ) )
		{
			if ( obj.GetComponentInParent<IDamageable>() is not Component victim ) continue;
			var candidate = victim.GameObject;
			var delta = candidate.WorldPosition - WorldPosition;
			if ( candidate.Tags.Has( "hornet" ) || candidate == Shooter || delta.LengthSquared > distance || WorldRotation.Forward.Dot( delta.WithZ( 0 ).Normal ) <= 0.9f ) continue;
			if ( Npc.ClassRelationship( Classification, Npc.ClassOf( candidate ) ).Disposition != Sandbox.Npcs.Disposition.Hostile || !Visible( candidate ) ) continue;
			nearest = candidate;
			distance = delta.LengthSquared;
		}
		return nearest;
	}

	bool Visible( GameObject candidate )
	{
		var trace = Scene.Trace.Ray( WorldPosition, Npc.TargetEyePosition( candidate ) ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "hornet", "npc", "player", "playercontroller", "trigger" ).Run();
		return !trace.Hit || trace.GameObject?.Root == candidate.Root;
	}

	internal override void OnImpact( IDamageable victim, Vector3 position, Vector3 normal )
	{
		var damage = WeaponDamage.ForTarget( Shooter, (victim as Component)?.GameObject, Damage, DeathmatchDamage ?? Damage );
		DamageVictim( victim, position, amount: damage );
		if ( victim is not null ) PlaySound( "hornet/ag_hornethit", position );
	}

	internal override SceneTrace FlightTrace( Vector3 start, Vector3 end ) =>
		Scene.Trace.Box( new BBox( -4, 4 ), start, end ).WithoutTags( "hornet" );

	void Component.IDamageable.OnDamage( in DamageInfo damage )
	{
		if ( !Networking.IsHost || damage.Damage <= 0 ) return;
		_health -= damage.Damage;
		if ( _health <= 0 ) GameObject.Destroy();
	}

	internal override bool Bounce( SceneTraceResult hit )
	{
		if ( Age < 0.1f || !hit.GameObject.IsValid() ) return false;
		var relationship = Npc.ClassRelationship( Classification, Npc.ClassOf( hit.GameObject ) );
		if ( relationship.Disposition == Sandbox.Npcs.Disposition.Hostile ) return false;
		var direction = Velocity.Normal;
		direction = new Vector3( -direction.x, -direction.y, direction.z );
		WorldPosition = hit.EndPosition + direction * 4;
		Velocity = direction * FlySpeed;
		WorldRotation = Rotation.LookAt( direction );
		return true;
	}

	static void PlaySound( string sample, Vector3 position, float volume = 1 )
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount ) mount.NpcAudio.Emit( sample, position, volume );
	}
}

[Hide]
public sealed class HornetDart : Hornet
{
	internal override float Lifetime => 4.1f;
	internal override void Guide() { }
	internal override bool Bounce( SceneTraceResult hit ) => false;
}
