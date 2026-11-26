using System.Text.Json.Serialization;

namespace Source1;

// Stored on imported models through ModelBuilder.WithData; game code reads them into its own types with the same GameData names.

[Sandbox.ModelEditor.GameData( "source_entity_hull" ), Hide]
public sealed class EntityHull
{
	public BBox Bounds { get; set; }
}

[Sandbox.ModelEditor.GameData( "source_air_drag" ), Hide]
public sealed class AirDrag
{
	public Vector3 AreaFractions { get; set; }
	public Vector3 UnitMassInertia { get; set; }

	private const float MetresPerInch = 0.0254f;

	private static Vector3 SizeInMetres( Model model )
	{
		if ( model.Physics is not { Parts.Count: 1 } physics || physics.Parts[0].Hulls is not { Count: > 0 } hulls )
			return default;
		var bounds = hulls[0].Bounds;
		for ( var i = 1; i < hulls.Count; i++ ) bounds = bounds.AddBBox( hulls[i].Bounds );
		return bounds.Size * MetresPerInch;
	}

	// The stored fractions use IVP axis order (X, Z, Y).
	private Vector3 Fractions => new( AreaFractions.x, AreaFractions.z, AreaFractions.y );

	private Vector3 LinearBasis( Vector3 size ) =>
		new Vector3( size.y * size.z, size.x * size.z, size.x * size.y ) * Fractions * MetresPerInch;

	internal (Vector3 Linear, Vector3 Angular, Vector3 Inertia) Basis( Model model, Vector3 scale )
	{
		var size = SizeInMetres( model ) * scale.Abs();
		var half = size * 0.5f;
		var inertia = UnitMassInertia;
		if ( scale != Vector3.One )
		{
			// Each principal moment is the sum of the second moments on the other two axes.
			var second = (new Vector3( (inertia.x + inertia.y + inertia.z) * 0.5f ) - inertia) * scale * scale;
			inertia = new( second.y + second.z, second.x + second.z, second.x + second.y );
		}

		var fractions = Fractions;
		var angular = new Vector3(
			AngularFactor( half.x, half.y, half.z, fractions.y, fractions.z, inertia.x ),
			AngularFactor( half.y, half.z, half.x, fractions.z, fractions.x, inertia.y ),
			AngularFactor( half.z, half.x, half.y, fractions.x, fractions.y, inertia.z ) );
		return (LinearBasis( size ), angular, inertia / (MetresPerInch * MetresPerInch));
	}

	/// <summary>Applies the authored inertia and returns the drag factors for this rigid body.</summary>
	internal static (Vector3 Linear, Vector3 Angular) ConfigureBody( Model model, Rigidbody body )
	{
		var physics = body.PhysicsBody;
		if ( !physics.IsValid() || model?.GetData<AirDrag>() is not { } drag ) return default;
		var mass = physics.Mass;
		if ( mass <= 0 ) return default;
		var basis = drag.Basis( model, Vector3.One );
		var inertia = basis.Inertia * mass;
		if ( inertia.x > 0 && inertia.y > 0 && inertia.z > 0 )
			physics.SetInertiaTensor( inertia, Rotation.Identity );
		return (basis.Linear / mass, basis.Angular / mass);
	}

	// Integrate the drag torque over both faces parallel to this spin axis.
	private static float AngularFactor( float length, float width, float height, float widthFraction, float heightFraction, float inertia )
	{
		if ( inertia <= 0 ) return 0;
		var w2 = width * width;
		var h2 = height * height;
		var l2 = length * length / 3;
		return 2 * length / inertia * (heightFraction * w2 * (l2 + w2 / 2 + h2)
			+ widthFraction * h2 * (l2 + h2 / 2 + w2));
	}
}

[Sandbox.ModelEditor.GameData( "source_prop_damage" ), Hide]
public sealed class PropDamageModifiers
{
	public float Bullet { get; set; } = 1;
	public float Club { get; set; } = 1;
	public float Explosive { get; set; } = 1;
	public bool FireResistant { get; set; }
}

