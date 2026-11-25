using System;
using Sandbox.Animation;
using System.Runtime.CompilerServices;

namespace Source1;

/// <summary>Retargets mounted Source viewmodel bones onto sandbox's first-person hands.</summary>
[Title( "Source View Model Hands" )]
[Category( "Source 1" )]
public sealed class SourceViewModelHands : Component
{
	[Property] public SkinnedModelRenderer Source { get; set; }
	SkinnedModelRenderer _hands;
	Model _sourceModel;
	Transform[] _sourcePose;
	Model _failedModel;
	bool _loading;
	Transform[] _local;
	Transform[] _pose;
	Transform?[] _mapped;
	bool[] _fingers;
	bool[] _supported;
	bool[] _contacts;
	int[] _aimChild;
	(int End, int Hand)[] _arms;
	int[] _mapping;
	Transform?[] _gripPose;
	Transform?[] _palmOffsets;
	int[] _anchors;
	int[] _parents;
	Rotation[] _axes;
	(int Lower, int Hand, float Weight)[] _twists;
	int _handMaterial = -1;
	bool _mirrored;
	(int Lower, int Hand)[] _upperArms;
	SourceAnimationPlayer _player;
	sealed class HandClips
	{
		public Skeleton Skeleton;
		public readonly Dictionary<Clip, Clip> Clips = new();
	}
	static readonly ConditionalWeakTable<Model, Dictionary<(Model, bool), HandClips>> Cache = new();
	HandClips _clips;
	bool _waitingForPose;
	bool _renderGame;
	bool _renderOverlay;

	/// <summary>HL2's v_hand sheet, or the arms built into Counter-Strike: Source's own viewmodels.</summary>
	static bool IsGlove( Material material ) => material.Name.Contains( "/v_hand/" ) || material.Name.Contains( "/v_models/hands/" );

	/// <summary>The viewmodel is drawn mirrored (see <see cref="SourceViewModel"/>); the hands are then fitted to its mirror image.</summary>
	bool SourceMirrored => Source.GameObject.LocalScale.y < 0;

	/// <summary>Hides the mounted glove material and returns its slot, or -1.</summary>
	public static int HideOriginalHands( SkinnedModelRenderer renderer )
	{
		var slot = Array.FindIndex( renderer.Model.Materials.ToArray(), IsGlove );
		if ( slot < 0 || renderer.Materials.HasOverride( slot ) ) return slot;
		var hidden = Material.Create( "hl2_hidden_hands", "shaders/complex.shader" );
		hidden.SetFeature( "F_ALPHA_TEST", 1 );
		hidden.Set( "g_tColor", Texture.Transparent );
		renderer.Materials.SetOverride( slot, hidden );
		return slot;
	}

	protected override void OnDisabled()
	{
		Release();
		_sourceModel = null;
		_failedModel = null;
	}

	protected override void OnUpdate()
	{
		if ( !Source.IsValid() || !Source.Model.IsValid() || Source.Model.IsError ) return;
		var stale = _sourceModel != Source.Model || _mirrored != SourceMirrored || _player is null;
		if ( !_loading && _failedModel != Source.Model && stale ) _ = Configure();
		if ( _hands.IsValid() ) _hands.Enabled = Source.Enabled;
	}

