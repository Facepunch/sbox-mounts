using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Pit Drone" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_pitdrone", "pit_drone", MonsterClass.AlienPredator, 40, 16, 48, 165,
	Mount = "opfor", Attack = NpcAttack.Projectile, Damage = 10, Range = 784, ProjectileModel = "pit_drone_spike", Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed class PitDrone : ExpansionPredator
{
	internal override NpcSounds Sounds { get; } = new( "pitdrone/pit_drone_idle", "pitdrone/pit_drone_alert", "pitdrone/pit_drone_pain", "pitdrone/pit_drone_die" );
	int _spikes = 6;
	internal override void ApplyEquipment() => Renderer.SetBodyGroup( "gun", _spikes == 0 ? 0 : 7 - _spikes );
	internal override string[] ExtraActions => ["reload"];
	internal override StudioSequence DistanceAttack => _spikes == 0 ? Find( "reload" ) : base.DistanceAttack;

	internal override void HandleActionEvent( int id )
	{
		if ( id == 7 ) _spikes = 6;
		else if ( id == 1 && _spikes > 0 && IsLiveTarget( Prey ) )
		{
			var origin = WorldTransform.PointToWorld( new Vector3( 15, 0, 36 ) );
			var direction = (TargetEyePosition( Prey ) - origin).Normal;
			NpcProjectile.Fire<PitDroneSpike>( GameObject, ProjectilePath, Definition.Damage, Prey, origin, direction, 900 );
			EmitSound( "pitdrone/pit_drone_attack_spike", origin );
			_spikes--;
		}
		else if ( id is 2 or 4 or 6 )
		{
			TraceMelee( 70, id == 4 ? 35 : 25, "slash", out _ );
			return;
		}
		else return;
		ApplyEquipment();
	}
}
