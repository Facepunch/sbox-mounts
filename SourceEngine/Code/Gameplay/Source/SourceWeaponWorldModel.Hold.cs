using System;

namespace Source1;

/// <summary>
/// Holds the model the way the mounted game's own human does. <see cref="SourceHoldPose"/> poses that human; its hands,
/// placed from the holder's shoulders along the aim, become the holder's hand IK targets, and the model merges onto the
/// human's bones as they sit in the holder's hand. The two skeletons meet at the hands, by their palm frames.
/// </summary>
public sealed partial class SourceWeaponWorldModel
{
	// The s&box human rig, as its animgraph names the arms and its hand IK.
	const string HolderRightHand = "hand_R";
	const string HolderLeftHand = "hand_L";
	const string HolderRightElbow = "arm_lower_R";
	const string HolderRightShoulder = "arm_upper_R";
	const string HolderLeftShoulder = "arm_upper_L";
	const string HolderChest = "spine_2";
	const string HolderRightClavicle = "clavicle_R";
	const string SourceRightElbow = "ValveBiped.Bip01_R_Forearm";
	const string RightHandIk = "ik.hand_right";
	const string LeftHandIk = "ik.hand_left";
	static readonly string[] HolderKnuckles = ["finger_index_0", "finger_middle_0", "finger_ring_0", "finger_pinky_0"];

	/// <summary>The arm stops short of straight, where two-bone IK snaps.</summary>
	const float ReachFraction = 0.97f;

	/// <summary>A muzzle attachment further than this off the pose's aim is not the barrel.</summary>
	const float BarrelCone = 30;

	const float ReloadBlendTime = 0.15f;

	/// <summary>Seconds the mount's human is looked for every frame; after that, on the hold bone, once every <see cref="ResolveRetryInterval"/>.</summary>
	const float ResolveTimeout = 10;
	const float ResolveRetryInterval = 1;

	SourceHoldPose _hold;
	(Model Weapon, Model Holder) _holdFor;
	bool _holdPending;
	TimeSince _holdRequested;
	TimeUntil _nextHoldTry;
	bool _holding;
	BaseCombatWeapon.WeaponHandedness? _heldHandedness;
	bool _holdsLeft;
	bool _leftHeld;
	SkinnedModelRenderer _holder;
	Rotation _rightHandToSource;
	Rotation _leftHandToSource;
	float _armScale;
	float _reach;
	bool _posed;
	Vector3 _rightShoulder;
	Vector3 _leftShoulder;
	Rotation _chest;
	Rotation _chestRest;
	int _anchor;
	Transform _anchorRest;
	(GameObject Object, int Bone)[] _merged = [];
	Rotation _barrel;
	int _heldShot;
	float _shotTime = float.MaxValue;
	bool _reloading;
	float _reloadTime;
	float _reloadWeight;
	TimeSince _deployed;
	Transform _rightStart;
	Transform _leftStart;
	(string Type, string Fallback)? _heldType;
	int _elbow;
	GameObject[] _arm;
	Vector3[] _armOffsets;
	Rotation _upperInBend;
	Rotation _lowerInBend;
	float _upperLength;
	float _lowerLength;
	Rotation _clavicle;

