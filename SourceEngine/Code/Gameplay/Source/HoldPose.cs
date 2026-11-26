using System;
using Sandbox.Animation;

namespace Source1;

/// <summary>
/// How a mounted game's own human holds a weapon in third person, sampled from its sequences on its own skeleton:
/// the hands and the bones a held model merges onto, in the human's model space, aimed along +X.
/// A Counter-Strike player model has a weapon layer over its legs (<see cref="Css.PlayerAnimState"/>); Garry's Mod's HL2 player
/// has HL2MP's idle and gestures for the weapon's hold type; an HL2 NPC human has the weapon's acttable (<see cref="Hl2.ActTable"/>),
/// whose attack sequence starts from the aimed pose.
/// </summary>
public sealed class HoldPose
{
	public const string RightHandBone = "ValveBiped.Bip01_R_Hand";
	public const string LeftHandBone = "ValveBiped.Bip01_L_Hand";

	const string GModPlayer = "models/player/group03/male_07.mdl";

	/// <summary>The humans whose sequences show how a mount's weapons are held, by preference, with the mount each comes from.</summary>
	[SkipHotload]
	static readonly Dictionary<string, (string Mount, string Path)[]> Humans = new()
	{
		["gmod"] = [("gmod", GModPlayer)],
		["hl2"] = [("gmod", GModPlayer), ("hl2", "models/humans/group03/male_07.mdl")],
		["css"] = [("css", "models/player/ct_urban.mdl")]
	};

	/// <summary>Seconds a sequence that is not a delta takes to blend back into the aimed pose.</summary>
	const float BlendTime = 0.15f;

	static readonly Dictionary<string, Task<Model>> Loading = new();

	readonly IReadOnlyList<Skeleton.Bone> _bones;
	readonly Dictionary<string, int> _indices = new( StringComparer.OrdinalIgnoreCase );
	readonly (Clip Clip, bool Delta)[] _shots;
	readonly Clip _reload;
	readonly Clip[] _realTimeLayers;
	readonly Transform[] _layerBase;
	readonly Transform[] _rest;
	readonly Transform[] _pose;
	readonly SortedSet<int> _tracked = new();
	readonly (int Bone, int Target)[] _locks = [];
	int[] _order = [];
	bool _resting;

	public int RightHand { get; }
	public int LeftHand { get; }

	/// <summary>The pose is the SMG's, borrowed for a weapon its human has no hold for.</summary>
	public bool Borrowed { get; private set; }
	internal bool HasCrouchPose { get; private set; }
	internal SourceModelSequenceData Sequence { get; }

	/// <summary>The sequence holds the left hand on the weapon with IK.</summary>
	public bool LocksLeftHand { get; }

	/// <summary>The point between the shoulder joints in the aimed pose.</summary>
	public Vector3 Shoulders { get; }

	/// <summary>Shoulder to wrist along the reference arm.</summary>
	public float ArmLength { get; }

	/// <summary>The palm frames of the reference hands, in their hand bones' space; see <see cref="Palm"/>.</summary>
	public Rotation RightPalm { get; }
	public Rotation LeftPalm { get; }

	/// <summary>How many knuckles the palm frames are made from, index finger first.</summary>
	public int Knuckles { get; }

	/// <summary>Where the right fist closes in the aimed pose, relative to the right hand bone, with the aim's axes.</summary>
	public Transform Grip { get; }

	/// <summary>Seconds one pass of the reload sequence takes, or 0 without one.</summary>
	public float ReloadDuration => _reload?.Duration ?? 0;

	/// <summary>The reload is one shell's insert, repeated per shell.</summary>
	public bool ReloadLoops { get; }

	/// <summary>A tracked bone in the human's model space, as of the last <see cref="Evaluate"/>.</summary>
	public Transform this[int bone] => _pose[bone];