[Sandbox.ModelEditor.GameData( "physgun_interactions" ), Hide]
public sealed class PhysGunInteractions
{
	/// <summary>Authored angular speed about the prop's local Z axis in degrees per second.</summary>
	public float? LaunchSpin { get; set; }
	public Angles? PreferredCarryAngles { get; set; }
}

/// <summary>Weapon-script data associated with imported first- and third-person models.</summary>
[Sandbox.ModelEditor.GameData( "weapon_data", AllowMultiple = true ), Hide]
public sealed class SourceModelWeaponData
{
	public string ClassName { get; set; }
	public string ViewModel { get; set; }
	public string WorldModel { get; set; }
	public string PrimaryAmmo { get; set; }
	public string SecondaryAmmo { get; set; }
	/// <summary>Mounted ammo resources, when the source config provides a reserve capacity.</summary>
	public string PrimaryAmmoResource { get; set; }
	public string SecondaryAmmoResource { get; set; }
	public string PrintName { get; set; }
	/// <summary>Mounted HUD icon texture, white on transparent, when the script's TextureData weapon entry resolves.</summary>
	public string Icon { get; set; }
	public int ClipSize { get; set; } = -1;
	public int? DefaultClip { get; set; }
	public int? SecondaryDefaultClip { get; set; }
	public int SecondaryClipSize { get; set; } = -1;
	public int Bucket { get; set; }
	/// <summary>Position within the source inventory bucket, when authored.</summary>
	public int? BucketPosition { get; set; }
	/// <summary>Source weapon selection priority, when authored.</summary>
	public int? Weight { get; set; }
	/// <summary>Damage explicitly authored in the weapon script.</summary>
	public float? ScriptDamage { get; set; }
	/// <summary>Damage from the weapon script, or the mounted game's numeric skill settings, if supplied.</summary>
	public float? PlayerDamage { get; set; }
	public float? NpcDamage { get; set; }
	public float? SecondaryPlayerDamage { get; set; }
	public float? SecondaryNpcDamage { get; set; }
	public float? BlastRadius { get; set; }
	public float? SecondaryBlastRadius { get; set; }
	/// <summary>Seconds between primary shots, when authored.</summary>
	public float? CycleTime { get; set; }
	/// <summary>Pellets per primary shot, when authored.</summary>
	public int? Bullets { get; set; }
	public bool? FullAuto { get; set; }
	public float? Range { get; set; }
	/// <summary>Damage multiplier per 500 units travelled, when authored.</summary>
	public float? RangeModifier { get; set; }
	/// <summary>Source weapon family, such as Pistol, Rifle, SubMachinegun, Shotgun, SniperRifle, Machinegun, Knife or Grenade.</summary>
	public string WeaponType { get; set; }
	/// <summary>Team that may use the weapon, such as TERRORIST, CT or ANY.</summary>
	public string Team { get; set; }
	/// <summary>Suffix naming the third-person animation set, such as ak or pistol.</summary>
	public string PlayerAnimationExtension { get; set; }
	/// <summary>False when the viewmodel is authored left-handed and is mirrored for right-handed use.</summary>
	public bool? BuiltRightHanded { get; set; }
	public string SilencerModel { get; set; }
	/// <summary>Counter-Strike muzzle flash style: CS_MUZZLEFLASH_NORM, CS_MUZZLEFLASH_X or CS_MUZZLEFLASH_NONE.</summary>
	public string MuzzleFlashStyle { get; set; }
	public float? MuzzleFlashScale { get; set; }
	/// <summary>Sound role (single_shot, reload, empty, etc.) to mounted SoundEvent resource path.</summary>
	public Dictionary<string, string> Sounds { get; set; } = new();
}