	/// <summary>Sets the holder's hand IK targets for this frame. False when the hold falls back to the hold bone.</summary>
	bool UpdateHold()
	{
		var holder = GameObject.Parent?.Components.GetInAncestors<SkinnedModelRenderer>();
		if ( !holder.IsValid() || !holder.Animation.IsValid || IsSeated() || !ResolveHold( holder ) || !_posed )
		{
			StopHold();
			return false;
		}

		_shotTime += Time.Delta;
		var reloading = _weapon.IsValid() && _weapon.IsReloading;
		if ( reloading && !_reloading )
			_reloadTime = 0;

		_reloading = reloading;
		_reloadTime += Time.Delta;
		_reloadWeight = _reloadWeight.Approach( reloading ? 1 : 0, Time.Delta / ReloadBlendTime );
		var repeats = _hold.ReloadLoops || _weapon.IsValid() && _weapon.IncrementalReloading;
		var reloadCycle = _reloadTime / (_hold.ReloadLoops ? _hold.ReloadDuration : MathF.Max( _weapon.IsValid() ? _weapon.ReloadTime : 1, 0.1f ));
		_hold.Evaluate( _heldShot, _shotTime, repeats ? reloadCycle % 1 : reloadCycle, _reloadWeight );

		// The human's hands, from between the holder's shoulders along the aim, scaled to the holder's arms. All of it in
		// the body's space, where the graph takes its targets.
		// A gun points where the holder looks; a melee weapon leans with the chest.
		var lean = (_chest * _chestRest.Inverse).Up;
		var aim = SourceWeaponSetup.IsMelee( _weapon ) ? Rotation.FromPitch( MathF.Atan2( lean.x, lean.z ).RadianToDegree() ) : holder.WorldRotation.Inverse * AimRotation( holder ) * _barrel.Inverse;
		var frame = new Transform( (_rightShoulder + _leftShoulder) * 0.5f, aim );
		var right = _hold[_hold.RightHand];
		var source = frame.ToWorld( new Transform( (right.Position - _hold.Shoulders) * _armScale, right.Rotation ) );
		var sourceLeft = source.ToWorld( right.ToLocal( _hold[_hold.LeftHand] ) );
		var rightTarget = new Transform( source.Position, (source.Rotation * _rightHandToSource.Inverse).Normal );
		var leftTarget = new Transform( sourceLeft.Position, (sourceLeft.Rotation * _leftHandToSource.Inverse).Normal );

		// The hands come onto the weapon over its deploy, from where the holder's own pose last had them.
		if ( !_holding )
		{
			_deployed = 0;
			_rightStart = HandInBody( holder, HolderRightHand ) ?? rightTarget;
			_leftStart = HandInBody( holder, HolderLeftHand ) ?? leftTarget;
		}

		var deployTime = _weapon.IsValid() ? _weapon.DeployTime : 0;
		var deployed = deployTime > 0 ? MathF.Min( _deployed / deployTime, 1 ) : 1;
		rightTarget = Blend( _rightStart, rightTarget, deployed );
		leftTarget = Blend( _leftStart, leftTarget, deployed );

		// The model keeps its size on shorter arms, so the whole hold moves in until both hands reach it.
		var pull = _holdsLeft ? Reach( _leftShoulder, leftTarget.Position ) : Vector3.Zero;
		pull += Reach( _rightShoulder, rightTarget.Position + pull );
		rightTarget.Position += pull;
		leftTarget.Position += pull;

		// The graph asserts on a target that is not finite or not a rotation.
		if ( !IsFinite( rightTarget ) || _holdsLeft && !IsFinite( leftTarget ) )
			return _holding;

		var parameters = holder.Animation.Parameters;
		if ( _arm is not null )
		{
			var elbow = frame.ToWorld( new Transform( (_hold[_elbow].Position - _hold.Shoulders) * _armScale ) ).Position + pull;
			SolveArm( rightTarget, elbow );
		}
		else if ( !parameters.TrySet( RightHandIk, rightTarget ) )
		{
			_hold = null;
			StopHold();
			return false;
		}

		parameters.TrySet( $"{RightHandIk}.enabled", _arm is null );
		if ( _holdsLeft )
			parameters.TrySet( LeftHandIk, leftTarget );

		if ( _holdsLeft || _leftHeld )
			parameters.TrySet( $"{LeftHandIk}.enabled", _holdsLeft );

		_leftHeld = _holdsLeft;
		if ( !_holding )
			StartHold();

		// A bone the human animates apart from the anchor, such as a second gun in the left hand or a magazine.
		foreach ( var (bone, index) in _merged )
		{
			if ( bone.IsValid() )
				bone.WorldTransform = Renderer.WorldTransform.ToWorld( _anchorRest.ToWorld( _hold[_anchor].ToLocal( _hold[index] ) ) );
		}

		return true;
	}