	/// <summary>The mount's preferred installed human, or null while it loads or when the mount has none.</summary>
	public static Model Human( string mount )
	{
		foreach ( var (source, path) in Humans.GetValueOrDefault( mount ?? "" ) ?? [] )
		{
			var resource = $"mount://{source}/{path}.vmdl";
			if ( !Loading.TryGetValue( resource, out var task ) )
			{
				var game = Sandbox.Mounting.Directory.Get( source );
				if ( game?.IsInstalled != true )
					continue;

				task = Load( game, resource );
				Loading[resource] = task;
			}

			if ( !task.IsCompleted )
				return null;

			if ( task.IsCompletedSuccessfully && task.Result.IsValid() && !task.Result.IsError )
				return task.Result;

			// Only a loaded human is kept; a missing or failed one is asked for again.
			Loading.Remove( resource );
		}

		return null;
	}

	static async Task<Model> Load( Sandbox.Mounting.BaseGameMount game, string resource )
	{
		if ( !game.IsMounted )
			await Sandbox.Mounting.Directory.Mount( game.Ident );

		return game.GetByPath( resource ) is null ? null : await Model.LoadAsync( resource );
	}

	/// <summary>
	/// The human's hold of a weapon, or null when its sequences have none. False while the sequences are still loading.
	/// </summary>
	public static bool TryResolve( Model human, BaseCombatWeapon weapon, Model weaponModel, out HoldPose pose )
		=> TryResolve( human, weapon, weaponModel, out pose, crouched: false );

	internal static bool TryResolve( Model human, BaseCombatWeapon weapon, Model weaponModel, out HoldPose pose, bool crouched )
	{
		pose = null;
		var skeleton = human.AnimationSkeleton();
		if ( !skeleton.IsValid() )
			return true;

		var sequences = ModelSequences.Of( human );
		var weaponClass = WeaponSetup.WeaponData( weapon )?.ClassName ?? weaponModel?.GetData<SourceModelWeaponData[]>()?.FirstOrDefault()?.ClassName;
		var layers = Css.PlayerAnimState.Resolve( human, sequences, weapon, weaponModel, Hl2.ActTable.Resolve( sequences, weaponClass ) );

		string lower = null;
		string crouchIdle = null;
		string aim;
		string[] shots;
		string reload;
		var borrowed = false;
		if ( layers is not null )
		{
			lower = layers.Clips.Idle;
			aim = layers.Upper[0];
			shots = layers.Shots[0];
			reload = layers.Reloads[0];
		}
		else if ( Hl2.ActTable.PlayerActivities( weaponClass ) is { } player && sequences.FindActivity( player.Idle ) is { } idle )
		{
			crouchIdle = sequences.FindActivity( Hl2.ActTable.CrouchIdle( weaponClass ) );
			aim = crouched ? crouchIdle ?? idle : idle;
			shots = [sequences.FindGesture( player.Attack )];
			reload = sequences.FindActivity( player.Reload );
		}
		else if ( weaponClass is not null )
		{
			// A weapon without a row of its own is held like the SMG, as an NPC would hold it; see BindHolder.
			borrowed = !Hl2.ActTable.TryResolve( sequences, weaponClass, out var acttable );
			acttable ??= Hl2.ActTable.Resolve( sequences, weaponClass );
			aim = acttable.Clips.Attack;
			shots = [aim];
			reload = acttable.Clips.Reload;
		}
		else
		{
			return true;
		}

		var clips = human.AnimationClips();
		Clip Find( string name ) => name is not null && clips.TryGetValue( name, out var clip ) ? clip : null;

		var aimClip = Find( aim );
		if ( aimClip is null || sequences.Get( aim ) is not { Delta: false } aimSequence )
			return true;

		var lowerClip = Find( lower );
		var reloadClip = sequences.Get( reload ) is { Delta: false } ? Find( reload ) : null;
		var shotClips = shots.Where( x => Find( x ) is not null ).Select( x => (Find( x ), sequences.Get( x )?.Delta == true) ).ToArray();
		// A whole-body clip has its delta layers baked in. A masked one leaves its aim matrix, and what the other layers do
		// outside the mask, to clips of their own; the matrix's own clip is its centre.
		var layerClips = aimSequence.BoneMask is null ? [] : aimSequence.AdditiveLayers.Select( sequences.Get ).Where( x => x is { Delta: true } )
			.Select( x => Find( x.Name ) ).Where( x => x is not null ).ToArray();

		var realTimeLayers = aimSequence.BoneMask is null ? aimSequence.AdditiveLayers.Select( sequences.Get )
			.Where( layer => layer is { Delta: true, RealTime: true } ).Select( layer => Find( layer.Name ) )
			.Where( clip => clip is { Duration: > 0 } ).ToArray() : [];

		// A clip samples as garbage until it and its skeleton have streamed in.
		Clip[] used = [aimClip, lowerClip, reloadClip, .. layerClips, .. realTimeLayers, .. shotClips.Select( x => x.Item1 )];
		if ( used.Any( x => x is not null && (!x.IsBound || !IsPosed( x, skeleton.Bones.Count )) ) )
			return false;

		pose = new HoldPose( skeleton, aimSequence, aimClip, lowerClip, layerClips, realTimeLayers, shotClips, reloadClip, layers?.ReloadLoops == true ) { Borrowed = borrowed, HasCrouchPose = crouchIdle is not null };
		if ( pose.Knuckles < 2 )
			pose = null;

		return true;
	}

