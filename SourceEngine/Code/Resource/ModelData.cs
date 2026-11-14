using System.Text.Json.Serialization;

namespace Source1;

// Stored on imported models through ModelBuilder.WithData; game code reads them into its own types with the same GameData names.

/// <summary>Weapon-script data associated with imported first- and third-person models.</summary>
[Sandbox.ModelEditor.GameData( "weapon_data", AllowMultiple = true ), Hide]
internal sealed class ModelWeaponData
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
	public int SecondaryClipSize { get; set; } = -1;
	public int Bucket { get; set; }
	/// <summary>Position within the source inventory bucket, when authored.</summary>
	public int? BucketPosition { get; set; }
	/// <summary>Source weapon selection priority, when authored.</summary>
	public int? Weight { get; set; }
	/// <summary>Damage from the weapon script, or the mounted game's numeric skill settings, if supplied.</summary>
	public float? PlayerDamage { get; set; }
	public float? NpcDamage { get; set; }
	public float? SecondaryPlayerDamage { get; set; }
	public float? SecondaryNpcDamage { get; set; }
	public float? BlastRadius { get; set; }
	public float? SecondaryBlastRadius { get; set; }
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
internal sealed class ModelSequenceData
{
	public string Name { get; set; }
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public string Activity { get; set; } = "";
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public int ActivityWeight { get; set; }
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public bool Looping { get; set; }
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
internal sealed class ModelAnimationData
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
internal sealed class ModelPoseLayerData
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
internal sealed class ModelPoseParameterData
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
internal sealed class ModelBoneSurfaceData
{
	public string Bone { get; set; }
	public Sample[] Samples { get; set; } = [];

	public readonly record struct Sample( Vector3 Position, Vector3 Normal, Vector2 TexCoord );
}
