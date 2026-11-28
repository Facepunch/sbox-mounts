// Ported from OpenMagicka (MIT, https://github.com/vogster/OpenMagicka),
// src/OpenMagicka.Content/Magicka/TemplateReaders.*.cs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Magicka;

internal static partial class XnbReaders
{
	private const int WeaponClasses = 27;
	private const int MaxCharacterLights = 4;
	private const int MaxEventCollections = 5;
	private const int PointLightSize = 4 + 12 + 12 + 4 + 1 + 4 + 4;
	private const int ResistanceSize = 13;
	private const int DamageSize = 16;

	private static object ReadCharacterTemplate( XnbContentReader r )
	{
		var id = r.ReadString();
		var displayId = r.ReadString();
		var factions = (MagickaFactions)r.ReadInt32();
		var bloodType = r.ReadInt32();

		// IsEthereal, LooksEthereal, Fearless, Uncharmable, Nonslippery, HasFairy, CanSeeInvisible
		var isEthereal = r.ReadBoolean();
		var looksEthereal = r.ReadBoolean();
		var fearless = r.ReadBoolean();
		var uncharmable = r.ReadBoolean();
		var nonslippery = r.ReadBoolean();
		var hasFairy = r.ReadBoolean();
		var canSeeInvisible = r.ReadBoolean();
		_ = (isEthereal, looksEthereal, fearless, uncharmable, nonslippery, hasFairy, canSeeInvisible);

		// Sounds
		var soundCount = r.ReadCount( 1 + 4 );
		for ( var i = 0; i < soundCount; i++ )
		{
			_ = r.ReadString();
			_ = r.ReadInt32();
		}

		// Gibs
		var gibs = r.ReadList( 1 + 8, x => new CharacterGib( x.ReadExternalReference(), x.ReadSingle(), x.ReadSingle() ) );

		// Attached lights (max 4)
		var lightCount = r.ReadCount( 1 + PointLightSize );
		if ( lightCount > MaxCharacterLights )
			throw new InvalidDataException( $"{lightCount} attached lights; maximum is {MaxCharacterLights}" );
		for ( var i = 0; i < lightCount; i++ )
		{
			_ = r.ReadString();
			SkipPointLight( r );
		}

		var stats = new CharacterStats(
			MaxHitpoints: r.ReadSingle(),
			NumberOfHealthBars: r.ReadInt32(),
			Undying: r.ReadBoolean(),
			UndieTime: r.ReadSingle(),
			UndieHitPoints: r.ReadSingle(),
			HitTolerance: r.ReadInt32(),
			KnockdownTolerance: r.ReadSingle(),
			ScoreValue: r.ReadInt32(),
			ExperienceValue: r.ReadInt32(),
			RewardOnKill: r.ReadBoolean(),
			RewardOnOverkill: r.ReadBoolean(),
			Regeneration: r.ReadInt32(),
			MaxPanic: r.ReadSingle(),
			ZapModifier: r.ReadSingle(),
			Length: r.ReadSingle(),
			Radius: r.ReadSingle(),
			Mass: r.ReadSingle(),
			Speed: r.ReadSingle(),
			TurnSpeed: r.ReadSingle(),
			BleedRate: r.ReadSingle(),
			StunTime: r.ReadSingle() );

		// Summon element sound
		_ = r.ReadInt32();
		_ = r.ReadString();

		// Resistances
		var resistanceCount = r.ReadCount( ResistanceSize );
		for ( var i = 0; i < resistanceCount; i++ )
		{
			_ = r.ReadInt32();
			_ = r.ReadSingle();
			_ = r.ReadSingle();
			_ = r.ReadBoolean();
		}

		// Visual model variants
		var models = r.ReadList( 1 + 16, x => new CharacterModelVariant( x.ReadExternalReference(), x.ReadSingle(), x.ReadVector3() ) );
		var skeleton = r.ReadExternalReference();

		// Attached effects
		var effectCount = r.ReadCount( 2 );
		for ( var i = 0; i < effectCount; i++ )
		{
			_ = r.ReadString();
			_ = r.ReadString();
		}