	async Task Configure()
	{
		_loading = true;
		if ( !_waitingForPose )
		{
			_renderGame = Source.RenderOptions.Game;
			_renderOverlay = Source.RenderOptions.Overlay;
		}
		Source.RenderOptions.Game = false;
		Source.RenderOptions.Overlay = false;
		_waitingForPose = true;
		var configured = false;
		var stale = false;
		var source = Source;
		var model = source.Model;
		_mirrored = SourceMirrored;
		try
		{
			var arms = await Model.LoadAsync( SourceViewModel.HandsModel );
			// This mounted reference retains every finger on both hands. Many gun
			// viewmodels collapse the trigger hand into a rigid weapon control.
			var mountName = SourceMount.Of( model );
			var reference = await Model.LoadAsync( $"mount://{mountName}/models/weapons/v_grenade.mdl.vmdl" );
			var wristReference = await Model.LoadAsync( $"mount://{mountName}/models/weapons/v_pistol.mdl.vmdl" );
			// The model changed while loading; the newer Configure owns the result.
			stale = !IsValid || !source.IsValid() || Source != source || source.Model != model || _mirrored != SourceMirrored;
			if ( stale || arms.IsError || reference.IsError || wristReference.IsError ) return;
			// Not the citizen arms these are fitted to: keep the gloves.
			if ( arms.Bones.GetBone( "hand_L" ) is null || arms.Bones.GetBone( "hand_R" ) is null || arms.Bones.GetBone( "arm_lower_L" ) is null || arms.Bones.GetBone( "arm_lower_R" ) is null ) return;
			Release();
			_sourceModel = model;
			_sourcePose = new Transform[model.BoneCount];
			// Our own arm mesh is never drawn mirrored: it takes the mirrored pose on the opposite arm instead.
			var go = new GameObject( _mirrored ? Source.GameObject.Parent : Source.GameObject, true, "Sandbox hands" );
			if ( _mirrored ) go.LocalTransform = Source.LocalTransform.WithScale( 1 );
			go.NetworkMode = NetworkMode.Never;
			_hands = go.AddComponent<SkinnedModelRenderer>();
			_hands.Model = arms;
			_hands.BodyGroups = arms.Parts.DefaultMask;
			_hands.UseAnimGraph = false;
			_hands.CreateBoneObjects = false;
			_hands.RenderOptions.Game = false;
			_hands.RenderOptions.Overlay = false;

			_hands.RenderType = ModelRenderer.ShadowRenderType.Off;
			var bones = arms.Bones.AllBones;
			_local = new Transform[bones.Count];
			_pose = new Transform[bones.Count];
			_mapped = new Transform?[bones.Count];
			_fingers = new bool[bones.Count];
			_supported = new bool[bones.Count];
			_contacts = new bool[bones.Count];
			_aimChild = new int[bones.Count];
			_arms = new (int, int)[bones.Count];
			_mapping = new int[bones.Count];
			_parents = new int[bones.Count];
			_axes = new Rotation[bones.Count];
			_gripPose = new Transform?[bones.Count];
			_palmOffsets = new Transform?[bones.Count];
			_anchors = new int[bones.Count];
			_twists = new (int, int, float)[bones.Count];
			_upperArms = new (int, int)[bones.Count];
			var mesh = new SourceHandPose.HandMesh( model );
			var referenceMesh = new SourceHandPose.HandMesh( reference );
			var wristMesh = new SourceHandPose.HandMesh( wristReference );
			var hasLeft = FindSourceBone( "ValveBiped.Bip01_L_Hand" ) >= 0;
			var hasRight = FindSourceBone( "ValveBiped.Bip01_R_Hand" ) >= 0;
			var recoveredLeft = !hasLeft ? SourceHandPose.RecoverHand( mesh, wristMesh, true ) : null;
			var recoveredRight = !hasRight ? SourceHandPose.RecoverHand( mesh, wristMesh, false ) : null;
			var leftArm = recoveredLeft.HasValue ? SourceHandPose.RecoverForearm( mesh, wristMesh, true ) : null;
			var rightArm = recoveredRight.HasValue ? SourceHandPose.RecoverForearm( mesh, wristMesh, false ) : null;
			var leftAnchor = hasLeft ? FindSourceBone( "ValveBiped.Bip01_L_Hand" ) : FindAnchor( recoveredLeft, wristReference, "L" );
			var rightAnchor = hasRight ? FindSourceBone( "ValveBiped.Bip01_R_Hand" ) : FindAnchor( recoveredRight, wristReference, "R" );
			hasLeft |= recoveredLeft.HasValue && leftAnchor >= 0;
			hasRight |= recoveredRight.HasValue && rightAnchor >= 0;
			for ( var i = 0; i < bones.Count; i++ )
			{
				var bone = bones[i];
				_fingers[i] = bone.Name.StartsWith( "finger_" );
				_aimChild[i] = _fingers[i] ? bones.FirstOrDefault( b => b.Parent == bone )?.Index ?? -1 : -1;
				_supported[i] = (!bone.Name.Contains( "_L" ) || hasLeft) && (!bone.Name.Contains( "_R" ) || hasRight);
				_arms[i] = (-1, -1);
				if ( bone.Name.StartsWith( "arm_" ) && !bone.Name.Contains( "twist" ) )
				{
					var side = bone.Name.EndsWith( "_L" ) ? "L" : "R";
					_arms[i] = (arms.Bones.GetBone( bone.Name.StartsWith( "arm_upper" ) ? $"arm_lower_{side}" : $"hand_{side}" ).Index,
						arms.Bones.GetBone( $"hand_{side}" ).Index);
				}
				_twists[i] = (-1, -1, 0);
				if ( bone.Name.Contains( "_twist" ) )
				{
					var side = bone.Name.Contains( "_L_" ) ? "L" : "R";
					_twists[i] = (arms.Bones.GetBone( $"arm_lower_{side}" ).Index,
						arms.Bones.GetBone( $"hand_{side}" ).Index, (bone.Name[^1] - '0') / 3f);
				}
				_parents[i] = bone.Parent?.Index ?? -1;
				_local[i] = bone.Parent is null ? bone.LocalTransform : bone.Parent.LocalTransform.ToLocal( bone.LocalTransform );
				var sourceName = SourceBone( bone.Name );
				_mapping[i] = FindSourceBone( sourceName );
				_upperArms[i] = (-1, -1);
				if ( bone.Name.StartsWith( "arm_upper_" ) && _mapping[i] < 0 )
				{
					var side = bone.Name[^1];
					_upperArms[i] = (arms.Bones.GetBone( $"arm_lower_{side}" ).Index, arms.Bones.GetBone( $"hand_{side}" ).Index);
				}
				_contacts[i] = _mapping[i] >= 0;
				var left = bone.Name.EndsWith( "_L" );
				var recovered = left ? recoveredLeft : recoveredRight;
				if ( bone.Name.StartsWith( "arm_" ) || bone.Name.StartsWith( "clavicle_" ) )
					recovered = (left ? leftArm : rightArm) ?? recovered;
				_anchors[i] = left ? leftAnchor : rightAnchor;
				var roll = bone.Name.EndsWith( "_L" ) ? 90 : -90;
				// Thumbs flex around the opposite axis to the other fingers.
				if ( bone.Name.StartsWith( "finger_thumb_" ) ) roll = -roll;
				_axes[i] = Rotation.FromRoll( roll );
				if ( sourceName is not null && _mapping[i] < 0 && _anchors[i] >= 0 )
				{
					var jointReference = wristReference;
					if ( _fingers[i] && SourceHandPose.RecoverJoint( mesh, referenceMesh, sourceName ) is { } joint )
					{
						recovered = joint;
						jointReference = reference;
						_contacts[i] = true;
					}
					var referenceBone = jointReference.Bones.GetBone( sourceName );
					if ( referenceBone is not null && recovered.HasValue )
						_gripPose[i] = SourceBind( _anchors[i] ).ToLocal( recovered.Value.ToWorld( referenceBone.LocalTransform ) );
				}
			}
			FitRigAxes( arms );
			// Finger roots describe the palm's actual contact frame. Fit once in
			// model space, then carry that correction with the animated source wrist.
			foreach ( var side in new[] { "L", "R" } )
			{
				var hand = arms.Bones.GetBone( $"hand_{side}" );
				Transform? Contact( int index ) => _mapping[index] >= 0 ? SourceBind( _mapping[index] )
					: _anchors[index] >= 0 && _gripPose[index].HasValue ? SourceBind( _anchors[index] ).ToWorld( _gripPose[index].Value ) : null;
				if ( Contact( hand.Index ) is not { } wrist ) continue;
				var roots = bones.Where( bone => bone.Name.StartsWith( "finger_" ) && bone.Name.EndsWith( $"_0_{side}" )
					&& _contacts[bone.Index] && Contact( bone.Index ).HasValue ).ToArray();
				var fit = SourceHandPose.FitPalm( roots.Select( bone => bone.LocalTransform.Position ).ToArray(),
					roots.Select( bone => Contact( bone.Index ).Value.Position ).ToArray() );
				if ( fit.HasValue )
					_palmOffsets[hand.Index] = wrist.WithRotation( wrist.Rotation * _axes[hand.Index] ).ToLocal( fit.Value.ToWorld( hand.LocalTransform ) );
			}
			var rigs = Cache.GetOrCreateValue( model );
			if ( !rigs.TryGetValue( (arms, _mirrored), out _clips ) )
			{
				var skeleton = new SkeletonBuilder();
				foreach ( var bone in bones ) skeleton.AddBone( bone.Name, _local[bone.Index], bone.Parent?.Name );
				rigs[(arms, _mirrored)] = _clips = new HandClips { Skeleton = skeleton.Create() };
			}
			_player = new SourceAnimationPlayer();
			_handMaterial = HideOriginalHands( Source );
			_failedModel = null;
			configured = true;
		}
		catch ( Exception e ) { Log.Warning( $"Couldn't retarget HL2 hands: {e.Message}" ); }
		finally
		{
			_loading = false;
			if ( !configured && !stale && source.IsValid() && Source == source )
			{
				// A mount may omit the reference hand mesh. Restore the authored
				// viewmodel once, rather than hiding the weapon and retrying every frame.
				Release();
				var slot = Array.FindIndex( source.Model.Materials.ToArray(), IsGlove );
				if ( slot >= 0 ) source.Materials.SetOverride( slot, null );
				_failedModel = model;
			}
		}
	}

