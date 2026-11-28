using System;

namespace Magicka;

/// <summary>
/// First-person viewmodel built from the Magicka Wizard character model: collapses every
/// non-arm bone to a tiny scale so only the wizard's arms and held weapon are visible, while
/// playing the wizard's native spell-casting and melee animations.
/// </summary>
[Hide]
public sealed class MagickaWeaponView : BaseWeaponModel
{
	private const float HiddenBoneScale = 0.001f;

	private static readonly HashSet<string> VisibleBones = new( StringComparer.OrdinalIgnoreCase )
	{
		"LeftArm",
		"LeftForeArm",
		"LeftHand",
		"LeftAttach",
		"RightArm",
		"RightForeArm",
		"RightHand",
		"RightAttach",
	};

	private BoneCollection.Bone[] _bones;
	private bool[] _keepBone;
	private int _attachBoneIndex = -1;
	private GameObject _heldObject;
	private string _idleSequence = "idle";
	private string _currentSequence;
	private bool _looping = true;
	private float _sequenceDuration;
	private TimeSince _sequenceElapsed;
	private TimeUntil _channelHold;
	private bool _channeling;

	/// <summary>Offset of the wizard viewmodel in camera-local space.</summary>
	public Vector3 ViewOffset { get; set; } = new( 7f, 2f, -36f );

	internal void Initialize( string weaponModelPath, string attachBone, string idleSequence, string deploySequence = null )
	{
		_idleSequence = string.IsNullOrEmpty( idleSequence ) ? "idle" : idleSequence;

		if ( Renderer.IsValid() && Renderer.Model.IsValid() )
		{
			Renderer.UseAnimGraph = false;
			Renderer.CreateBoneObjects = false;
			Renderer.CreateAttachments = false;

			var bones = Renderer.Model.Bones.AllBones;
			_bones = new BoneCollection.Bone[bones.Count];
			_keepBone = new bool[bones.Count];
			for ( var i = 0; i < bones.Count; i++ )
			{
				_bones[i] = bones[i];
				var name = bones[i].Name;
				_keepBone[i] = VisibleBones.Contains( name );
				if ( string.Equals( name, attachBone, StringComparison.OrdinalIgnoreCase ) )
					_attachBoneIndex = i;
			}

			AttachWeaponModel( weaponModelPath );
		}

		if ( !string.IsNullOrEmpty( deploySequence ) && Play( deploySequence, loop: false ) )
			return;

		Play( _idleSequence, loop: true );
	}

	private void AttachWeaponModel( string weaponModelPath )
	{
		if ( string.IsNullOrEmpty( weaponModelPath ) )
			return;

		var model = Model.Load( weaponModelPath );
		if ( !model.IsValid() || model.IsError )
			return;

		_heldObject = new GameObject( GameObject, false, "held_weapon" );
		_heldObject.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		_heldObject.Tags.Add( "firstperson", "viewmodel" );

		var mr = _heldObject.AddComponent<ModelRenderer>();
		mr.Model = model;
		mr.RenderType = ModelRenderer.ShadowRenderType.Off;
		mr.RenderOptions.Game = false;
		mr.RenderOptions.Overlay = true;

		_heldObject.Enabled = true;
	}

	internal bool Play( string sequenceName, bool loop = false, float playbackRate = 1f )
	{
		if ( !Renderer.IsValid() || !Renderer.Model.IsValid() || string.IsNullOrEmpty( sequenceName ) )
			return false;

		var resolved = ResolveSequenceName( sequenceName );
		if ( resolved is null )
			return false;

		_currentSequence = resolved;
		_looping = loop;
		_channeling = false;
		_sequenceElapsed = 0;

		Renderer.Sequence.Name = resolved;
		Renderer.Sequence.Looping = loop;
		Renderer.Sequence.PlaybackRate = playbackRate;
		if ( Renderer.SceneModel.IsValid() )
			Renderer.Sequence.Time = 0f;

		var duration = Renderer.SceneModel.IsValid() ? Renderer.Sequence.Duration : 0f;
		_sequenceDuration = duration > 0.01f ? duration / MathF.Max( playbackRate, 0.01f ) : 0.6f;
		return true;
	}