/// <summary>
/// Authored sequence metadata retained by model importers alongside animation clips.
/// A model can have thousands of these and the stored form is parsed whenever it is read,
/// so members at their default are left out of it. An importer may leave an empty member null;
/// read back, it is empty.
/// </summary>
[Sandbox.ModelEditor.GameData( "sequence_data", AllowMultiple = true ), Hide]
public sealed class SourceModelSequenceData
{
	public string Name { get; set; }
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public string Activity { get; set; } = "";
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public int ActivityWeight { get; set; }
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public bool Looping { get; set; }
	/// <summary>The source sequence takes its phase from game time.</summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public bool RealTime { get; set; }
	/// <summary>Enter this sequence immediately, without blending from the previous pose.</summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public bool Snap { get; set; }
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public float FadeIn { get; set; }
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public float FadeOut { get; set; }
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public Event[] Events { get; set; } = [];
	/// <summary>The clips are additive: differences to layer onto another pose rather than poses themselves.</summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public bool Delta { get; set; }
	/// <summary>
	/// The bone mask of the model's animation skeleton that carries this sequence's per-bone weights,
	/// or null when the sequence poses the whole body. A masked sequence only animates part of the skeleton:
	/// its clips hold the authored pose for every bone the mask weights and the reference pose elsewhere,
	/// and are meant to be layered over another pose through the mask.
	/// </summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public string BoneMask { get; set; }

	/// <summary>Blend of a masked gesture whose absolute pose comes from one timed layer.</summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public LayerRamp? PoseBlend { get; set; }

	public readonly record struct LayerRamp( float Start, float Peak, float Tail, float End, bool Spline )
	{
		internal bool IsTimed => Start != End && float.IsFinite( Start ) && float.IsFinite( Peak ) && float.IsFinite( Tail ) && float.IsFinite( End );

		internal float Weight( float cycle )
		{
			if ( cycle < Start || cycle >= End ) return 0;
			var weight = cycle < Peak ? (cycle - Start) / (Peak - Start) : cycle > Tail ? (End - cycle) / (End - Tail) : 1;
			return Spline ? weight * weight * (3 - 2 * weight) : weight;
		}
	}
	/// <summary>
	/// Additive sequences to play beside this one for its complete motion. For a whole-body sequence these are the
	/// delta sequences authored as its layers, already baked into its clip with their parameters at zero.
	/// For a sequence with a <see cref="BoneMask"/> they are what its clip cannot carry through the mask:
	/// the blend grids among its delta layers, for the consumer to drive, and one clip holding what the
	/// other delta layers do to the bones the mask leaves out.
	/// </summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public string[] AdditiveLayers { get; set; } = [];
	/// <summary>
	/// Bones the sequence holds in place with IK, such as hands on a weapon that is animated on a bone of its own.
	/// The hold is solved on the final pose: a clip played by itself already has it, but a consumer that layers
	/// the sequence over another pose, or more on top of it, has to pull each bone onto its target again.
	/// </summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public IkTarget[] IkTargets { get; set; } = [];
	/// <summary>
	/// How far one cycle of the sequence is authored to carry the model along the ground, in model space.
	/// The clips play in place; zero for a sequence that does not travel.
	/// </summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public Vector3 Movement { get; set; }
	/// <summary>The ground speed the sequence is authored for at its normal rate, in units per second.</summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public float GroundSpeed { get; set; }
	/// <summary>
	/// Pose parameters that blend between the clips of <see cref="BlendGrid"/>: none, one, or two.
	/// The clip named after the sequence is the grid evaluated with every parameter at zero.
	/// </summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public BlendAxis[] BlendAxes { get; set; } = [];
	/// <summary>
	/// One clip per grid cell, first axis fastest: the cell at (x, y) is index x + y * BlendAxes[0].Positions.Length.
	/// Cells may differ in length and are blended by cycle.
	/// </summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public string[] BlendGrid { get; set; } = [];
	/// <summary><see cref="Movement"/> of each <see cref="BlendGrid"/> cell.</summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public Vector3[] BlendGridMovement { get; set; } = [];
	/// <summary><see cref="GroundSpeed"/> of each <see cref="BlendGrid"/> cell.</summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public float[] BlendGridSpeeds { get; set; } = [];
	/// <summary>
	/// The single parameter of a one-axis grid of single-frame, non-additive poses.
	/// Positions use the source parameter's authored units.
	/// </summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public string BlendParameter { get; set; }
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public float[] BlendPositions { get; set; } = [];
	/// <summary>Absolute pose clips corresponding to BlendPositions; the ordinary clip remains the neutral fallback.</summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public string[] BlendClips { get; set; } = [];