	HoldPose( Skeleton skeleton, SourceModelSequenceData aim, Clip aimClip, Clip lower, Clip[] layers, Clip[] realTimeLayers, (Clip, bool)[] shots, Clip reload, bool reloadLoops )
	{
		Sequence = aim;
		_bones = skeleton.Bones;
		_shots = shots;
		_realTimeLayers = realTimeLayers;
		if ( realTimeLayers.Length > 0 ) _layerBase = new Transform[_bones.Count];
		_reload = reload;
		ReloadLoops = reloadLoops;
		_rest = new Transform[_bones.Count];
		_pose = new Transform[_bones.Count];
		for ( var i = 0; i < _bones.Count; i++ )
		{
			_indices.TryAdd( _bones[i].Name, i );
			var local = aimClip.GetLocalTransform( 0, i );
			var delta = global::Transform.Zero;
			foreach ( var layer in layers )
				delta = Add( delta, layer.GetLocalTransform( 0, i ) );

			// On the bones its mask leaves out a masked sequence holds the reference pose under its delta layers; those come from the legs.
			if ( lower is not null && Same( local, Add( _bones[i].LocalTransform, delta ) ) )
				local = lower.GetLocalTransform( 0, i );

			_rest[i] = Add( local, delta );
		}

		RightHand = Track( RightHandBone );
		LeftHand = Track( LeftHandBone );
		LocksLeftHand = aim.IkTargets.Any( x => string.Equals( x.Bone, LeftHandBone, StringComparison.OrdinalIgnoreCase ) );

		var rightShoulder = Track( "ValveBiped.Bip01_R_UpperArm" );
		var leftShoulder = Track( "ValveBiped.Bip01_L_UpperArm" );
		var elbow = Find( "ValveBiped.Bip01_R_Forearm" );
		if ( RightHand < 0 || LeftHand < 0 || rightShoulder < 0 || leftShoulder < 0 || elbow < 0 )
			return;

		EvaluateBase( null, false, 0, 0, 0 );
		Shoulders = (_pose[rightShoulder].Position + _pose[leftShoulder].Position) * 0.5f;
		ArmLength = _bones[rightShoulder].Transform.Position.Distance( _bones[elbow].Transform.Position )
			+ _bones[elbow].Transform.Position.Distance( _bones[RightHand].Transform.Position );

		var right = KnucklePositions( "R" );
		var left = KnucklePositions( "L" );
		Knuckles = Math.Min( right.Length, left.Length );
		if ( Knuckles < 2 )
			return;

		RightPalm = Palm( _bones[RightHand].Transform, right[..Knuckles] );
		LeftPalm = Palm( _bones[LeftHand].Transform, left[..Knuckles] );

		var knuckles = Enumerable.Range( 1, Knuckles ).Select( finger => Track( $"ValveBiped.Bip01_R_Finger{finger}" ) ).ToArray();
		EvaluateBase( null, false, 0, 0, 0 );
		var fist = knuckles.Aggregate( Vector3.Zero, ( sum, bone ) => sum + _pose[bone].Position ) / Knuckles;
		Grip = _pose[RightHand].ToLocal( new Transform( fist ) );

		_locks = aim.IkTargets.Select( x => (Find( x.Bone ), Track( x.TargetBone )) ).Where( x => x.Item1 >= 0 && x.Item2 >= 0 ).ToArray();
		_resting = false;
	}