	/// <summary>
	/// Poses the right arm itself, the elbow on the side the human's is: the graph's hand IK bends the elbow the way its own pose
	/// does, which under a swinging arm is anywhere. Everything in the body's space; the bones take it relative to their parents.
	/// </summary>
	void SolveArm( Transform hand, Vector3 elbowHint )
	{
		var toHand = hand.Position - _rightShoulder;
		var distance = Math.Clamp( toHand.Length, MathF.Abs( _upperLength - _lowerLength ) + 0.01f, _upperLength + _lowerLength - 0.01f );
		var along = toHand.Normal;
		var side = elbowHint - _rightShoulder;
		side -= along * side.Dot( along );
		if ( side.Length < 0.01f )
			side = Vector3.Down - along * Vector3.Down.Dot( along );

		side = side.Normal;
		var reach = (_upperLength * _upperLength - _lowerLength * _lowerLength + distance * distance) / (2 * distance);
		var height = MathF.Sqrt( MathF.Max( _upperLength * _upperLength - reach * reach, 0 ) );
		var elbow = _rightShoulder + along * reach + side * height;
		var hinge = side.Cross( along );
		var upper = Rotation.LookAt( elbow - _rightShoulder, hinge ) * _upperInBend;
		var lower = Rotation.LookAt( _rightShoulder + along * distance - elbow, hinge ) * _lowerInBend;

		ReadOnlySpan<Rotation> parents = [_clavicle, upper, lower];
		ReadOnlySpan<Rotation> bones = [upper, lower, hand.Rotation];
		for ( var i = 0; i < _arm.Length; i++ )
		{
			_arm[i].Flags |= GameObjectFlags.ProceduralBone;
			_arm[i].LocalTransform = new Transform( _armOffsets[i], (parents[i].Inverse * bones[i]).Normal );
		}
	}

	/// <summary>
	/// Once this frame's pose is final: puts the model in the hand, so it never trails the IK, and notes where the shoulders
	/// are on the body. The bones and the body only agree here; by the next update the body has moved on, or teleported.
	/// </summary>
	protected override void OnPreRender()
	{
		if ( !_holder.IsValid() || !Renderer.IsValid() )
			return;

		var body = _holder.WorldTransform;
		var hasRight = _holder.TryGetBoneTransform( HolderRightShoulder, out var rightShoulder );
		var hasLeft = _holder.TryGetBoneTransform( HolderLeftShoulder, out var leftShoulder );
		var hasChest = _holder.TryGetBoneTransform( HolderChest, out var chest );
		_posed = hasRight && hasLeft && hasChest;
		_chest = body.Rotation.Inverse * chest.Rotation;
		if ( _holder.TryGetBoneTransform( HolderRightClavicle, out var clavicle ) )
			_clavicle = body.Rotation.Inverse * clavicle.Rotation;
		_rightShoulder = body.PointToLocal( rightShoulder.Position );
		_leftShoulder = body.PointToLocal( leftShoulder.Position );
		if ( !_holding || !_holder.TryGetBoneTransform( HolderRightHand, out var hand ) )
			return;

		var source = new Transform( hand.Position, hand.Rotation * _rightHandToSource, hand.UniformScale );
		var anchor = _anchor < 0 ? _hold.Grip : _hold[_hold.RightHand].ToLocal( _hold[_anchor] );
		Renderer.WorldTransform = source.ToWorld( anchor ).ToWorld( _anchorRest.ToLocal( global::Transform.Zero ) );
	}