	static string SourceBone( string name )
	{
		var side = name.EndsWith( "_L" ) ? "L" : name.EndsWith( "_R" ) ? "R" : null;
		if ( side is null ) return null;
		var part = name[..^2] switch { "clavicle" => "Clavicle", "arm_upper" => "UpperArm", "arm_lower" => "Forearm", "hand" => "Hand", _ => null };
		if ( part is not null ) return $"ValveBiped.Bip01_{side}_{part}";
		var fingers = new[] { "thumb", "index", "middle", "ring", "pinky" };
		for ( var digit = 0; digit < fingers.Length; digit++ )
			for ( var joint = 0; joint < 3; joint++ )
				if ( name == $"finger_{fingers[digit]}_{joint}_{side}" ) return $"ValveBiped.Bip01_{side}_Finger{digit}{(joint == 0 ? "" : joint.ToString())}";
		return null;
	}

	int FindSourceBone( string name )
	{
		if ( name is null ) return -1;
		// Exporters retain different scene namespaces around the same humanoid rig.
		var canonical = name[(name.LastIndexOf( '.' ) + 1)..];
		var bone = _sourceModel.Bones.AllBones.FirstOrDefault( b => b.Name == canonical || b.Name.EndsWith( "." + canonical ) );
		if ( bone is not null ) return bone.Index;
		var alias = RigBone( canonical, _mirrored );
		return alias is null ? -1 : _sourceModel.Bones.AllBones.FirstOrDefault( b => Normalized( b.Name ) == alias )?.Index ?? -1;
	}