	/// <summary>The reference positions of a hand's knuckles, index finger first; a hand need not have all four.</summary>
	Vector3[] KnucklePositions( string side ) => Enumerable.Range( 1, 4 ).Select( finger => Find( $"ValveBiped.Bip01_{side}_Finger{finger}" ) )
		.TakeWhile( bone => bone >= 0 ).Select( bone => _bones[bone].Transform.Position ).ToArray();

	/// <summary>
	/// A hand's palm in its hand bone's space, from the reference pose: forward from the wrist through the knuckles,
	/// up out of the plane they span. The same frame on two skeletons maps one hand bone onto the other.
	/// </summary>
	public static Rotation Palm( Transform hand, IReadOnlyList<Vector3> knuckles )
	{
		var centre = knuckles.Aggregate( Vector3.Zero, ( sum, x ) => sum + x ) / knuckles.Count;
		var forward = centre - hand.Position;
		return hand.Rotation.Inverse * Rotation.LookAt( forward, forward.Cross( knuckles[0] - knuckles[^1] ) );
	}

	/// <summary>A bone of the human's skeleton by name, or -1.</summary>
	public int Find( string bone ) => _indices.TryGetValue( bone ?? "", out var index ) ? index : -1;

	/// <summary>Evaluates a bone from now on; returns its index, or -1 when the skeleton lacks it.</summary>
	public int Track( string bone )
	{
		var index = Find( bone );
		for ( var i = index; i >= 0 && _tracked.Add( i ); i = _bones[i].ParentIndex )
			_resting = false;

		_order = [.. _tracked];
		return index;
	}

	/// <summary>True for the left hand bone and the bones under it.</summary>
	public bool IsInLeftHand( int bone ) => IsInChain( bone, LeftHand );

	bool IsInChain( int bone, int root )
	{
		while ( bone >= 0 && bone != root )
			bone = _bones[bone].ParentIndex;

		return bone >= 0;
	}

	/// <summary>How long a shot's sequence moves the pose for.</summary>
	public float ShotDuration( int shot ) => _shots.Length == 0 ? 0 : _shots[shot % _shots.Length].Clip.Duration;

	/// <summary>
	/// Poses the tracked bones: the aimed pose, <paramref name="shotTime"/> seconds into a shot's sequence and blended by
	/// <paramref name="reloadWeight"/> into the reload at <paramref name="reloadCycle"/>.
	/// </summary>
	public void Evaluate( int shot, float shotTime, float reloadCycle, float reloadWeight )
	{
		var (shotClip, delta) = _shots.Length > 0 && shotTime < ShotDuration( shot ) ? _shots[shot % _shots.Length] : default;
		var shotCycle = shotClip is null ? 0 : shotTime / shotClip.Duration;
		// An additive firing gesture overlays the already animated idle.
		var overlay = delta && _layerBase is not null ? shotClip : null;
		EvaluateBase( overlay is null ? shotClip : null, delta, shotCycle, reloadCycle, reloadWeight );
		if ( _layerBase is not null ) ApplyLayers( overlay, shotCycle );

		// Source solves IK after breathing and gestures. A reload can move the hand off its grip.
		foreach ( var (bone, target) in _locks )
		{
			if ( !_tracked.Contains( bone ) ) continue;
			var before = _pose[bone];
			var after = _reload is not null && reloadWeight > 0 ? Blend( _pose[target], before, reloadWeight ) : _pose[target];
			if ( before == after ) continue;
			_pose[bone] = after;
			foreach ( var child in _order )
				if ( child > bone && IsInChain( child, bone ) )
					_pose[child] = after.ToWorld( before.ToLocal( _pose[child] ) );
		}
	}

