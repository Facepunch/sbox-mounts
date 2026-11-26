using Sandbox.Npcs;

namespace NS2;

internal sealed class NpcTeamMemory : GameObjectSystem<NpcTeamMemory>, Component.IDamageable.IEvents
{
	readonly Dictionary<string, AlienTargeting> teams = [];
	readonly Dictionary<BaseNpc, (GameObject Attacker, float Time)> recentHits = [];
	readonly Dictionary<Marine, float> pistolSounds = [];
	float lastUpdate;

	public NpcTeamMemory( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishFixedUpdate, 0, UpdateReports, "NS2 team reports" );
	}

	internal AlienTargeting ForFaction( string faction )
	{
		faction ??= string.Empty;
		if ( !teams.TryGetValue( faction, out var team ) ) teams[faction] = team = new();
		return team;
	}

	internal void ReportRifleFire( Marine shooter ) => ReportGunfire( shooter, 22 * MapLoader.Scale, inclusive: false );

	internal void ReportPistolShot( Marine shooter )
	{
		// NS2 keeps the parented pistol sound alive for its asset length plus the server's sound end buffer.
		pistolSounds[shooter] = Time.Now + 2.205f + 0.5f;
	}

	void ReportGunfire( Marine shooter, float range, bool inclusive )
	{
		foreach ( var listener in Scene.GetAllComponents<BaseNpc>() )
		{
			if ( listener is not (AlienLifeform or Marine or Exo) || listener.Health <= 0 || listener.Faction == shooter.Faction ) continue;
			var distance = listener.WorldPosition.DistanceSquared( shooter.WorldPosition );
			if ( inclusive ? distance > range * range : distance >= range * range ) continue;
			if ( listener.GetDisposition( shooter.GameObject ) == Disposition.Hostile )
				ForFaction( listener.Faction ).Remember( shooter.GameObject, shooter.WorldPosition );
		}
	}

	void Component.IDamageable.IEvents.OnDamaged( Component.IDamageable.DamageEvent hit )
	{
		if ( !Networking.IsHost || hit.AcceptedDamage <= 0 ) return;
		var victim = hit.Victim.GetComponent<BaseNpc>();
		if ( victim is not (AlienNpc or Babbler or Drifter or Marine or Exo or Mac) ) return;
		RecordHit( victim, hit.Instigator );
	}

	internal static void RecordArmorLoss( Component.IDamageable.DamageEvent hit, float armorLost )
	{
		if ( hit.AcceptedDamage == 0 && armorLost > 0 && hit.Victim.GetComponent<BaseNpc>() is { } victim )
			Get( victim.Scene ).RecordHit( victim, hit.Instigator );
	}

	void RecordHit( BaseNpc victim, GameObject attacker )
	{
		if ( attacker.IsValid() ) recentHits[victim] = (attacker, Time.Now);
	}

	void UpdateReports()
	{
		if ( Time.Now <= lastUpdate + 0.133f ) return;
		lastUpdate = Time.Now;
		foreach ( var (shooter, expires) in pistolSounds )
		{
			if ( !shooter.IsValid() || Time.Now >= expires )
				pistolSounds.Remove( shooter );
			else
				ReportGunfire( shooter, 14 * MapLoader.Scale, inclusive: true );
		}

		foreach ( var (victim, hit) in recentHits )
		{
			if ( !victim.IsValid() || victim.Health <= 0 || Time.Now >= hit.Time + 1 || !IsAlive( hit.Attacker ) )
			{
				recentHits.Remove( victim );
				continue;
			}

			if ( victim.GetDisposition( hit.Attacker ) == Disposition.Hostile )
				ForFaction( victim.Faction ).Remember( hit.Attacker, hit.Attacker.WorldPosition );
		}
	}

	static bool IsAlive( GameObject attacker ) =>
		attacker.IsValid() && attacker.GetComponentInChildren<Component.IDamageable>() is Component { Active: true } hp &&
		hp is not Component.IDamageable { MaxHealth: > 0, Health: <= 0 };
}