		string alertCue = null, attackCue = null, painCue = null, deathCue = null;

		// Animation sets (27 weapon classes)
		var animations = new Dictionary<string, CharacterAnimationClip>( StringComparer.OrdinalIgnoreCase );
		for ( var weaponClass = 0; weaponClass < WeaponClasses; weaponClass++ )
		{
			var animCount = r.ReadCount( 1 + 14 );
			for ( var i = 0; i < animCount; i++ )
			{
				var slot = r.ReadString();
				var clipName = r.ReadString();
				var speed = r.ReadSingle();
				var blendTime = r.ReadSingle();
				var loop = r.ReadBoolean();
				var soundCue = ReadAnimationActionSound( r );

				if ( !string.IsNullOrEmpty( soundCue ) )
				{
					if ( slot.Contains( "die", StringComparison.OrdinalIgnoreCase ) )
						deathCue ??= soundCue;
					else if ( slot.Contains( "hit", StringComparison.OrdinalIgnoreCase ) || slot.Contains( "pain", StringComparison.OrdinalIgnoreCase ) )
						painCue ??= soundCue;
					else if ( slot.Contains( "attack", StringComparison.OrdinalIgnoreCase ) || slot.Contains( "cast", StringComparison.OrdinalIgnoreCase ) )
						attackCue ??= soundCue;
					else if ( slot.Contains( "idle", StringComparison.OrdinalIgnoreCase ) || slot.Contains( "special", StringComparison.OrdinalIgnoreCase ) )
						alertCue ??= soundCue;
				}

				// Keep the first (default/unarmed) mapping for each logical slot.
				if ( !string.IsNullOrEmpty( slot ) && !string.IsNullOrEmpty( clipName ) )
					animations.TryAdd( slot, new CharacterAnimationClip( clipName, speed, blendTime, loop ) );
			}
		}

		// Equipment
		var equipment = r.ReadList( 4 + 1 + 12 + 1,
			x => new CharacterEquipment( x.ReadInt32(), x.ReadString(), x.ReadVector3(), x.ReadExternalReference() ) );

		// Events (extract pain/death/attack sound cues)
		ReadConditionSounds( r, ref alertCue, ref attackCue, ref painCue, ref deathCue );
		// AI weights
		var alertRadius = r.ReadSingle();
		for ( var i = 0; i < 12; i++ )
			_ = r.ReadSingle();
		_ = r.ReadBoolean(); // flocking
		_ = r.ReadSingle(); // break-free strength

		// Abilities
		var abilities = r.ReadList( 1 + 4 + 1 + 1 + 4, ReadAbility );

		// Move animations
		var moveCount = r.ReadCount( 1 + 4 );
		for ( var i = 0; i < moveCount; i++ )
		{
			_ = r.ReadByte();
			SkipStrings( r );
		}

		// Buffs
		var buffCount = r.ReadCount( 1 + 1 + 12 + 4 + 1 );
		for ( var i = 0; i < buffCount; i++ )
			SkipBuff( r );

		// Auras
		var auraCount = r.ReadCount( 1 + 1 + 1 + 12 + 1 + 4 + 4 + 1 + 4 );
		for ( var i = 0; i < auraCount; i++ )
			SkipAura( r );

		return new CharacterTemplate(
			id, displayId, factions, bloodType, stats, alertRadius,
			models, skeleton, animations, equipment, gibs, abilities,
			new CharacterSounds( alertCue, attackCue, painCue, deathCue ) );
	}

	private static object ReadItemTemplate( XnbContentReader r )
	{
		var name = r.ReadString();
		var displayName = r.ReadString();
		_ = r.ReadString(); // description

		SkipStringBankList( r ); // sounds: cue + banks

		_ = r.ReadBoolean(); // pickable
		_ = r.ReadBoolean(); // bound
		_ = r.ReadInt32();   // block value
		var weaponClass = r.ReadByte();
		var cooldown = r.ReadSingle();
		var hideModel = r.ReadBoolean();
		_ = r.ReadBoolean(); // hide effect
		_ = r.ReadBoolean(); // pause sounds