	static readonly string[] Digits = ["thumb", "index", "middle", "ring", "pinky"];

	static string Normalized( string name ) => name[(name.LastIndexOf( '.' ) + 1)..].Replace( "_", "" ).ToLowerInvariant();

	/// <summary>
	/// A Bip01 part's name on Counter-Strike: Source's v_weapon rig, normalized ("Left_Index02" and
	/// "Left_Thumb_02" alike). It has forearms and hands but no upper arms. Mirrored, the other side's.
	/// </summary>
	static string RigBone( string bip01, bool mirrored )
	{
		var parts = bip01.Split( '_' );
		if ( parts.Length != 3 || parts[0] != "Bip01" ) return null;
		var side = (parts[1] == "L") != mirrored ? "left" : "right";
		if ( parts[2] == "Hand" ) return side + "hand";
		if ( parts[2] == "Forearm" ) return side + "arm";
		if ( !parts[2].StartsWith( "Finger" ) || parts[2].Length < 7 ) return null;
		var digit = parts[2][6] - '0';
		var joint = parts[2].Length > 7 ? parts[2][7] - '0' : 0;
		return digit is >= 0 and < 5 ? $"{side}{Digits[digit]}0{joint + 1}" : null;
	}

	/// <summary>A source bone's bind pose in model space, mirrored with the viewmodel.</summary>
	Transform SourceBind( int index )
	{
		var bind = _sourceModel.Bones.AllBones[index].LocalTransform;
		return _mirrored ? Mirror( bind ) : bind;
	}

