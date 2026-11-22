using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Shock Trooper" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_shocktrooper", "strooper", MonsterClass.AlienMilitary, 125, 24, 72, 160,
	Mount = "opfor", Attack = NpcAttack.Bullet, Damage = 5, Range = 2048, ProjectileModel = "shock_effect", StrikeEvents = new[] { 4, 5, 6 }, Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed class ShockTrooper : HumanGrunt
{
	internal override NpcSounds Sounds { get; } = new( Pain: "shocktrooper/shock_trooper_pain", Death: "shocktrooper/shock_trooper_die" );
	int _charges = 8;
	TimeUntil _nextCharge;

	protected override void Think()
	{
		if ( _nextCharge <= 0 && _charges < 8 )
		{
			_charges++;
			_nextCharge = 1;
		}
		base.Think();
	}

	internal override void BeginAttack( GameObject target )
	{
		if ( WorldPosition.Distance( target.WorldPosition ) < 64 && Find( "frontkick" ) is { } kick ) PlayAction( kick );
		else if ( _charges > 0 ) AttackWith( target, Find( "crouching_mp5" ) );
	}

	internal override void FireBullet( GameObject target, Vector3 origin, Vector3 end )
	{
		if ( _charges <= 0 ) return;
		if ( _charges == 8 ) _nextCharge = 1;
		_charges--;
		EmitSound( "shocktrooper/shock_trooper_attack.wav", origin );
		NpcProjectile.Fire<ElectricBolt>( GameObject, ProjectilePath, 10, target, origin, (end - origin).Normal, 2000 );
	}

	internal override void HandleActionEvent( int id )
	{
		if ( id is 3 or 7 ) base.HandleActionEvent( id );
	}

	internal override void TossGrenade( Vector3 origin, Vector3 velocity ) =>
		NpcProjectile.Fire<SporeGrenade>( GameObject, "mount://opfor/gearbox/models/spore.mdl.vmdl", 50, null, origin, velocity.Normal, velocity.Length );

	internal override bool Radio( string sentence ) => false;
	internal override void OnGibbed() { }
	internal override NpcCorpse AddCorpseComponent( GameObject go ) => go.AddComponent<NpcCorpse>();

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		if ( Networking.IsHost && Renderer.IsValid() && Renderer.GetBodyGroup( "weapons" ) == 0 && PrefabFile.Load( "mount://opfor/entities/npcs/monster_shockroach.prefab" ) is { } prefab )
		{
			Renderer.SetBodyGroup( "weapons", 1 );
			var child = GameObject.Clone( prefab, WorldTransform.WithPosition( WorldPosition + Vector3.Up * 48 ) );
			child.NetworkSpawn( true, null );
		}
		base.OnDeath( death );
	}
}