		SkipList( r, ResistanceSize, SkipResistance );

		_ = r.ReadByte();    // passive ability
		_ = r.ReadSingle();  // passive value
		SkipStrings( r ); // effects
		SkipList( r, PointLightSize, SkipPointLight );

		if ( r.ReadBoolean() )
		{
			_ = r.ReadSingle(); // special recharge
			SkipSpecialAbility( r );
		}

		var meleeRange = r.ReadSingle();
		var meleeMulti = r.ReadBoolean();
		SkipConditions( r );

		var rangedRange = r.ReadSingle();
		_ = r.ReadBoolean(); // facing
		_ = r.ReadSingle();  // homing
		_ = r.ReadSingle();  // elevation
		_ = r.ReadSingle();  // danger
		var gunRange = r.ReadSingle();
		_ = r.ReadInt32();   // clip
		_ = r.ReadInt32();   // rate
		_ = r.ReadSingle();  // accuracy
		_ = r.ReadString();  // gun sound
		_ = r.ReadString();  // muzzle effect
		_ = r.ReadString();  // shells effect
		_ = r.ReadSingle();  // tracer velocity
		_ = r.ReadString();  // non-tracer sprite
		_ = r.ReadString();  // tracer sprite
		SkipConditions( r ); // gun conditions
		var projectile = r.ReadExternalReference();
		SkipConditions( r ); // ranged conditions

		var scale = r.ReadSingle();
		var model = r.ReadExternalReference();
		SkipList( r, 1 + 1 + 1 + 12 + 1 + 4 + 4 + 1 + 4, SkipAura );

		return new ItemTemplate( name, displayName, weaponClass, cooldown, scale, model, hideModel,
			new ItemMelee( meleeRange, meleeMulti ),
			new ItemRanged( rangedRange, gunRange, projectile is not null, projectile ) );
	}

	/// <summary>Reads and discards one event collection list, for items whose cues aren't used yet.</summary>
	private static void SkipConditions( XnbContentReader r )
	{
		var count = r.ReadInt32();
		if ( count is < 0 or > MaxEventCollections )
			throw new InvalidDataException( $"{count} event collections; maximum is {MaxEventCollections}" );

		for ( var i = 0; i < count; i++ )
		{
			_ = r.ReadByte();
			_ = r.ReadInt32();
			_ = r.ReadInt32();
			_ = r.ReadSingle();
			_ = r.ReadSingle();
			_ = r.ReadBoolean();
			var events = r.ReadCount( 1 );
			for ( var e = 0; e < events; e++ )
				ReadEventSound( r );
		}
	}

	private static void SkipResistance( XnbContentReader r )
	{
		_ = r.ReadInt32();
		_ = r.ReadSingle();
		_ = r.ReadSingle();
		_ = r.ReadBoolean();
	}

	private static void SkipStringBankList( XnbContentReader r )
	{
		var count = r.ReadCount( 1 + 4 );
		for ( var i = 0; i < count; i++ )
		{
			_ = r.ReadString();
			_ = r.ReadInt32();
		}
	}

	private static void SkipList( XnbContentReader r, int minBytes, Action<XnbContentReader> skip )
	{
		var count = r.ReadCount( minBytes );
		for ( var i = 0; i < count; i++ )
			skip( r );
	}

