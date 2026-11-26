using System;
using Sandbox.Npcs.Layers;

namespace Magicka;

/// <summary>Builds a spawnable Magicka NPC prefab from a <see cref="CharacterTemplate"/>.</summary>
internal sealed class NpcPrefabLoader(
	CharacterTemplate template,
	string templateFile,
	string modelPath,
	CharacterModelVariant variant,
	string title ) : ResourceLoader<MagickaMount>
{
	private PrefabFile _prefab;

	protected override object Load()
	{
		var modelUrl = Host.Url( modelPath, "vmdl" );
		var model = Model.Load( modelUrl );
		if ( model is null || !model.IsValid || model.IsError )
			return null;

		var clips = model.AnimationNames ?? [];
		var idle = ResolveClip( clips, ["idle", "idle_agg"], "idle" );
		var walk = ResolveClip( clips, ["move_walk", "move_run", "move_wnd"], "walk" ) ?? idle;
		var run = ResolveClip( clips, ["move_run", "move_walk"], "run" ) ?? walk;
		var death = ResolveClip( clips, ["die0", "die1"], "die" );
		var hit = ResolveHitClip( clips );

		var builder = new PrefabBuilder().WithName( Path )
			.WithMetadata( "Title", title )
			.WithMetadata( "Group", Host.Title )
			.WithMetadata( "PreviewModel", modelUrl );

		if ( idle is not null )
			builder.WithMetadata( "PreviewSequence", idle );

		var scale = variant.Scale is > 0.05f and < 10f ? variant.Scale : 1f;
		var bounds = model.RenderBounds;
		// Magicka's Stats.Length is only the inner capsule shaft (excluding hemispheres), which is
		// far shorter than the actual character. Use the scaled render bounds for accurate height,
		// and derive a snug torso width from depth (X) rather than T-pose arm span (Y).
		var height = Math.Clamp(
			bounds.Size.z > 1f ? bounds.Size.z * scale : (template.Stats.Length + template.Stats.Radius * 2f) * MagickaSpace.UnitScale * scale,
			20f, 260f );
		var depth = bounds.Size.x * scale;
		var span = bounds.Size.y * scale;
		var torsoWidth = MathF.Max( depth * 1.1f, height * 0.42f );
		if ( span > 1f )
			torsoWidth = MathF.Min( torsoWidth, MathF.Max( depth * 1.1f, span * 0.65f ) );
		var radius = Math.Clamp( torsoWidth * 0.5f, 12f, 96f );
		var baseZ = MathF.Max( bounds.Mins.z * scale, 0f );
		var moveSpeed = template.Stats.Speed > 0.1f
			? Math.Clamp( template.Stats.Speed * MagickaSpace.UnitScale, 40f, 360f )
			: 110f;

		var weapon = ResolveWeapon( out var weaponScale );
		var (meleeClips, rangedClips, healClips) = ResolveAbilityClips( clips );
		var attacks = meleeClips.Concat( rangedClips ).Distinct( StringComparer.OrdinalIgnoreCase ).ToArray();
		var combat = DeriveCombatProfile( radius, weapon );
		var friendly = (template.Factions & (MagickaFactions.Friendly | MagickaFactions.Human | MagickaFactions.Wizard)) != 0
			&& (template.Factions & (MagickaFactions.Evil | MagickaFactions.Demon | MagickaFactions.Undead)) == 0;
		var passive = friendly && !combat.HasMelee && !combat.HasRanged;

		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;

			var root = new GameObject( true, title );
			root.Tags.Add( MagickaMount.NpcTag );
			root.NetworkMode = NetworkMode.Object;

			var agent = root.AddComponent<NavMeshAgent>();
			agent.Height = height;
			agent.Radius = radius;
			agent.Acceleration = 2000f;
			agent.MaxSpeed = moveSpeed;

			root.AddComponent<NavigationLayer>();
			root.AddComponent<AnimationLayer>();

			var senses = root.AddComponent<SensesLayer>();
			senses.FieldOfView = 300f;
			senses.HearingRange = 1200f;
			senses.SightRange = template.AlertRadius > 1f
				? Math.Clamp( template.AlertRadius * MagickaSpace.UnitScale, 512f, 2048f )
				: 1200f;

			var body = root.AddComponent<Rigidbody>();
			body.MotionEnabled = false;
			body.EnableImpactDamage = false;
			body.AngularDamping = 2f;
			body.LinearDamping = 0.5f;
			body.Locking = new PhysicsLock { Pitch = true, Roll = true };

			var visuals = new GameObject( root, true, "model" );
			if ( MathF.Abs( scale - 1f ) > 0.001f )
				visuals.LocalScale = Vector3.One * scale;

			var renderer = visuals.AddComponent<SkinnedModelRenderer>();
			renderer.Model = model;
			renderer.UseAnimGraph = false;
			renderer.CreateAttachments = true;
			renderer.CreateBoneObjects = true;
			var tint = Color.White;
			if ( variant.Tint.LengthSquared > 0.01f )
			{
				// Normalize HDR tints (e.g. elemental_fire's 1.8, 0.8, 0.4) into 0..1 so s&box's
				// 8-bit instance tint doesn't wrap channels > 1.0.
				var max = MathF.Max( 1f, MathF.Max( variant.Tint.x, MathF.Max( variant.Tint.y, variant.Tint.z ) ) );
				tint = new Color(
					Math.Clamp( variant.Tint.x / max, 0f, 1f ),
					Math.Clamp( variant.Tint.y / max, 0f, 1f ),
					Math.Clamp( variant.Tint.z / max, 0f, 1f ),
					1f );
			}
			if ( string.Equals( template.Id, "elemental_cold", StringComparison.OrdinalIgnoreCase ) )
				tint = new Color( 0.5f, 0.8f, 1.0f, 1f );
			if ( tint != Color.White )
				renderer.Tint = tint;

			if ( idle is not null )
			{
				renderer.Sequence.Name = idle;
				renderer.Sequence.Looping = true;
			}

			var hitboxes = visuals.AddComponent<ModelHitboxes>();
			hitboxes.Renderer = renderer;
			hitboxes.Target = root;

			if ( weapon is not null )
			{
				var held = visuals.AddComponent<MagickaHeldItem>();
				held.Renderer = renderer;
				held.ModelPath = Host.Url( weapon, "vmdl" );
				held.Bone = ResolveWeaponBone();
				held.Scale = weaponScale;
				held.FallbackOffset = new Vector3( 6f, -10f, height * 0.55f );
			}

			var hull = new GameObject( root, true, "collider" );
			var collider = hull.AddComponent<BoxCollider>();
			collider.Center = new Vector3( 0, 0, baseZ + height * 0.5f );
			collider.Scale = new Vector3( radius * 2f, radius * 2f, height );
			collider.Surface = Surface.FindByName( MagickaMount.SurfaceForBloodType( template.BloodType ) );
			var npc = root.AddComponent<MagickaNpc>();
			npc.Renderer = renderer;
			npc.CharacterName = title;
			npc.StartingHealth = ScaleHealth( template.Stats.MaxHitpoints );
			npc.MoveSpeed = moveSpeed;
			npc.HasMelee = combat.HasMelee;
			npc.MeleeRange = combat.MeleeRange;
			npc.MeleeInterval = combat.MeleeInterval;
			npc.HasRanged = combat.HasRanged;
			npc.RangedMinRange = combat.RangedMinRange;
			npc.RangedMaxRange = combat.RangedMaxRange;
			npc.RangedInterval = combat.RangedInterval;
			npc.CanSelfHeal = combat.CanSelfHeal;
			npc.HealCooldown = combat.HealCooldown;
			npc.AttackHeight = height * 0.5f;
			npc.Friendly = friendly;
			npc.Passive = passive;
			npc.IdleClip = idle;
			npc.WalkClip = walk;
			npc.RunClip = run;
			npc.DeathClip = death;
			npc.HitClip = hit;
			npc.AttackClips = attacks;
			npc.MeleeClips = meleeClips;
			npc.RangedClips = rangedClips;
			npc.HealClips = healClips;
			npc.MissileModel = combat.Missile is null ? null : Host.Url( combat.Missile, "vmdl" );
			npc.MissileColor = combat.MissileColor;
			npc.EyeHeight = height * 0.8f;
			npc.AlertSounds = ResolveSound( template.Sounds.AlertCue, "angry", "alert", "idle" );
			npc.AttackSounds = ResolveSound( template.Sounds.AttackCue, "attack", "swing", "angry" );
			npc.PainSounds = ResolveSound( template.Sounds.PainCue, "pain", "hurt" );
			npc.DeathSounds = ResolveSound( template.Sounds.DeathCue, "death", "die" );
			npc.VoiceSounds = ResolveVoiceSounds( friendly || passive );
		}

		return _prefab = builder.Create();
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( _prefab );
		_prefab = null;
		base.Shutdown();
	}

	private string ResolveClip( IReadOnlyList<string> modelClips, string[] slots, string fallbackSubstring )
	{
		foreach ( var slot in slots )
		{
			if ( template.Animations.TryGetValue( slot, out var entry ) && HasClip( modelClips, entry.ClipName, out var matched ) )
				return matched;
		}

		foreach ( var name in modelClips )
		{
			if ( name.Contains( fallbackSubstring, StringComparison.OrdinalIgnoreCase )
				&& !name.Contains( "wnd", StringComparison.OrdinalIgnoreCase ) )
				return name;
		}

		return null;
	}

	private string ResolveHitClip( IReadOnlyList<string> modelClips )
	{
		foreach ( var slot in new[] { "hit", "hit0" } )
		{
			if ( template.Animations.TryGetValue( slot, out var entry ) && HasClip( modelClips, entry.ClipName, out var matched ) )
				return matched;
		}

		// Exact preferred standing flinch clip names first.
		foreach ( var preferred in new[] { "hit", "hit0", "common_hit", "brute_hit0", "raider_hit0", "zombie_hit0", "twohand_hit0" } )
		{
			if ( HasClip( modelClips, preferred, out var matched ) )
				return matched;
		}

		foreach ( var name in modelClips )
		{
			if ( name.Contains( "hit", StringComparison.OrdinalIgnoreCase )
				&& !name.Contains( "fly", StringComparison.OrdinalIgnoreCase )
				&& !name.Contains( "slide", StringComparison.OrdinalIgnoreCase )
				&& !name.Contains( "stun", StringComparison.OrdinalIgnoreCase )
				&& !name.Contains( "attack", StringComparison.OrdinalIgnoreCase )
				&& !name.Contains( "sit", StringComparison.OrdinalIgnoreCase )
				&& !name.Contains( "recoil", StringComparison.OrdinalIgnoreCase ) )
				return name;
		}

		return null;
	}

	private sealed record CombatProfile(
		bool HasMelee,
		float MeleeRange,
		float MeleeInterval,
		bool HasRanged,
		float RangedMinRange,
		float RangedMaxRange,
		float RangedInterval,
		string Missile,
		Color MissileColor,
		bool CanSelfHeal,
		float HealCooldown );

	private static bool IsMeleeAbility( CharacterAbility a ) =>
		a.TypeName.Equals( "Melee", StringComparison.OrdinalIgnoreCase )
		|| a.TypeName.Equals( "Dash", StringComparison.OrdinalIgnoreCase )
		|| a.TypeName.Equals( "Jump", StringComparison.OrdinalIgnoreCase )
		|| a.TypeName.Equals( "PickUpCharacter", StringComparison.OrdinalIgnoreCase )
		|| a.TypeName.Equals( "ZombieGrip", StringComparison.OrdinalIgnoreCase );

	private static bool IsOffensiveSpell( CharacterAbility a ) =>
		a.TypeName.Equals( "CastSpell", StringComparison.OrdinalIgnoreCase )
		&& a.Target == CharacterAbilityTarget.Enemy
		&& a.CastType != 3; // CastType.Self = 3

	private static bool IsSelfHealSpell( CharacterAbility a ) =>
		a.TypeName.Equals( "CastSpell", StringComparison.OrdinalIgnoreCase )
		&& a.Target == CharacterAbilityTarget.Self
		&& (a.Element & 0x40) != 0; // Elements.Life = 0x40

	private static bool IsOffensiveRanged( CharacterAbility a ) =>
		a.TypeName.Equals( "Ranged", StringComparison.OrdinalIgnoreCase )
		&& a.Target != CharacterAbilityTarget.Self;

	private (string[] Melee, string[] Ranged, string[] Heal) ResolveAbilityClips( IReadOnlyList<string> modelClips )
	{
		var melee = new List<string>();
		var ranged = new List<string>();
		var heal = new List<string>();
		var seenMelee = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var seenRanged = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var seenHeal = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		void Collect( CharacterAbility ability, List<string> dst, HashSet<string> seen )
		{
			foreach ( var slot in ability.Animations )
			{
				var clipName = template.Animations.TryGetValue( slot, out var entry ) ? entry.ClipName : slot;
				if ( HasClip( modelClips, clipName, out var matched ) && seen.Add( matched ) )
					dst.Add( matched );
			}
		}

		foreach ( var ability in template.Abilities )
		{
			if ( IsSelfHealSpell( ability ) )
				Collect( ability, heal, seenHeal );
			else if ( IsOffensiveRanged( ability ) || IsOffensiveSpell( ability ) )
				Collect( ability, ranged, seenRanged );
			else if ( IsMeleeAbility( ability ) )
				Collect( ability, melee, seenMelee );
		}

		// Fallbacks from clip names when templates don't name explicit clips.
		foreach ( var name in modelClips )
		{
			if ( name.Contains( "recoil", StringComparison.OrdinalIgnoreCase ) )
				continue;

			if ( name.StartsWith( "cast", StringComparison.OrdinalIgnoreCase ) )
			{
				if ( seenRanged.Add( name ) ) ranged.Add( name );
				if ( seenHeal.Add( name ) ) heal.Add( name );
			}
			else if ( name.Contains( "attack", StringComparison.OrdinalIgnoreCase ) )
			{
				if ( seenMelee.Add( name ) ) melee.Add( name );
			}
		}

		if ( ranged.Count == 0 ) ranged.AddRange( melee );
		if ( melee.Count == 0 ) melee.AddRange( ranged );
		if ( heal.Count == 0 ) heal.AddRange( ranged );

		return (melee.ToArray(), ranged.ToArray(), heal.ToArray());
	}

	private CombatProfile DeriveCombatProfile( float radius, string weapon )
	{
		var meleeAbilities = template.Abilities.Where( IsMeleeAbility ).ToList();
		var rangedAbilities = template.Abilities.Where( IsOffensiveRanged ).ToList();
		var spellAbilities = template.Abilities.Where( IsOffensiveSpell ).ToList();
		var healAbility = template.Abilities.FirstOrDefault( IsSelfHealSpell );

		// Melee reach is always sensible close-quarters reach (ignore bogus 50m melee flags).
		var hasMelee = meleeAbilities.Count > 0;
		var meleeRange = Math.Clamp( radius + 48f, 64f, 140f );
		var meleeInterval = 1.3f;
		if ( hasMelee )
		{
			var bestReach = meleeAbilities.Select( a => a.MaxRange ).Where( r => r is > 0.2f and < 12f ).DefaultIfEmpty( 2f ).Max();
			meleeRange = Math.Clamp( MathF.Max( bestReach * MagickaSpace.UnitScale, radius + 40f ), 64f, 160f );
			var cd = meleeAbilities.Select( a => a.Cooldown ).Where( c => c > 0.2f ).DefaultIfEmpty( 1.3f ).Min();
			meleeInterval = Math.Clamp( cd, 0.7f, 3.0f );
		}

		// Ranged / spell profile from offensive ranged or spell abilities only.
		var offensiveRanged = rangedAbilities.Concat( spellAbilities ).ToList();
		var hasRanged = offensiveRanged.Count > 0;
		var rangedMin = 0f;
		var rangedMax = 500f;
		var rangedInterval = 2.0f;
		string missile = null;
		var missileColor = Color.White;

		if ( hasRanged )
		{
			var minRaw = offensiveRanged.Min( a => a.MinRange );
			var maxRaw = offensiveRanged.Select( a => a.MaxRange ).Where( r => r > 0.5f ).DefaultIfEmpty( 12f ).Max();
			rangedMin = Math.Clamp( minRaw * MagickaSpace.UnitScale, 0f, 220f );
			rangedMax = Math.Clamp( maxRaw * MagickaSpace.UnitScale, 260f, 900f );
			if ( rangedMin > rangedMax * 0.5f )
				rangedMin = rangedMax * 0.25f;

			var cd = offensiveRanged.Select( a => a.Cooldown ).Where( c => c > 0.2f ).DefaultIfEmpty( 2.2f ).Min();
			rangedInterval = Math.Clamp( cd, 0.9f, 4.0f );

			// Resolve missile: weapon projectile for Ranged abilities first, then spell element missile.
			if ( rangedAbilities.Count > 0 && weapon is not null )
			{
				foreach ( var slot in template.Equipment.OrderBy( e => e.Slot ) )
				{
					var proj = Host.ResolveItemProjectile( templateFile, slot.Item );
					if ( proj is not null && Host.GetByPath( $"mount://{Host.Ident}/{proj}.vmdl" ) is not null )
					{
						missile = proj;
						break;
					}
				}
			}

			if ( missile is null && spellAbilities.Count > 0 )
			{
				var primarySpell = spellAbilities.FirstOrDefault( a => a.Element != 0 ) ?? spellAbilities[0];
				var candidate = MagickaMount.MissileForElement( primarySpell.Element );
				if ( Host.GetByPath( $"mount://{Host.Ident}/{candidate}.vmdl" ) is not null )
				{
					missile = candidate;
					missileColor = MagickaMount.ColorForElement( primarySpell.Element );
				}
			}

			if ( missile is null )
			{
				const string fallbackMissile = "models/missiles/fire1";
				if ( Host.GetByPath( $"mount://{Host.Ident}/{fallbackMissile}.vmdl" ) is not null )
					missile = fallbackMissile;
			}

			if ( missile is null )
				hasRanged = false;
		}

		// If an enemy has no parsed melee or ranged abilities, give it a basic melee attack.
		if ( !hasMelee && !hasRanged && template.Abilities.Count > 0 )
			hasMelee = true;

		var canSelfHeal = healAbility is not null;
		var healCooldown = healAbility is not null ? Math.Clamp( healAbility.Cooldown, 4f, 15f ) : 8f;

		return new CombatProfile(
			hasMelee, meleeRange, meleeInterval,
			hasRanged, rangedMin, rangedMax, rangedInterval,
			missile, missileColor,
			canSelfHeal, healCooldown );
	}

	/// <summary>Scales Magicka's high-damage HP pool (~10x) down to s&amp;box sandbox weapon scale.</summary>
	private static float ScaleHealth( float raw )
	{
		if ( raw <= 1f )
			return 100f;

		var scaled = Math.Clamp( raw * 0.12f, 45f, 1500f );
		return MathF.Round( scaled / 5f ) * 5f;
	}

	private static bool HasClip( IReadOnlyList<string> clips, string name, out string matched )
	{
		if ( !string.IsNullOrEmpty( name ) )
		{
			foreach ( var clip in clips )
			{
				if ( string.Equals( clip, name, StringComparison.OrdinalIgnoreCase ) )
				{
					matched = clip;
					return true;
				}
			}
		}

		matched = null;
		return false;
	}

	/// <summary>The first equipped item whose model the mount registered: right hand first.</summary>
	private string ResolveWeapon( out float scale )
	{
		scale = 1f;
		string fallback = null;
		float fallbackScale = 1f;

		foreach ( var slot in template.Equipment.OrderBy( e => e.Slot ) )
		{
			var model = Host.ResolveItemModel( templateFile, slot.Item );
			if ( model is null || Host.GetByPath( $"mount://{Host.Ident}/{model}.vmdl" ) is null )
				continue;

			var item = Host.ResolveItem( templateFile, slot.Item );
			var itemScale = item?.Scale is > 0.05f and < 10f ? item.Scale : 1f;

			// Prefer the right hand or a plain attachment over a shield or a body-mounted item.
			if ( slot.Bone?.Contains( "right", StringComparison.OrdinalIgnoreCase ) == true
				|| slot.Bone?.Contains( "attach", StringComparison.OrdinalIgnoreCase ) == true )
			{
				if ( model.Contains( "shield", StringComparison.OrdinalIgnoreCase ) )
				{
					fallback ??= model;
					fallbackScale = itemScale;
					continue;
				}

				scale = itemScale;
				return model;
			}

			fallback ??= model;
			fallbackScale = itemScale;
		}

		scale = fallbackScale;
		return fallback;
	}

	private string ResolveWeaponBone()
	{
		var slot = template.Equipment.OrderBy( e => e.Slot ).FirstOrDefault();
		return string.IsNullOrEmpty( slot.Bone ) ? "rightattach" : slot.Bone;
	}

	private string[] ResolveSound( string explicitCue, params string[] suffixes )
	{
		if ( Host.ResolveCueWaves( explicitCue ) is { } direct )
			return direct;

		var id = template.Id?.ToLowerInvariant() ?? "";
		var stem = id.Contains( '_' ) ? id[..id.IndexOf( '_' )] : id;
		foreach ( var suffix in suffixes )
		{
			if ( Host.ResolveCueWaves( $"chr_{id}_{suffix}" ) is { } exact ) return exact;
			if ( Host.ResolveCueWaves( $"boss_{id}_{suffix}" ) is { } bossExact ) return bossExact;
			if ( stem.Length > 0 && Host.ResolveCueWaves( $"chr_{stem}_{suffix}" ) is { } byStem ) return byStem;
			if ( stem.Length > 0 && Host.ResolveCueWaves( $"boss_{stem}_{suffix}" ) is { } bossStem ) return bossStem;
		}

		return [];
	}

	private string[] ResolveVoiceSounds( bool isHumanOrFriendly )
	{
		var resolved = ResolveSound( template.Sounds.AlertCue, "talk", "idle", "alert" );
		if ( resolved.Length > 0 )
			return resolved;

		if ( !isHumanOrFriendly )
			return [];

		var talks = new List<string>();
		if ( Host.ResolveCueWaves( "amb_people_talk01" ) is { } t1 ) talks.AddRange( t1 );
		if ( Host.ResolveCueWaves( "amb_people_talk02" ) is { } t2 ) talks.AddRange( t2 );
		return talks.ToArray();
	}
}
