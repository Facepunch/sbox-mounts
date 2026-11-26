using Sandbox.Npcs;

namespace NS2;

internal sealed class NpcTeamMemory : GameObjectSystem<NpcTeamMemory>, Component.IDamageable.IEvents
{
	readonly Dictionary<string, AlienTargeting> teams = [];
	readonly Dictionary<BaseNpc, (GameObject Attacker, float Time)> recentHits = [];
	float lastUpdate;

	public NpcTeamMemory( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishFixedUpdate, 0, UpdateReports, "NS2 team damage reports" );
	}

	internal AlienTargeting ForFaction( string faction )
	{
		faction ??= string.Empty;
		if ( !teams.TryGetValue( faction, out var team ) ) teams[faction] = team = new();
		return team;
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