	private static CharacterAbility ReadAbility( XnbContentReader r )
	{
		var typeName = r.ReadString();
		var cooldown = r.ReadSingle();
		var target = (CharacterAbilityTarget)r.ReadByte();
		if ( r.ReadBoolean() )
			_ = r.ReadString(); // fuzzy expression
		var animations = ReadStringArray( r );

		var minRange = 0f;
		var maxRange = 0f;
		var castType = 0;
		var element = 0;

		switch ( typeName.ToLowerInvariant() )
		{
			case "block":
				_ = r.ReadSingle();
				_ = r.ReadInt32();
				break;

			case "castspell":
				minRange = r.ReadSingle();
				maxRange = r.ReadSingle();
				_ = r.ReadSingle();
				_ = r.ReadSingle();
				_ = r.ReadSingle();
				castType = r.ReadInt32();
				element = ReadFirstElement( r );
				break;

			case "confusegrip":
			case "damagegrip":
			case "removestatus":
				break;

			case "dash":
				minRange = r.ReadSingle();
				maxRange = r.ReadSingle();
				_ = r.ReadSingle();
				_ = r.ReadVector3();
				break;

			case "elementsteal":
				maxRange = r.ReadSingle();
				_ = r.ReadSingle();
				break;

			case "gripcharacterfrombehind":
			case "jump":
				maxRange = r.ReadSingle();
				minRange = r.ReadSingle();
				_ = r.ReadSingle();
				_ = r.ReadSingle();
				break;

			case "melee":
				minRange = r.ReadSingle();
				maxRange = r.ReadSingle();
				_ = r.ReadSingle();
				SkipIntList( r );
				_ = r.ReadBoolean();
				break;

			case "pickupcharacter":
			case "zombiegrip":
				maxRange = r.ReadSingle();
				minRange = r.ReadSingle();
				_ = r.ReadSingle();
				_ = r.ReadSingle();
				_ = r.ReadString();
				break;

			case "ranged":
				minRange = r.ReadSingle();
				maxRange = r.ReadSingle();
				_ = r.ReadSingle();
				_ = r.ReadSingle();
				_ = r.ReadSingle();
				SkipIntList( r );
				break;

			case "specialabilityability":
				maxRange = r.ReadSingle();
				minRange = r.ReadSingle();
				_ = r.ReadSingle();
				_ = r.ReadInt32();
				break;

			case "throwgrip":
				maxRange = r.ReadSingle();
				minRange = r.ReadSingle();
				_ = r.ReadSingle();
				var dmgCount = r.ReadCount( DamageSize );
				for ( var i = 0; i < dmgCount; i++ )
					SkipIntDamage( r );
				break;

			default:
				throw new InvalidDataException( $"unknown ability '{typeName}'" );
		}

		return new CharacterAbility( typeName, cooldown, target, minRange, maxRange, castType, element, animations );
	}

	private static string ReadAnimationActionSound( XnbContentReader r )
	{
		string firstSound = null;
		var count = r.ReadCount( 1 + 8 );
		for ( var i = 0; i < count; i++ )
		{
			var typeName = r.ReadString();
			_ = r.ReadSingle(); // start
			_ = r.ReadSingle(); // end

			switch ( typeName.ToLowerInvariant() )
			{
				case "block":
					_ = r.ReadInt32();
					break;
				case "breakfree":
					_ = r.ReadSingle();
					_ = r.ReadInt32();
					break;
				case "camerashake":
					_ = r.ReadString();
					_ = r.ReadSingle();
					_ = r.ReadSingle();
					break;
				case "castspell":
					if ( !r.ReadBoolean() )
						_ = r.ReadString();
					break;
				case "crouch":
					_ = r.ReadSingle();
					_ = r.ReadSingle();
					break;
				case "damagegrip":
					_ = r.ReadBoolean();
					var dmgCount = r.ReadCount( DamageSize );
					for ( var d = 0; d < dmgCount; d++ )
						SkipDamage( r );
					break;
				case "dealdamage":
					_ = r.ReadInt32();
					_ = r.ReadByte();
					break;
				case "detachitem":
					_ = r.ReadInt32();
					_ = r.ReadVector3();
					break;
				case "ethereal":
					_ = r.ReadBoolean();
					_ = r.ReadSingle();
					_ = r.ReadSingle();
					break;
				case "footstep":
				case "overkillgrip":
				case "releasegrip":
				case "throwgrip":
					break;
				case "grip":
					_ = r.ReadByte();
					_ = r.ReadSingle();
					_ = r.ReadSingle();
					_ = r.ReadString();
					_ = r.ReadString();
					_ = r.ReadBoolean();
					break;
				case "gunfire":
					_ = r.ReadInt32();
					_ = r.ReadSingle();
					break;
				case "immortal":
				case "invisible":
				case "suicide":
					_ = r.ReadBoolean();
					break;
				case "jump":
					_ = r.ReadSingle();
					if ( r.ReadBoolean() ) _ = r.ReadSingle();
					if ( r.ReadBoolean() ) _ = r.ReadSingle();
					break;
				case "move":
					_ = r.ReadVector3();
					break;
				case "playeffect":
					_ = r.ReadString();
					_ = r.ReadBoolean();
					_ = r.ReadString();
					_ = r.ReadSingle();
					break;
				case "playsound":
					var cue = r.ReadString();
					_ = r.ReadInt32();
					if ( !string.IsNullOrEmpty( cue ) )
						firstSound ??= cue;
					break;
				case "removestatus":
					_ = r.ReadString();
					break;
				case "setitemattach":
					_ = r.ReadInt32();
					_ = r.ReadString();
					break;
				case "spawnmissile":
					_ = r.ReadInt32();
					_ = r.ReadVector3();
					_ = r.ReadBoolean();
					break;
				case "specialability":
					var weapon = r.ReadInt32();
					if ( weapon < 0 )
						SkipSpecialAbility( r );
					break;
				case "tongue":
					_ = r.ReadSingle();
					break;
				case "weaponvisibility":
					_ = r.ReadInt32();
					_ = r.ReadBoolean();
					break;
				default:
					throw new InvalidDataException( $"unknown animation action '{typeName}'" );
			}
		}

		return firstSound;
	}