	void ApplyLayers( Clip overlay, float shotCycle )
	{
		// Transfer the animated source pose to the holder's existing hand IK targets.
		// The clock continues across deploys and shots.
		Span<float> cycles = stackalloc float[_realTimeLayers.Length];
		for ( var j = 0; j < cycles.Length; j++ )
		{
			var duration = _realTimeLayers[j].Duration;
			cycles[j] = (float)(Time.NowDouble % duration / duration);
		}
		foreach ( var i in _order )
		{
			var local = _layerBase[i];
			for ( var j = 0; j < cycles.Length; j++ )
				local = Add( local, _realTimeLayers[j].GetLocalTransform( cycles[j], i ) );
			if ( overlay is not null ) local = Add( local, overlay.GetLocalTransform( shotCycle, i ) );
			var parent = _bones[i].ParentIndex;
			_pose[i] = parent < 0 ? local : _pose[parent].ToWorld( local );
		}
	}

	void EvaluateBase( Clip shotClip, bool delta, float shotCycle, float reloadCycle, float reloadWeight )
	{
		var shooting = shotClip is not null;
		var reloading = _reload is not null && reloadWeight > 0;
		if ( !shooting && !reloading && _resting )
			return;

		_resting = !shooting && !reloading;
		var shotWeight = shooting ? MathF.Min( 1, (1 - shotCycle) * shotClip.Duration / BlendTime ) : 0;
		reloadCycle = Math.Clamp( reloadCycle, 0, 1 );

		foreach ( var i in _order )
		{
			var local = _rest[i];
			if ( shooting && !delta )
				local = Blend( local, shotClip.GetLocalTransform( shotCycle, i ), shotWeight );

			if ( reloading )
			{
				var pose = _reload.GetLocalTransform( reloadCycle, i );
				if ( !Same( pose, _bones[i].LocalTransform ) )
					local = Blend( local, pose, reloadWeight );
			}

			if ( shooting && delta )
				local = Add( local, shotClip.GetLocalTransform( shotCycle, i ) );

			if ( _layerBase is not null ) _layerBase[i] = local;
			_pose[i] = _bones[i].ParentIndex < 0 ? local : _pose[_bones[i].ParentIndex].ToWorld( local );
		}
	}

	static bool IsPosed( Clip clip, int bones )
	{
		for ( var i = 0; i < bones; i++ )
		{
			var local = clip.GetLocalTransform( 0, i );
			var rotation = local.Rotation;
			var length = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
			if ( !(MathF.Abs( length - 1 ) < 0.001f) || !(local.Position.Length < 1000) )
				return false;
		}

		return true;
	}

	/// <summary>bone_setup's delta accumulation: positions add, rotations apply in the bone's own space.</summary>
	static Transform Add( Transform pose, Transform delta ) => new( pose.Position + delta.Position, pose.Rotation * delta.Rotation );

	static Transform Blend( Transform from, Transform to, float weight ) => new( Vector3.Lerp( from.Position, to.Position, weight ), Rotation.Slerp( from.Rotation, to.Rotation, weight ) );

	static bool Same( Transform a, Transform b ) => a.Position.Distance( b.Position ) < 0.001f && a.Rotation.Distance( b.Rotation ) < 0.05f;
}