	/// <summary>Reflects a model space transform across the viewmodel's forward-up plane, as a proper rotation.</summary>
	static Transform Mirror( Transform t ) => new( t.Position * new Vector3( 1, -1, 1 ), new Rotation( -t.Rotation.x, t.Rotation.y, -t.Rotation.z, t.Rotation.w ), t.Scale );

	/// <summary>Aligns the source and target bind frames using the palm and joint directions.</summary>
	void FitRigAxes( Model arms )
	{
		var bones = arms.Bones.AllBones;
		int Bone( string name ) => arms.Bones.GetBone( name )?.Index ?? -1;
		int Mapped( int target ) => target >= 0 ? _mapping[target] : -1;
		Rotation? Frame( int index, int? next, int hand, int index0, int middle0, int pinky0, bool transverse, Func<int, Transform> at )
		{
			if ( index < 0 || next < 0 || hand < 0 || index0 < 0 || middle0 < 0 || pinky0 < 0 ) return null;
			var pose = at( index );
			var direction = next.HasValue ? at( next.Value ).Position - pose.Position : pose.Rotation.Forward;
			var across = at( index0 ).Position - at( pinky0 ).Position;
			// Curled fingers can point along the palm normal, which makes it unreliable for roll.
			var roll = transverse ? across : across.Cross( at( middle0 ).Position - at( hand ).Position );
			if ( direction.LengthSquared < 0.0001f || roll.LengthSquared < 0.0001f ) return null;
			return Rotation.LookAt( direction.Normal, roll.Normal );
		}
		for ( var i = 0; i < bones.Count; i++ )
		{
			var name = bones[i].Name;
			if ( _mapping[i] < 0 ) continue;
			var side = name[^1];
			int? next;
			var transverse = false;
			if ( name.StartsWith( "arm_lower_" ) && !name.Contains( "twist" ) ) next = Bone( $"hand_{side}" );
			else if ( name.StartsWith( "hand_" ) ) next = Bone( $"finger_middle_0_{side}" );
			else if ( name.StartsWith( "finger_" ) && !name.Contains( "_meta_" ) )
			{
				var joint = name[^3] - '0';
				var stem = name[..^4];
				next = joint < 2 ? Bone( $"{stem}_{joint + 1}_{side}" ) : null;
				transverse = !name.StartsWith( "finger_thumb_" );
			}
			else continue;
			int hand = Bone( $"hand_{side}" ), index0 = Bone( $"finger_index_0_{side}" ), middle0 = Bone( $"finger_middle_0_{side}" ), pinky0 = Bone( $"finger_pinky_0_{side}" );
			var target = Frame( i, next, hand, index0, middle0, pinky0, transverse, b => bones[b].LocalTransform );
			var source = Frame( _mapping[i], next.HasValue ? Mapped( next.Value ) : null, Mapped( hand ), Mapped( index0 ), Mapped( middle0 ), Mapped( pinky0 ), transverse, SourceBind );
			if ( target is null || source is null ) continue;
			_axes[i] = SourceBind( _mapping[i] ).Rotation.Inverse * source.Value * target.Value.Inverse * bones[i].LocalTransform.Rotation;
		}
	}

