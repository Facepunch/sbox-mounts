using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>
/// What a spawned NPC picks for itself on the host, before its spawn goes out so every client sees the same: one of
/// <see cref="ModelChoices"/>, and a starting weapon from its <see cref="BaseCombatNpc.WeaponChoices"/> whose weapon
/// script allows <see cref="Team"/>. A weapon it was handed before it spawned is kept. Goes before the NPC's own
/// component, so its weapon is the NPC's starting weapon.
/// </summary>
[Title( "Source NPC Spawn Choice" )]
[Category( "Source 1" )]
public sealed class SourceNpcSpawnChoice : Component, Component.INetworkSpawn
{
	/// <summary>Models to spawn as, relative to the model's mount, such as a team's player models. Empty keeps the prefab's.</summary>
	[Property] public List<string> ModelChoices { get; set; } = [];

	/// <summary>A weapon-script team, such as CT or TERRORIST. Empty, or content without scripts, allows any weapon.</summary>
	[Property] public string Team { get; set; }

	bool _chosen;

	void INetworkSpawn.OnNetworkSpawn( Connection owner )
	{
		if ( Networking.IsHost )
			Choose();
	}

	protected override void OnStart()
	{
		// Not network spawned: choose before the NPC's own start does
		if ( Networking.IsHost && !_chosen )
			Choose();
	}

	void Choose()
	{
		_chosen = true;
		var npc = GetComponent<BaseCombatNpc>();
		if ( !npc.IsValid() )
			return;

		var mounted = npc.GetComponentInChildren<SourceMountedModel>( true );
		if ( ModelChoices is { Count: > 0 } && mounted.IsValid() )
			mounted.ModelPath = Game.Random.FromList( ModelChoices );

		if ( string.IsNullOrEmpty( Team ) || npc.WeaponChoices is not { Count: > 0 } || HandedWeapon( npc ) )
			return;

		if ( npc.WeaponChoices.OrderBy( _ => Game.Random.Float() ).FirstOrDefault( AllowsTeam ) is { } choice )
			npc.ArmWith( choice );
	}

	/// <summary>Whether it holds something other than its prefab's own weapon, which only a game hands it.</summary>
	bool HandedWeapon( BaseCombatNpc npc )
	{
		if ( !npc.Weapon.IsValid() || string.IsNullOrEmpty( GameObject.PrefabInstanceSource ) )
			return false;

		var own = GameObject.GetPrefab( GameObject.PrefabInstanceSource )?.GetComponent<BaseCombatNpc>()?.Weapon?.GameObject.PrefabInstanceSource;
		return !string.Equals( npc.Weapon.GameObject.PrefabInstanceSource, own, StringComparison.OrdinalIgnoreCase );
	}

	bool AllowsTeam( GameObject prefab )
	{
		var mounted = prefab?.GetComponent<BaseCombatWeapon>( true )?.WorldModelPrefab?.GetComponentInChildren<SourceMountedModel>( true );
		if ( mounted is null )
			return true;

		var path = $"mount://{mounted.MountName}/{mounted.ModelPath}.vmdl";
		if ( Sandbox.Mounting.Directory.Get( mounted.MountName )?.GetByPath( path ) is null )
			return false;

		var team = Model.Load( path )?.GetData<SourceModelWeaponData[]>()?.FirstOrDefault()?.Team;
		return string.IsNullOrEmpty( team ) || team.Equals( "ANY", StringComparison.OrdinalIgnoreCase ) || team.Equals( Team, StringComparison.OrdinalIgnoreCase );
	}
}