	private static void ReadConditionSounds( XnbContentReader r, ref string alertCue, ref string attackCue, ref string painCue, ref string deathCue )
	{
		var count = r.ReadInt32();
		if ( count is < 0 or > MaxEventCollections )
			throw new InvalidDataException( $"{count} event collections; maximum is {MaxEventCollections}" );

		for ( var i = 0; i < count; i++ )
		{
			var condition = r.ReadByte();
			_ = r.ReadInt32();
			_ = r.ReadInt32();
			_ = r.ReadSingle();
			_ = r.ReadSingle();
			_ = r.ReadBoolean();
			var events = r.ReadCount( 1 );
			for ( var e = 0; e < events; e++ )
			{
				var cue = ReadEventSound( r );
				if ( string.IsNullOrEmpty( cue ) )
					continue;

				// EventConditionType flags: Hit=0x2, Damaged=0x8, Death=0x20, OverKill=0x40
				if ( (condition & 0x60) != 0 )
					deathCue ??= cue;
				else if ( (condition & 0x08) != 0 )
					painCue ??= cue;
				else if ( (condition & 0x02) != 0 )
					attackCue ??= cue;
				else
					alertCue ??= cue;
			}
		}
	}

	private static string ReadEventSound( XnbContentReader r )
	{
		var type = r.ReadByte();
		switch ( type )
		{
			case 0:
			case 16:
				SkipDamage( r );
				_ = r.ReadBoolean();
				break;
			case 1:
				SkipIntDamage( r );
				_ = r.ReadSingle();
				break;
			case 2:
				_ = r.ReadInt32();
				var cue = r.ReadString();
				_ = r.ReadSingle();
				_ = r.ReadBoolean();
				return cue;
			case 3:
				_ = r.ReadBoolean();
				_ = r.ReadBoolean();
				_ = r.ReadString();
				break;
			case 4:
				_ = r.ReadInt32();
				break;
			case 5:
				_ = r.ReadSingle();
				_ = r.ReadSingle();
				_ = r.ReadBoolean();
				break;
			case 6:
				_ = r.ReadInt32();
				_ = r.ReadInt32();
				_ = r.ReadInt32();
				break;
			case 8:
				_ = r.ReadString();
				_ = r.ReadString();
				_ = r.ReadString();
				_ = r.ReadSingle();
				_ = r.ReadByte();
				_ = r.ReadByte();
				_ = r.ReadByte();
				_ = r.ReadSingle();
				_ = r.ReadVector3();
				break;
			case 9:
				break;
			case 10:
				_ = r.ReadInt32();
				_ = r.ReadInt32();
				break;
			case 11:
			case 12:
				_ = r.ReadString();
				break;
			case 13:
				_ = r.ReadString();
				_ = r.ReadVector3();
				_ = r.ReadBoolean();
				break;
			case 14:
				SkipPointLight( r );
				break;
			case 15:
				_ = r.ReadString();
				SkipElements( r );
				break;
			default:
				throw new InvalidDataException( $"unsupported event type {type}" );
		}

		return null;
	}