	/// <summary>The hold for this model on this holder, resolved once the mount's human has loaded.</summary>
	bool ResolveHold( SkinnedModelRenderer holder )
	{
		if ( !holder.IsValid() || !holder.Model.IsValid() || Application.IsDedicatedServer )
			return false;

		if ( _holdFor != (Renderer.Model, holder.Model) )
		{
			StopHold();
			_holdFor = (Renderer.Model, holder.Model);
			_hold = null;
			_holder = holder;
			_posed = false;
			_holdPending = true;
			_holdRequested = 0;
			_nextHoldTry = 0;
		}

		if ( _holdPending && _nextHoldTry && SourceHoldPose.Human( SourceMount.Of( Renderer.Model ) ) is { } human
			&& SourceHoldPose.TryResolve( human, _weapon, Renderer.Model, out var hold ) )
		{
			_holdPending = false;
			_hold = hold is not null && BindHolder( hold, holder ) ? hold : null;
		}
		else if ( _holdPending && _nextHoldTry && _holdRequested > ResolveTimeout )
		{
			_nextHoldTry = ResolveRetryInterval;
		}

		return _hold is not null;
	}

	/// <summary>Maps the human's hands onto the holder's and finds the bones the model merges onto. False for a holder without the rig.</summary>
	bool BindHolder( SourceHoldPose hold, SkinnedModelRenderer holder )
	{
		var bones = holder.Model.Bones;
		var shoulder = bones.GetBone( HolderRightShoulder );
		var elbow = bones.GetBone( HolderRightElbow );
		var hand = bones.GetBone( HolderRightHand );
		var chest = bones.GetBone( HolderChest );
		if ( shoulder is null || elbow is null || hand is null || chest is null || bones.GetBone( HolderLeftShoulder ) is null )
			return false;

		_chestRest = chest.LocalTransform.Rotation;

		if ( HolderPalm( bones, HolderRightHand, "R", hold.Knuckles ) is not { } rightPalm || HolderPalm( bones, HolderLeftHand, "L", hold.Knuckles ) is not { } leftPalm )
			return false;

		_rightHandToSource = rightPalm * hold.RightPalm.Inverse;
		_leftHandToSource = leftPalm * hold.LeftPalm.Inverse;
		var arm = shoulder.LocalTransform.Position.Distance( elbow.LocalTransform.Position ) + elbow.LocalTransform.Position.Distance( hand.LocalTransform.Position );
		_armScale = arm / hold.ArmLength;
		_reach = arm * ReachFraction;

		// Bonemerge: every bone of the model that the human has follows the human's. The first positions the model.
		var merged = Renderer.Model.Bones.AllBones.Select( bone => (Bone: bone, Index: hold.Track( bone.Name )) ).Where( x => x.Index >= 0 ).ToArray();
		_anchor = merged.Length > 0 ? merged[0].Index : -1;
		_anchorRest = merged.Length > 0 ? merged[0].Bone.LocalTransform : PropGrip( Renderer.Model.Bounds );
		_merged = merged.Skip( 1 ).Select( x => (Renderer.GetBoneObject( x.Bone ), x.Index) ).Where( x => x.Item1.IsValid() ).ToArray();

		// The model as the human holds it, in the human's space.
		hold.Evaluate( 0, float.MaxValue, 0, 0 );
		var right = hold[hold.RightHand];
		var model = right.ToWorld( _anchor < 0 ? hold.Grip : right.ToLocal( hold[_anchor] ) ).ToWorld( _anchorRest.ToLocal( global::Transform.Zero ) );

		// The left hand stays on the weapon where the sequence locks it there, where part of the model rides on it,
		// or where the pose simply has it within a hand of the model.
		var leftHand = model.PointToLocal( hold[hold.LeftHand].Position );
		var reachesLeft = Renderer.Model.Bounds.ClosestPoint( leftHand ).Distance( leftHand ) < hold.Grip.Position.Length;
		_holdsLeft = hold.LocksLeftHand || merged.Any( x => hold.IsInLeftHand( x.Index ) ) || reachesLeft;

		// The SMG's two-handed hold, borrowed, only suits a model long enough to fill both hands.
		if ( hold.Borrowed && !reachesLeft )
			return false;

		// A melee weapon's arm is posed here, elbow and all. In the reference pose an elbow folds forward.
		_arm = null;
		_elbow = hold.Track( SourceRightElbow );
		GameObject[] armBones = [holder.GetBoneObject( shoulder ), holder.GetBoneObject( elbow ), holder.GetBoneObject( hand )];
		if ( SourceWeaponSetup.IsMelee( _weapon ) && _elbow >= 0 && shoulder.Parent?.Name == HolderRightClavicle && armBones.All( x => x.IsValid() ) )
		{
			var upper = elbow.LocalTransform.Position - shoulder.LocalTransform.Position;
			var lower = hand.LocalTransform.Position - elbow.LocalTransform.Position;
			var hinge = upper.Cross( Vector3.Forward );
			_upperInBend = Rotation.LookAt( upper, hinge ).Inverse * shoulder.LocalTransform.Rotation;
			_lowerInBend = Rotation.LookAt( lower, hinge ).Inverse * elbow.LocalTransform.Rotation;
			_upperLength = upper.Length;
			_lowerLength = lower.Length;
			_armOffsets = [shoulder.Parent.LocalTransform.PointToLocal( shoulder.LocalTransform.Position ), shoulder.LocalTransform.PointToLocal( elbow.LocalTransform.Position ), elbow.LocalTransform.PointToLocal( hand.LocalTransform.Position )];
			_arm = armBones;
		}

		// The pose aims roughly down +X; the model's muzzle says exactly where its barrel points in it.
		_barrel = Rotation.Identity;
		var muzzle = _muzzleName is null || _anchor < 0 ? null : Renderer.Model.Attachments.All.FirstOrDefault( a => a.Name == _muzzleName );
		if ( muzzle is not null )
		{
			var barrel = model.Rotation * muzzle.WorldTransform.Rotation.Forward;
			if ( barrel.Angle( Vector3.Forward ) < BarrelCone )
				_barrel = Rotation.LookAt( barrel );
		}

		return true;
	}