	/// <summary>
	/// Plays a continuous channeling animation without restarting from frame 0 on every tick:
	/// plays the wind-up once, then holds the extended-cast pose while the button stays held.
	/// </summary>
	internal void PlayChannel( string sequenceName, float holdFor = 0.16f, float playbackRate = 1.35f )
	{
		if ( !Renderer.IsValid() || !Renderer.Model.IsValid() || string.IsNullOrEmpty( sequenceName ) )
			return;

		var resolved = ResolveSequenceName( sequenceName );
		if ( resolved is null )
			return;

		_channelHold = holdFor;
		if ( _channeling && string.Equals( _currentSequence, resolved, StringComparison.OrdinalIgnoreCase ) )
			return;

		_currentSequence = resolved;
		_looping = false;
		_channeling = true;
		_sequenceElapsed = 0;

		Renderer.Sequence.Name = resolved;
		Renderer.Sequence.Looping = false;
		Renderer.Sequence.PlaybackRate = playbackRate;
		if ( Renderer.SceneModel.IsValid() )
			Renderer.Sequence.Time = 0f;

		var duration = Renderer.SceneModel.IsValid() ? Renderer.Sequence.Duration : 0f;
		_sequenceDuration = duration > 0.01f ? duration / MathF.Max( playbackRate, 0.01f ) : 0.6f;
	}

	private string ResolveSequenceName( string desired )
	{
		var names = Renderer.Model.AnimationNames;
		if ( names is null )
			return null;

		foreach ( var name in names )
		{
			if ( string.Equals( name, desired, StringComparison.OrdinalIgnoreCase ) )
				return name;
		}

		return null;
	}

	protected override void OnUpdate()
	{
		if ( _channeling && Renderer.IsValid() && Renderer.SceneModel.IsValid() )
		{
			var rawDuration = Renderer.Sequence.Duration;
			if ( !_channelHold )
			{
				// Hold in the active extended-cast middle of the animation while firing.
				if ( rawDuration > 0.05f )
				{
					var holdPoint = rawDuration * 0.55f;
					if ( Renderer.Sequence.Time > holdPoint )
						Renderer.Sequence.Time = rawDuration * 0.48f;
				}
				return;
			}

			// Button released: let the animation finish or return to idle.
			_channeling = false;
			_sequenceElapsed = 0;
			_sequenceDuration = MathF.Max( 0.15f, (rawDuration - Renderer.Sequence.Time) / MathF.Max( Renderer.Sequence.PlaybackRate, 0.1f ) );
		}

		if ( !_looping && _currentSequence is not null && _sequenceElapsed >= _sequenceDuration )
			Play( _idleSequence, loop: true );
	}

	protected override void OnPreRender()
	{
		ApplyBoneMask();
	}

	internal void ApplyBoneMask()
	{
		if ( !Renderer.IsValid() || !Renderer.SceneModel.IsValid() || _bones is null || _keepBone is null )
			return;

		var sceneModel = Renderer.SceneModel;
		var modelToWorld = sceneModel.Transform;

		// Collapse all non-arm bones to 0.001f at a single model-local point behind the camera
		// so overrides stay locked to the viewmodel regardless of camera movement.
		var hiddenLocal = new global::Transform( new Vector3( -36f, 0f, 20f ), Rotation.Identity, HiddenBoneScale );
		var attachLocal = global::Transform.Zero;
		var hasAttach = false;

		for ( var i = 0; i < _bones.Length; i++ )
		{
			if ( _keepBone[i] )
			{
				if ( Renderer.TryGetBoneTransformAnimation( _bones[i], out var animWorld ) )
				{
					var localTx = modelToWorld.ToLocal( animWorld );
					localTx.Scale = Vector3.One;
					sceneModel.SetBoneOverride( i, in localTx );
					if ( i == _attachBoneIndex )
					{
						attachLocal = localTx;
						hasAttach = true;
					}
				}
			}
			else
			{
				sceneModel.SetBoneOverride( i, in hiddenLocal );
			}
		}

		// Position the held weapon in local space of the viewmodel so it moves with the camera
		// with zero frame lag.
		if ( _heldObject.IsValid() && hasAttach )
			_heldObject.LocalTransform = attachLocal;
	}

	protected override void OnDestroy()
	{
		if ( _heldObject.IsValid() )
			_heldObject.Destroy();

		base.OnDestroy();
	}
}