	// Retarget before the viewmodel lens stretches the rendered pose.
	Clip Retarget( Clip source )
	{
		if ( _clips.Clips.TryGetValue( source, out var result ) ) return result;
		var builder = new ClipBuilder { Skeleton = _clips.Skeleton, FrameRate = source.FrameCount == 1 ? 1 : source.FrameRate, IsAdditive = source.IsAdditive };
		var target = new Transform[_pose.Length];
		var bones = source.Skeleton.Bones;
		var indices = bones.Select( bone => _sourceModel.Bones.GetBone( bone.Name )?.Index ?? -1 ).ToArray();
		var sourcePose = new Transform[bones.Count];
		foreach ( var bone in _sourceModel.Bones.AllBones ) _sourcePose[bone.Index] = SourceBind( bone.Index );
		Transform ParentLocal( int i ) => _parents[i] < 0 ? _pose[i] : !_supported[_parents[i]] ? _local[i] : _pose[_parents[i]].ToLocal( _pose[i] );
		// A delta must be measured against the same fitted grip as the animation.
		Transform[] reference = null;
		if ( source.IsAdditive )
		{
			RetargetPose();
			reference = Enumerable.Range( 0, target.Length ).Select( ParentLocal ).ToArray();
		}
		for ( var frame = 0; frame < source.FrameCount; frame++ )
		{
			var cycle = frame / (float)Math.Max( 1, source.FrameCount - 1 );
			foreach ( var bone in bones )
			{
				var local = source.GetLocalTransform( cycle, bone.Index );
				if ( source.IsAdditive )
				{
					var bind = bone.LocalTransform;
					local = new Transform( bind.Position + local.Position, bind.Rotation * local.Rotation, bind.Scale + local.Scale );
				}
				var pose = bone.ParentIndex < 0 ? local : sourcePose[bone.ParentIndex].ToWorld( local );
				sourcePose[bone.Index] = pose;
				if ( indices[bone.Index] >= 0 ) _sourcePose[indices[bone.Index]] = _mirrored ? Mirror( pose ) : pose;
			}
			RetargetPose();
			for ( var i = 0; i < target.Length; i++ )
			{
				var local = ParentLocal( i );
				target[i] = reference is null ? local : new Transform( local.Position - reference[i].Position,
					reference[i].Rotation.Inverse * local.Rotation, local.Scale - reference[i].Scale );
			}
			builder.AddFrame( target );
		}
		return _clips.Clips[source] = builder.Create();
	}

	int FindAnchor( Transform? recovered, Model reference, string side )
	{
		if ( !recovered.HasValue ) return -1;
		var hand = recovered.Value.PointToWorld( reference.Bones.GetBone( $"ValveBiped.Bip01_{side}_Hand" ).LocalTransform.Position );
		// Collapsed hands move with the nearby weapon/handle control. Humanoid
		// bones belong to the other, still articulated hand and are not anchors.
		return _sourceModel.Bones.AllBones.Where( b => !b.Name.Contains( "Bip01" ) )
			.OrderBy( b => b.LocalTransform.Position.DistanceSquared( hand ) ).FirstOrDefault()?.Index ?? -1;
	}