	/// <summary>
	/// Approximation for a model with none of the human's bones, which the mounted game never put in a hand: its shape lays it
	/// along the aim in the fist. A gun is mirrored left to right, so its side axis is the one its bounds are most centred on;
	/// the longer of the other two is the barrel, with the muzzle on the long side of the origin. An upright model stands Z up
	/// as Source authors it; a prop lying on its side hangs its grip on the deep side of the last axis.
	/// Returns the grip, behind and below the middle, facing along the barrel.
	/// </summary>
	static Transform PropGrip( BBox bounds )
	{
		var size = bounds.Size;
		if ( size.x <= 0 || size.y <= 0 || size.z <= 0 )
			return global::Transform.Zero;

		var centre = bounds.Center;
		var side = Enumerable.Range( 0, 3 ).MinBy( axis => MathF.Abs( centre[axis] ) / size[axis] );
		var others = Enumerable.Range( 0, 3 ).Where( axis => axis != side ).OrderByDescending( axis => size[axis] ).ToArray();
		var forward = Vector3.Zero;
		forward[others[0]] = centre[others[0]] < 0 ? -1 : 1;
		var up = Vector3.Zero;
		up[others[1]] = UpSign( bounds, others[1] );
		var grip = centre - forward * size[others[0]] * 0.25f - up * size[others[1]] * 0.25f;
		return new Transform( grip, Rotation.LookAt( forward, up ) );
	}

	/// <summary>
	/// Which way is up along <paramref name="axis"/> of a gun's bounds: +Z for one standing upright as Source authors it, else away
	/// from the side reaching further from the origin, its barrel line, where a gun lying on its side hangs its grip. Centred on
	/// the origin, like a crossbow's limbs, it has no deep side and stays as authored.
	/// </summary>
	internal static float UpSign( BBox bounds, int axis ) => axis == 2 || bounds.Center[axis] < bounds.Size[axis] * 0.05f ? 1 : -1;

	/// <summary>The holder's palm frame for a hand, from as many knuckles as the human's has.</summary>
	static Rotation? HolderPalm( BoneCollection bones, string handName, string side, int knuckles )
	{
		var hand = bones.GetBone( handName );
		var joints = HolderKnuckles.Take( knuckles ).Select( x => bones.GetBone( $"{x}_{side}" ) ).ToArray();
		if ( hand is null || joints.Any( x => x is null ) )
			return null;

		return SourceHoldPose.Palm( hand.LocalTransform, joints.Select( x => x.LocalTransform.Position ).ToArray() );
	}

