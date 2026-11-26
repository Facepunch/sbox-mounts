// Ported from OpenMagicka (MIT, https://github.com/vogster/OpenMagicka),
// src/OpenMagicka.Content/PolygonHead and src/OpenMagicka.Content/Magicka.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Magicka;

/// <summary>The main material of static geometry. Layer1 is blended over Layer0 by vertex colour.</summary>
public sealed record RenderDeferredEffect(
	float Alpha, float Sharpness, bool VertexColorEnabled, bool UseMaterialTextureForReflectiveness,
	ExternalReference ReflectionMap, RenderDeferredLayer Layer0, RenderDeferredLayer Layer1 );

public sealed record RenderDeferredLayer(
	bool DiffuseTextureAlphaDisabled, bool AlphaMaskEnabled, Vector3 DiffuseColor,
	float SpecAmount, float SpecPower, float EmissiveAmount, float NormalPower, float Reflectiveness,
	ExternalReference DiffuseTexture, ExternalReference MaterialTexture, ExternalReference NormalTexture );

/// <summary>Water (and its frozen state) used by level liquids.</summary>
public sealed record RenderDeferredLiquidEffect(
	ExternalReference ReflectionMap, float WaveHeight, Vector2 WaveSpeed0, Vector2 WaveSpeed1,
	float WaterReflectiveness, Vector3 BottomColor, Vector3 DeepBottomColor,
	float WaterEmissiveAmount, float WaterSpecAmount, float WaterSpecPower,
	ExternalReference BottomTexture, ExternalReference WaterNormalMap,
	float IceReflectiveness, Vector3 IceColor, float IceEmissiveAmount, float IceSpecAmount,
	float IceSpecPower, ExternalReference IceDiffuseMap, ExternalReference IceNormalMap );

public sealed record LavaEffect(
	float MaskDistortion, Vector2 Speed0, Vector2 Speed1, float LavaHotEmissiveAmount,
	float LavaColdEmissiveAmount, float LavaSpecAmount, float LavaSpecPower, float TempFrequency,
	ExternalReference ToneMap, ExternalReference TempMap, ExternalReference MaskMap,
	Vector3 RockColor, float RockEmissiveAmount, float RockSpecAmount, float RockSpecPower,
	float RockNormalPower, ExternalReference RockTexture, ExternalReference RockNormalMap );

/// <summary>Drawn in the additive pass; the colour tint's alpha is always 1.</summary>
public sealed record AdditiveEffect( Vector3 ColorTint, bool VertexColorEnabled, bool TextureEnabled, ExternalReference Texture );

/// <summary>Normal-mapped skinned material of the Cthulhu boss models.</summary>
public sealed record SkinnedModelDeferredNormalMappedEffect(
	Vector3 DiffuseColor, float SpecularAmount, float SpecularPower, float EmissiveAmount, float NormalPower,
	ExternalReference DiffuseMap, ExternalReference MaterialMap, ExternalReference DamageMap,
	ExternalReference NormalMap, ExternalReference NormalDamageMap );

/// <summary>Static level geometry: one root per material, each with an AABB tree over its index range.</summary>
public sealed record BiTreeModel( IReadOnlyList<BiTreeRoot> Roots );

/// <summary>
/// One batch of static geometry. The stride is stored here, not in the declaration. The effect is
/// inline. Drawn as a triangle list with base vertex 0.
/// </summary>
public sealed record BiTreeRoot(
	bool Visible, bool CastShadows, float Sway, float EntityInfluence, float GroundLevel,
	int NumVertices, int VertexStride, VertexDeclaration Declaration, VertexBuffer VertexBuffer,
	IndexBuffer IndexBuffer, object Effect, BiTreeNode Tree );

/// <summary>A node's index range covers the ranges of its children.</summary>
public sealed record BiTreeNode( int PrimitiveCount, int StartIndex, Box Bounds, BiTreeNode ChildA, BiTreeNode ChildB );