	/// <summary>
	/// The end bone of a two-joint IK chain (its two parents are the joints) and the bone it belongs on.
	/// The target bone exists for this purpose and is animated by the sequence's clips.
	/// </summary>
	public sealed class IkTarget
	{
		public string Bone { get; set; }
		public string TargetBone { get; set; }
	}

	/// <summary>One axis of a blend grid, in the pose parameter's authored units.</summary>
	public sealed class BlendAxis
	{
		public string Parameter { get; set; }
		/// <summary>The parameter's range. A value outside it is clamped, or wrapped when <see cref="Loop"/> is set.</summary>
		public float Min { get; set; }
		public float Max { get; set; }
		/// <summary>The period of a wrapping parameter such as a movement direction in degrees, otherwise zero.</summary>
		public float Loop { get; set; }
		/// <summary>The parameter value at each cell along this axis, in either order.</summary>
		public float[] Positions { get; set; } = [];
	}

	/// <summary>
	/// A raw source animation event at a normalized cycle; consumers decide which events they support.
	/// Events authored by name carry it in <see cref="Name"/> with a zero <see cref="Id"/>;
	/// events authored by number have an empty name.
	/// </summary>
	public sealed class Event
	{
		public float Cycle { get; set; }
		public int Id { get; set; }
		/// <summary>The stored event type flags; bit 10 marks an event authored by name.</summary>
		public int Type { get; set; }
		public string Name { get; set; }
		/// <summary>The authored option string, at most 63 characters.</summary>
		public string Options { get; set; }
	}
}

/// <summary>Where an animated model's skeleton and clips are mounted, as resources of their own.</summary>
[Sandbox.ModelEditor.GameData( "animation_data" ), Hide]
public sealed class SourceModelAnimationData
{
	/// <summary>The mounted skeleton the clips animate.</summary>
	public string Skeleton { get; set; }
	/// <summary>The mounted folder holding a .anim for each of <see cref="Clips"/>.</summary>
	public string ClipFolder { get; set; }
	/// <summary>Names of the clips the model offers.</summary>
	public string[] Clips { get; set; } = [];
}

/// <summary>A model's authored, static additive pose layer, driven by a named parameter.</summary>
[Sandbox.ModelEditor.GameData( "pose_layer_data", AllowMultiple = true ), Hide]
public sealed class SourceModelPoseLayerData
{
	public string Name { get; set; }
	public string Parameter { get; set; }
	/// <summary>Parameter values corresponding to the clips, in their authored units.</summary>
	public float[] Positions { get; set; } = [];
	/// <summary>Additive clips relative to the neutral pose already baked into the base animations.</summary>
	public string[] Clips { get; set; } = [];
}

/// <summary>A pose parameter a model's authored blend grids and pose layers are driven by.</summary>
[Sandbox.ModelEditor.GameData( "pose_parameter_data", AllowMultiple = true ), Hide]
internal sealed class SourceModelPoseParameterData
{
	public string Name { get; set; }
	/// <summary>The parameter's range in authored units. A value outside it is clamped, or wrapped when <see cref="Loop"/> is set.</summary>
	public float Min { get; set; }
	public float Max { get; set; }
	/// <summary>The period of a wrapping parameter such as a movement direction in degrees, otherwise zero.</summary>
	public float Loop { get; set; }
}

/// <summary>
/// A bounded sample of the surface rigidly attached to a bone, retained by model importers
/// for mesh correspondence and retargeting. Positions and normals use the model's reference pose.
/// </summary>
[Sandbox.ModelEditor.GameData( "bone_surface", AllowMultiple = true ), Hide]
public sealed class SourceModelBoneSurfaceData
{
	public string Bone { get; set; }
	public Sample[] Samples { get; set; } = [];

	public readonly record struct Sample( Vector3 Position, Vector3 Normal, Vector2 TexCoord );
}