	void RetargetPose()
	{
		for ( var i = 0; i < _pose.Length; i++ )
		{
			Transform? mapped = _mapping[i] >= 0 ? _sourcePose[_mapping[i]]
				: _anchors[i] >= 0 && _gripPose[i].HasValue ? _sourcePose[_anchors[i]].ToWorld( _gripPose[i].Value ) : null;
			_mapped[i] = mapped.HasValue ? mapped.Value.WithRotation( mapped.Value.Rotation * _axes[i] ) : null;
			if ( _palmOffsets?[i] is { } palm && _mapped[i] is { } wrist ) _mapped[i] = wrist.ToWorld( palm ).WithScale( wrist.Scale );
		}
		// A rig without upper arms (Counter-Strike: Source's) still drives our elbow: continue the forearm's line back.
		for ( var i = 0; i < _pose.Length; i++ )
		{
			var (lower, hand) = _upperArms[i];
			if ( lower < 0 || _mapped[i] is not null || _mapped[lower] is not { } elbow || _mapped[hand] is not { } wristPose ) continue;
			var back = (elbow.Position - wristPose.Position).Normal;
			_mapped[i] = elbow.WithPosition( elbow.Position + back * _local[lower].Position.Length );
		}
		// Source viewmodels use exaggerated elbow poses for their own low-poly arms.
		// Keep the weapon's hand contact, but relax extreme wrist bends on our arms.
		for ( var i = 0; i < _pose.Length; i++ )
		{
			var arm = _arms[i];
			if ( arm.End < 0 || arm.End != arm.Hand || _mapped[i] is not { } elbow || _mapped[arm.Hand] is not { } hand ) continue;
			var direction = (hand.Position - elbow.Position).Normal;
			var forward = hand.Rotation.Forward;
			var bend = (1 - direction.Dot( forward )).Clamp( 0, 1 );
			direction = Vector3.Lerp( direction, forward, bend * 0.65f ).Normal;
			_mapped[i] = elbow.WithPosition( hand.Position - direction * _local[arm.Hand].Position.Length );
		}
		for ( var i = 0; i < _pose.Length; i++ )
		{
			var pose = _parents[i] < 0 ? _local[i] : _pose[_parents[i]].ToWorld( _local[i] );
			if ( _mapped[i] is { } mapped )
			{
				// Transfer the curl while keeping the replacement fingers attached at their own joint lengths.
				pose = _fingers[i] && (!_contacts[i] || _parents[i] >= 0 && _contacts[_parents[i]] && _fingers[_parents[i]]) ? pose.WithRotation( mapped.Rotation ) : mapped;
				var arm = _arms[i];
				if ( arm.End >= 0 && _mapped[arm.End] is { } end && _mapped[arm.Hand] is { } hand )
					pose = pose.WithRotation( Rotation.LookAt( end.Position - pose.Position, hand.Rotation.Up ) );
			}
			// Source has no separate metacarpals. Aim our palm bones and any
			// unrecovered finger segment at its next known contact, instead of
			// leaving it in the replacement mesh's open-hand bind orientation.
			var knuckle = _aimChild[i];
			if ( knuckle >= 0 && _mapped[knuckle] is { } contact && _contacts[knuckle] )
			{
				var from = pose.Rotation * _local[knuckle].Position;
				var to = contact.Position - pose.Position;
				if ( from.LengthSquared > 0.0001f && to.LengthSquared > 0.0001f )
					pose = pose.WithRotation( Rotation.FromToRotation( from.Normal, to.Normal ) * pose.Rotation );
			}
			var twist = _twists[i];
			if ( twist.Lower >= 0 )
				pose = new Transform( Vector3.Lerp( _pose[twist.Lower].Position, _pose[twist.Hand].Position, twist.Weight ),
					Rotation.Slerp( _pose[twist.Lower].Rotation, _pose[twist.Hand].Rotation, twist.Weight ) );
			// Match the hands actually present in the mounted viewmodel. An absent
			// arm has no authored grip or animation and must not be synthesized.
			if ( !_supported[i] ) pose = new Transform( Vector3.Zero, Rotation.Identity, Vector3.Zero );
			_pose[i] = pose;

		}
	}

	internal void Animate( SourceAnimationPlayer weapon, (string Name, bool Loop)[] choices )
	{
		if ( _loading || _player is null || !_hands.IsValid() || !_hands.SceneModel.IsValid() ) return;
		if ( !_player.IsReady && !_player.Ensure( _hands, choices, weapon.Current, _sourceModel, _clips.Skeleton, Retarget ) ) return;
		weapon.ApplySequence( _hands );
		if ( _waitingForPose && !_loading )
		{
			Source.RenderOptions.Game = _renderGame;
			Source.RenderOptions.Overlay = _renderOverlay;
			_hands.RenderOptions.Game = _renderGame;
			_hands.RenderOptions.Overlay = _renderOverlay;
			_waitingForPose = false;
		}
	}

	void Release()
	{
		_player?.Dispose();
		_player = null;
		if ( Source.IsValid() && _waitingForPose && !_loading )
		{
			Source.RenderOptions.Game = _renderGame;
			Source.RenderOptions.Overlay = _renderOverlay;
			_waitingForPose = false;
		}
		if ( Source.IsValid() && _handMaterial >= 0 ) Source.Materials.SetOverride( _handMaterial, null );
		if ( _hands.IsValid() ) _hands.GameObject.Destroy();
		_handMaterial = -1;
		_pose = null;
	}

	protected override void OnDestroy() => Release();
}