/// <summary>The geometry and placement data of one level scene (Levels/**/*.xnb).</summary>
public sealed record LevelModel(
	BiTreeModel Geometry,
	IReadOnlyList<AnimatedLevelPart> AnimatedParts,
	IReadOnlyList<LevelLight> Lights,
	IReadOnlyList<VisualEffectPlacement> Effects,
	IReadOnlyList<PhysicsEntityPlacement> PhysicsEntities,
	IReadOnlyList<Liquid> Liquids,
	IReadOnlyList<ForceField> ForceFields,
	CollisionMesh[] CollisionMeshes,
	CollisionMesh CameraMesh,
	IReadOnlyList<TriggerArea> TriggerAreas,
	IReadOnlyList<Locator> Locators,
	NavMesh NavMesh );

/// <summary>Surface material; also the index of a level's static collision mesh.</summary>
public enum CollisionMaterial : byte
{
	Generic,
	Gravel,
	Grass,
	Wood,
	Snow,
	Stone,
	Mud,
	Reflect,
	Water,
	Lava,
}

public enum LevelLightType
{
	Point = 0,
	Directional = 1,
	Spot = 2,
}

public enum LightVariationType
{
	None,
	Sine,
	Flicker,
	Candle,
	Strobe,
}

[Flags]
public enum MovementProperties : byte
{
	Default = 0,
	Water = 1,
	Jump = 2,
	Fly = 4,
	Dynamic = 0x80,
}

/// <summary>Range, attenuation, cutoff and sharpness are stored for every light type.</summary>
public sealed record LevelLight(
	string Name, Vector3 Position, Vector3 Direction, LevelLightType Type, LightVariationType Variation,
	float Range, bool UseAttenuation, float CutoffAngle, float Sharpness, Vector3 DiffuseColor,
	Vector3 AmbientColor, float SpecularAmount, float VariationSpeed, float VariationAmount,
	int ShadowMapSize, bool CastShadows );

/// <summary>A particle effect from Content/Effects placed at a position facing Forward.</summary>
public sealed record VisualEffectPlacement( string Id, Vector3 Position, Vector3 Forward, float Range, string Effect );

/// <summary>A physics object; Template is loaded from Data/PhysicsEntities.</summary>
public sealed record PhysicsEntityPlacement( Matrix4x4 Transform, string Template );

/// <summary>Water or lava: Effect is a <see cref="RenderDeferredLiquidEffect"/> or a <see cref="LavaEffect"/>.</summary>
public sealed record Liquid(
	object Effect, VertexBuffer VertexBuffer, IndexBuffer IndexBuffer, VertexDeclaration Declaration,
	int VertexStride, int NumVertices, int PrimitiveCount, bool HasCollision, bool Freezable, bool AutoFreeze );

/// <summary>A translucent force field (5 in the levels of the Steam release).</summary>
public sealed record ForceField(
	Vector3 Color, float Width, float AlphaPower, float AlphaFalloffPower, float MaxRadius,
	float RippleDistortion, float MapDistortion, bool VertexColorEnabled, ExternalReference DisplacementMap,
	float Ttl, VertexBuffer VertexBuffer, IndexBuffer IndexBuffer, VertexDeclaration Declaration,
	int VertexStride, int NumVertices, int PrimitiveCount );

public readonly record struct CollisionTriangle( int I0, int I1, int I2 );

public sealed record CollisionMesh( IReadOnlyList<Vector3> Vertices, IReadOnlyList<CollisionTriangle> Triangles );

/// <summary>An oriented box; the stored position is a corner of the box, not its centre.</summary>
public sealed record TriggerArea( string Name, Vector3 Corner, Vector3 SideLengths, Quaternion Orientation );

public sealed record Locator( string Name, Matrix4x4 Transform, float Radius );

public sealed record NavTriangle(
	ushort A, ushort B, ushort C, ushort? NeighbourA, ushort? NeighbourB, ushort? NeighbourC,
	float CostAB, float CostBC, float CostCA, MovementProperties Movement );

public sealed record NavMesh( IReadOnlyList<Vector3> Vertices, IReadOnlyList<NavTriangle> Triangles );

/// <summary>A pair of flags per mesh of an animated part (visible, casts shadows).</summary>
public sealed record MeshSetting( string MeshName, bool Visible, bool CastShadows );

