using System.Text.Json;
using System;

namespace Source1;

/// <summary>Reads the engine mount's converted defaults; never interprets Source console commands.</summary>
static class SourceSkillData
{
	static readonly Dictionary<string, Task<IReadOnlyDictionary<string, float>>> Parsed = new();

	/// <summary>What a mount that is not mounted yet reads as; not kept, so the next read tries again.</summary>
	static readonly IReadOnlyDictionary<string, float> Unmounted = new Dictionary<string, float>();

	/// <summary>The parsed skill values of one mount, read once per mount and config.</summary>
	public static Task<IReadOnlyDictionary<string, float>> Read( string mountName, string additionalConfig = null )
	{
		var key = $"{mountName}|{additionalConfig}";
		if ( !Parsed.TryGetValue( key, out var task ) || task.IsFaulted || task.IsCompletedSuccessfully && task.Result == Unmounted )
		{
			task = Load( mountName, additionalConfig );
			Parsed[key] = task;
		}
		return task;
	}

	static async Task<IReadOnlyDictionary<string, float>> Load( string mountName, string additionalConfig )
	{
		var mount = await Sandbox.Mounting.Directory.Mount( mountName );
		if ( mount?.IsMounted != true )
			return Unmounted;

		var values = new Dictionary<string, float>( StringComparer.OrdinalIgnoreCase );
		foreach ( var config in new[] { "skill.cfg", additionalConfig } )
		{
			if ( string.IsNullOrWhiteSpace( config ) )
				continue;

			if ( mount.GetByPath( $"mount://{mountName}/cfg/{config}.json" ) is not { } loader || await loader.GetOrCreate() is not string json )
				continue;

			foreach ( var pair in JsonSerializer.Deserialize<Dictionary<string, float>>( json ) ?? [] )
				values[pair.Key] = pair.Value;
		}
		return values;
	}

	/// <summary>A skill value, or the Source code default when skill.cfg leaves the convar unset.</summary>
	public static float Get( this IReadOnlyDictionary<string, float> values, string name, float sourceDefault )
		=> values.TryGetValue( name, out var value ) ? value : sourceDefault;
}

/// <summary>Applies mounted health defaults while preserving any damage taken during loading.</summary>
[Title( "Source NPC Defaults" )]
[Category( "Source 1" )]
public sealed class SourceNpcDefaults : Component
{
	[Property] public string AdditionalConfig { get; set; }
	[Property] public string HealthSetting { get; set; }
	[Property] public string DamageSetting { get; set; }
	[Property] public string PlayerDamageSetting { get; set; }

	protected override async void OnStart()
	{
		if ( !Networking.IsHost )
			return;

		var npc = GetComponent<Sandbox.Npcs.BaseNpc>();
		if ( !npc.IsValid() )
			return;

		var initial = npc.Health;
		var values = await SourceSkillData.Read( SourceMount.Of( GameObject ), AdditionalConfig );
		if ( !IsValid || !npc.IsValid() || npc.Health <= 0 )
			return;

		if ( !string.IsNullOrWhiteSpace( HealthSetting ) && values.TryGetValue( HealthSetting, out var health ) && health > 0 )
		{
			npc.Health = Math.Max( 1, health - Math.Max( 0, initial - npc.Health ) );
			npc.MaxHealth = health;
		}

		if ( npc is not SourceCreatureNpc creature )
			return;

		if ( !string.IsNullOrWhiteSpace( DamageSetting ) && values.TryGetValue( DamageSetting, out var damage ) && damage >= 0 )
			creature.AttackDamage = damage;

		if ( !string.IsNullOrWhiteSpace( PlayerDamageSetting ) && values.TryGetValue( PlayerDamageSetting, out var playerDamage ) && playerDamage >= 0 )
			creature.PlayerAttackDamage = playerDamage;
	}
}
