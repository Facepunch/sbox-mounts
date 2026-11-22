namespace GoldSrc.HalfLife;

[Hide]
public sealed class CrossbowBolt : NpcProjectile
{
	internal const string ModelAsset = "mount://hl1/valve/models/crossbow_bolt.mdl.vmdl";
	TimeUntil? _detonateAt;
	float _nextBubble = 0.2f;

	internal override void Guide()
	{
		WorldRotation *= Rotation.FromRoll( 10 * Time.Delta );
		if ( Age < _nextBubble ) return;
		_nextBubble = Age + 0.1f;
		if ( WaterBody.Find( Scene, WorldPosition ).IsValid() ) Bubble( WorldPosition - Velocity * 0.1f, WorldPosition );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Bubble( Vector3 start, Vector3 end ) => Blast.BubbleTrail( Game.ActiveScene, start, end, 1, "Crossbow bubble" );

	internal override void HandleContact( SceneTraceResult hit )
	{
		var direction = Velocity.Normal;
		var victim = hit.GameObject?.GetComponentInParent<IDamageable>();
		if ( victim is not null )
		{
			var player = WeaponDamage.IsPlayer( hit.GameObject );
			var tags = new TagSet { "nevergib" };
			if ( !player ) tags.Add( "bullet" );
			var damage = player ? WeaponDamage.ForTarget( Shooter, hit.GameObject, 10, 20 ) : Damage;
			BaseCombatWeapon.ShootBullet( hit, damage, 0, Shooter, GameObject, tags );
		}
		WorldPosition = hit.EndPosition + (victim is null ? -direction * 12 : hit.Normal * 2);
		Velocity = Vector3.Zero;
		ImpactEffects( hit.EndPosition, direction, victim is not null, false );
		if ( Networking.MaxPlayers > 1 )
		{
			_detonateAt = 0.1f;
			return;
		}

		if ( victim is null && (hit.Body is null || hit.Body.BodyType == PhysicsBodyType.Static) )
		{
			WorldRotation = Rotation.LookAt( direction ) * Rotation.FromRoll( Game.Random.Float( 0, 360 ) );
			GameObject.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 10;
			Enabled = false;
			return;
		}

		GameObject.Destroy();
	}

	protected override void OnFixedUpdate()
	{
		if ( _detonateAt is not { } delay )
		{
			base.OnFixedUpdate();
			return;
		}
		if ( delay > 0 ) return;
		Blast.DamageInRadius( GameObject, Shooter, WorldPosition, 0, 128, 40, "blast", "alwaysgib" );
		Explode( WorldPosition );
		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Explode( Vector3 position )
	{
		bool water = WaterBody.Find( Game.ActiveScene, position ).IsValid();
		Blast.ExplosionFlash( "Crossbow bolt explosion", position, 1, water );
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
			NpcAudio.Play( mount.NpcAudio.Find( $"weapons/explode{Game.Random.Int( 3, 5 )}.wav" ), position, attenuation: 0.3f );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void ImpactEffects( Vector3 position, Vector3 direction, bool body, bool stick )
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ) return;
		if ( body )
		{
			NpcAudio.Play( mount.NpcAudio.Find( "weapons/xbow_hitbod" ), position );
			return;
		}
		NpcAudio.Play( mount.NpcAudio.Find( "weapons/xbow_hit1.wav" ), position, Game.Random.Float( 0.95f, 1 ), Game.Random.Float( 0.98f, 1.05f ) );
		if ( !WaterBody.Find( Game.ActiveScene, position ).IsValid() ) StudioSparks.Emit( position, 6 );
		if ( !stick ) return;
		var bolt = new GameObject( false, "Embedded crossbow bolt" );
		bolt.NetworkMode = NetworkMode.Never;
		bolt.WorldPosition = position - direction * 10;
		bolt.WorldRotation = Rotation.LookAt( direction );
		bolt.AddComponent<ModelRenderer>().Model = Model.Load( ModelAsset );
		bolt.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 5;
		bolt.Enabled = true;
	}
}