	void StartHold()
	{
		_holding = true;
		ReleaseLeftHand();
		_alignedModel = null;
		foreach ( var (bone, _) in _merged )
			bone.Flags |= GameObjectFlags.ProceduralBone;

		// The human's left hand is off the weapon, so the holder's is too.
		if ( !_holdsLeft && _weapon.IsValid() )
		{
			_heldHandedness = _weapon.Handedness;
			_weapon.Handedness = BaseCombatWeapon.WeaponHandedness.Right;
		}

		// A melee weapon's whole arm is the human's pose. The graph's own melee stance and swing under the hand IK would bend the elbow their way.
		if ( SourceWeaponSetup.IsMelee( _weapon ) )
		{
			_heldType = (_weapon.HoldType, _weapon.HoldFallback);
			_weapon.HoldType = "";
			_weapon.HoldFallback = "";

			// A weapon without a hold leaves the stance it last set, so drop it here.
			if ( _holder.IsValid() )
				_holder.Animation.Parameters.TrySet( "holdtype", "none" );
		}
	}

	void StopHold()
	{
		if ( !_holding )
			return;

		_holding = false;
		foreach ( var (bone, _) in _merged )
		{
			if ( bone.IsValid() )
				bone.Flags &= ~GameObjectFlags.ProceduralBone;
		}

		if ( _heldType is { } heldType && _weapon.IsValid() )
			(_weapon.HoldType, _weapon.HoldFallback) = heldType;

		if ( _heldHandedness is { } handedness && _weapon.IsValid() )
			_weapon.Handedness = handedness;

		_heldHandedness = null;
		_heldType = null;
		foreach ( var bone in _arm ?? [] )
		{
			if ( bone.IsValid() )
				bone.Flags &= ~GameObjectFlags.ProceduralBone;
		}

		if ( !_holder.IsValid() || !_holder.Animation.IsValid )
			return;

		_holder.Animation.Parameters.TrySet( $"{RightHandIk}.enabled", false );
		if ( _leftHeld )
			_holder.Animation.Parameters.TrySet( $"{LeftHandIk}.enabled", false );

		_leftHeld = false;
	}

	protected override void OnDisabled() => StopHold();

	/// <summary>Where the holder looks: a player's synced eye angles, else the way the body faces.</summary>
	Rotation AimRotation( SkinnedModelRenderer holder ) => _weapon.IsValid() && _weapon.Owner.IsValid() ? _weapon.Owner.EyeAngles.ToRotation() : holder.WorldRotation;

	/// <summary>A seat poses its sitter's hands itself.</summary>
	bool IsSeated() => _weapon.IsValid() && _weapon.Owner.IsValid() && _weapon.Owner.GetComponentInParent<Sandbox.Movement.ISitTarget>( false ) is not null;

	/// <summary>A hand bone as last posed, in the body's space.</summary>
	static Transform? HandInBody( SkinnedModelRenderer holder, string bone )
	{
		if ( !holder.TryGetBoneTransform( bone, out var hand ) )
			return null;

		return holder.WorldTransform.ToLocal( hand ).WithScale( 1 );
	}

	static Transform Blend( Transform from, Transform to, float weight ) => new( Vector3.Lerp( from.Position, to.Position, weight ), Rotation.Slerp( from.Rotation, to.Rotation, weight ).Normal );

	static bool IsFinite( Transform target )
	{
		var rotation = target.Rotation;
		var length = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
		return MathF.Abs( length - 1 ) < 0.0001f && target.Position.Length < float.MaxValue;
	}

	/// <summary>How far a hand target has to move toward its shoulder for the arm to reach it.</summary>
	Vector3 Reach( Vector3 shoulder, Vector3 target )
	{
		var offset = target - shoulder;
		var excess = offset.Length - _reach;
		return excess > 0 ? -offset.Normal * excess : Vector3.Zero;
	}
}
