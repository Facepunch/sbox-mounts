using Sandbox.Npcs.Layers;

namespace UnityMount;

public sealed record PostalNpcDef(
	string Id,
	string Title,
	PostalNpcArchetype Archetype,
	PostalNpcAttackStyle AttackStyle,
	string ModelPath,
	string AnimatedRigName,
	float MaxHealth,
	float MoveSpeed,
	float RunSpeed,
	float AttackDamage,
	float AttackRange,
	float AttackCooldown,
	float ModelScale,
	string NoticeSoundPath,
	string AttackSoundPath,
	string PainSoundPath,
	string DeathSoundPath,
	string ResolvedModelPath = "" )
{
	public string EffectiveModelPath => !string.IsNullOrEmpty( ResolvedModelPath ) ? ResolvedModelPath : ModelPath;
}

/// <summary>
/// Builds a registered s&amp;box PrefabFile for a functional POSTAL: Brain Damaged NPC using Sandbox.Mounting.PrefabBuilder,
/// tagged with "npc" so it automatically appears in Sandbox's NPCs -> POSTAL: Brain Damaged tab!
/// </summary>
public sealed class NpcPrefabLoader( PostalNpcDef def ) : ResourceLoader<UnityGameMount>
{
	PrefabFile _prefab;

	protected override object Load()
	{
		string modelPath = def.EffectiveModelPath;
		var builder = new PrefabBuilder()
			.WithName( Path )
			.WithMetadata( "Title", def.Title )
			.WithMetadata( "PreviewModel", modelPath );

		using ( builder.Scope() )
		{
			var root = new GameObject( true, def.Id );
			root.Tags.Add( "npc" );

			var cap = root.AddComponent<CapsuleCollider>();
			cap.Start = Vector3.Up * 18f;
			cap.End = Vector3.Up * 56f;
			cap.Radius = 18f;

			var senses = root.AddComponent<SensesLayer>();
			senses.SightRange = def.Archetype == PostalNpcArchetype.RangedEnemy ? 1550f : 1150f;
			senses.HearingRange = 1250f;
			senses.FieldOfView = 155f;

			var npc = root.AddComponent<PostalNpc>();
			npc.DisplayName = def.Title;
			npc.Archetype = def.Archetype;
			npc.AttackStyle = def.AttackStyle;
			npc.ModelPath = modelPath;
			npc.Health = def.MaxHealth;
			npc.MaxHealth = def.MaxHealth;
			npc.MoveSpeed = def.MoveSpeed;
			npc.RunSpeed = def.RunSpeed;
			npc.AttackDamage = def.AttackDamage;
			npc.AttackRange = def.AttackRange;
			npc.AttackCooldown = def.AttackCooldown;
			npc.ModelScale = def.ModelScale;
			npc.NoticeSoundPath = def.NoticeSoundPath;
			npc.AttackSoundPath = def.AttackSoundPath;
			npc.PainSoundPath = def.PainSoundPath;
			npc.DeathSoundPath = def.DeathSoundPath;

			var model = Model.Load( modelPath );
			if ( model is not null && !model.IsError )
				PostalNpc.AttachGroundedVisual( root, model, def.ModelScale, cap, npc );
		}

		_prefab = builder.Create();
		return _prefab;
	}

	protected override void Shutdown()
	{
		if ( _prefab is not null )
		{
			PrefabBuilder.Destroy( _prefab );
			_prefab = null;
		}
		base.Shutdown();
	}
}