/// <summary>A level light that follows an animated part.</summary>
public sealed record PartLightAttachment( string LightName, Matrix4x4 LocalTransform );

public sealed record PartCollision( CollisionMaterial Material, CollisionMesh Mesh );

/// <summary>A moving piece of a level (door, platform, bridge) with its own model and animation.</summary>
public sealed record AnimatedLevelPart(
	string Name, bool AffectShields, ModelContent Model, IReadOnlyList<MeshSetting> MeshSettings,
	IReadOnlyList<Liquid> Liquids, IReadOnlyList<Locator> Locators, float AnimationDuration,
	IReadOnlyList<AnimationKeyframe> Animation, IReadOnlyList<VisualEffectPlacement> Effects,
	IReadOnlyList<PartLightAttachment> Lights, PartCollision Collision, NavMesh NavMesh,
	IReadOnlyList<AnimatedLevelPart> Children );

/// <summary>Character factions (flags) from Magicka's Data/Characters/*.xnb templates.</summary>
[Flags]
internal enum MagickaFactions
{
	None = 0,
	Evil = 0x1,
	Wild = 0x2,
	Friendly = 0x4,
	Demon = 0x8,
	Undead = 0x10,
	Human = 0x20,
	Wizard = 0x40,
	Neutral = 0xFF,
	Player0 = 0x100,
	Player1 = 0x200,
	Player2 = 0x400,
	Player3 = 0x800,
	TeamRed = 0x1000,
	TeamBlue = 0x2000,
	Players = 0x3F00,
}

internal sealed record CharacterModelVariant( ExternalReference Model, float Scale, Vector3 Tint );

internal sealed record CharacterGib( ExternalReference Model, float Mass, float Scale );

internal sealed record CharacterEquipment( int Slot, string Bone, Vector3 Rotation, ExternalReference Item );

internal sealed record CharacterAnimationClip( string ClipName, float Speed, float BlendTime, bool Loop );

internal enum CharacterAbilityTarget : byte
{
	Self = 1,
	Enemy = 2,
	Friendly = 3,
}

internal sealed record CharacterAbility(
	string TypeName,
	float Cooldown,
	CharacterAbilityTarget Target,
	float MinRange,
	float MaxRange,
	int CastType,
	int Element,
	IReadOnlyList<string> Animations );

internal sealed record CharacterStats(
	float MaxHitpoints,
	int NumberOfHealthBars,
	bool Undying,
	float UndieTime,
	float UndieHitPoints,
	int HitTolerance,
	float KnockdownTolerance,
	int ScoreValue,
	int ExperienceValue,
	bool RewardOnKill,
	bool RewardOnOverkill,
	int Regeneration,
	float MaxPanic,
	float ZapModifier,
	float Length,
	float Radius,
	float Mass,
	float Speed,
	float TurnSpeed,
	float BleedRate,
	float StunTime );

internal sealed record CharacterSounds( string AlertCue, string AttackCue, string PainCue, string DeathCue );

/// <summary>A weapon, staff or other item from Data/Items/**.</summary>
internal sealed record ItemTemplate(
	string Name,
	string DisplayName,
	int WeaponClass,
	float CooldownTime,
	float Scale,
	ExternalReference Model,
	bool HideModel,
	ItemMelee Melee,
	ItemRanged Ranged );

internal sealed record ItemMelee( float Range, bool MultiHit );

internal sealed record ItemRanged(
	float Range,
	float GunRange,
	bool HasProjectile,
	ExternalReference ProjectileModel );

/// <summary>A creature or wizard template from Data/Characters/*.xnb.</summary>
internal sealed record CharacterTemplate(
	string Id,
	string DisplayId,
	MagickaFactions Factions,
	int BloodType,
	CharacterStats Stats,
	float AlertRadius,
	IReadOnlyList<CharacterModelVariant> Models,
	ExternalReference Skeleton,
	IReadOnlyDictionary<string, CharacterAnimationClip> Animations,
	IReadOnlyList<CharacterEquipment> Equipment,
	IReadOnlyList<CharacterGib> Gibs,
	IReadOnlyList<CharacterAbility> Abilities,
	CharacterSounds Sounds );
