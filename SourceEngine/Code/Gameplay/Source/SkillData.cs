using System;

namespace Source1;

static class SkillData
{
	static readonly IReadOnlyDictionary<string, float> Empty = new Dictionary<string, float>();

	public static async Task<IReadOnlyDictionary<string, float>> Read( string mountName, string additionalConfig = null )
	{
		var mount = await Sandbox.Mounting.Directory.Mount( mountName );
		if ( mount?.IsMounted != true ) return Empty;

		SkillConfigLoader Config( string name ) => mount.GetByPath( $"mount://{mountName}/cfg/{name}.json" ) as SkillConfigLoader;
		var defaults = Config( "skill.cfg" )?.Values ?? Empty;
		return string.IsNullOrWhiteSpace( additionalConfig ) ? defaults : Config( additionalConfig )?.WithDefaults( defaults ) ?? defaults;
	}

	/// <summary>A skill value, or the Source code default when skill.cfg leaves the convar unset.</summary>
	public static float Get( this IReadOnlyDictionary<string, float> values, string name, float sourceDefault )
		=> values.TryGetValue( name, out var value ) ? value : sourceDefault;
}

/// <summary>Applies mounted health defaults while preserving any damage taken during loading.</summary>
[Title( "Source NPC Defaults" )]
[Category( "Source 1" )]
[Alias( "Source1.SourceNpcDefaults", "SourceNpcDefaults" )]
public sealed class NpcDefaults : Component
{
	[Property] public string AdditionalConfig { get; set; }
	[Property] public string HealthSetting { get; set; }
	[Property] public Dictionary<string, string> AttackEventSettings { get; set; }
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
		var values = await SkillData.Read( MountContent.Of( GameObject ), AdditionalConfig );
		if ( !IsValid || !npc.IsValid() || npc.Health <= 0 )
			return;

		if ( !string.IsNullOrWhiteSpace( HealthSetting ) && values.TryGetValue( HealthSetting, out var health ) && health > 0 )
		{
			npc.Health = Math.Max( 1, health - Math.Max( 0, initial - npc.Health ) );
			npc.MaxHealth = health;
		}

		if ( npc is not CreatureNpc creature )
			return;

		if ( AttackEventSettings is { Count: > 0 } )
			creature.DamageByEvent = AttackEventSettings.Where( x => values.ContainsKey( x.Value ) ).ToDictionary( x => x.Key, x => values[x.Value] );

		if ( !string.IsNullOrWhiteSpace( DamageSetting ) && values.TryGetValue( DamageSetting, out var damage ) && damage >= 0 )
			creature.AttackDamage = damage;

		if ( !string.IsNullOrWhiteSpace( PlayerDamageSetting ) && values.TryGetValue( PlayerDamageSetting, out var playerDamage ) && playerDamage >= 0 )
			creature.PlayerAttackDamage = playerDamage;
	}
}