	private static void SkipBuff( XnbContentReader r )
	{
		var type = r.ReadByte();
		_ = r.ReadByte();
		_ = r.ReadVector3();
		_ = r.ReadSingle();
		_ = r.ReadString();

		switch ( type )
		{
			case 0:
			case 1:
				SkipDamage( r );
				break;
			case 2:
				_ = r.ReadInt32();
				_ = r.ReadSingle();
				_ = r.ReadSingle();
				_ = r.ReadBoolean();
				break;
			case 3:
				break;
			case 4:
			case 5:
				_ = r.ReadSingle();
				break;
			case 6:
			case 7:
			case 8:
				_ = r.ReadSingle();
				_ = r.ReadSingle();
				break;
			default:
				throw new InvalidDataException( $"unknown buff type {type}" );
		}
	}

	private static void SkipAura( XnbContentReader r )
	{
		_ = r.ReadByte();
		var type = r.ReadByte();
		_ = r.ReadByte();
		_ = r.ReadVector3();
		_ = r.ReadString();
		_ = r.ReadSingle();
		_ = r.ReadSingle();
		_ = r.ReadString();
		_ = r.ReadInt32();

		switch ( type )
		{
			case 0:
				SkipBuff( r );
				break;
			case 1:
			case 2:
			case 3:
				_ = r.ReadSingle();
				break;
			case 4:
				_ = r.ReadSingle();
				_ = r.ReadSingle();
				break;
			default:
				throw new InvalidDataException( $"unknown aura type {type}" );
		}
	}

	private static void SkipPointLight( XnbContentReader r )
	{
		_ = r.ReadSingle();
		_ = r.ReadVector3();
		_ = r.ReadVector3();
		_ = r.ReadSingle();
		_ = r.ReadByte();
		_ = r.ReadSingle();
		_ = r.ReadSingle();
	}

	private static void SkipSpecialAbility( XnbContentReader r )
	{
		_ = r.ReadString();
		_ = r.ReadString();
		_ = r.ReadString();
		SkipElements( r );
	}

	private static void SkipDamage( XnbContentReader r )
	{
		_ = r.ReadInt32();
		_ = r.ReadInt32();
		_ = r.ReadSingle();
		_ = r.ReadSingle();
	}

	private static void SkipIntDamage( XnbContentReader r )
	{
		_ = r.ReadInt32();
		_ = r.ReadInt32();
		_ = r.ReadInt32();
		_ = r.ReadSingle();
	}

	private static int ReadFirstElement( XnbContentReader r )
	{
		var count = r.ReadCount( 4 );
		var first = 0;
		for ( var i = 0; i < count; i++ )
		{
			var value = r.ReadInt32();
			if ( i == 0 )
				first = value;
		}

		return first;
	}

	private static void SkipElements( XnbContentReader r )
	{
		var count = r.ReadCount( 4 );
		for ( var i = 0; i < count; i++ )
			_ = r.ReadInt32();
	}

	private static void SkipIntList( XnbContentReader r )
	{
		var count = r.ReadCount( 4 );
		for ( var i = 0; i < count; i++ )
			_ = r.ReadInt32();
	}

	private static string[] ReadStringArray( XnbContentReader r ) => r.ReadList( 1, x => x.ReadString() );

	private static void SkipStrings( XnbContentReader r )
	{
		var count = r.ReadCount( 1 );
		for ( var i = 0; i < count; i++ )
			_ = r.ReadString();
	}
}
