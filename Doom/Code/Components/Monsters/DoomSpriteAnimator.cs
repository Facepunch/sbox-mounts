using System;

namespace Doom;

/// <summary>
/// Shows a monster's sprite for its host-synced state, from the rotation facing the local camera (R_ProjectSprite).
/// Needs one animation per state and rotation, named by <see cref="MonsterAnimations.Name"/>.
/// </summary>
[Title( "Doom Sprite Animator" )]
[Category( "Doom" )]
[Icon( "360" )]
public sealed class DoomSpriteAnimator : Component, Component.ExecuteInEditor
{
	[Property] public SpriteRenderer Renderer { get; set; }

	/// <summary>The state being shown, such as <see cref="MonsterAnimations.See"/>.</summary>
	[Property, Sync( SyncFlags.FromHost )] public string State { get; private set; } = MonsterAnimations.Spawn;

	/// <summary>How long the current state has been playing; frames are picked from this.</summary>
	[Sync( SyncFlags.FromHost )] public TimeSince StateTime { get; private set; }

	/// <summary>Restarts a state, optionally some seconds in. Host only.</summary>
	public void Play( string state, float from = 0f )
	{
		State = state;
		StateTime = from;
	}

	/// <summary>How long a state's animation takes to play through once, in seconds.</summary>
	public float Duration( string state )
	{
		var animation = FindAnimation( MonsterAnimations.Name( state, 1 ) );
		return animation is null || animation.FrameRate <= 0 ? 0f : animation.Frames.Count / animation.FrameRate;
	}

	protected override void OnStart()
	{
		Renderer ??= GetComponent<SpriteRenderer>();

		// Frames are chosen here from the state's time, not advanced by the renderer.
		if ( Renderer.IsValid() )
			Renderer.PlaybackSpeed = 0f;
	}


	protected override void OnPreRender()
	{
		if ( !Renderer.IsValid() || Renderer.Sprite is null ) return;

		int index = Renderer.Sprite.GetAnimationIndex( MonsterAnimations.Name( State, ViewRotation() ) );
		if ( index < 0 ) return;

		Renderer.PlayAnimation( index );

		var animation = Renderer.CurrentAnimation;
		int count = animation.Frames.Count;
		if ( count == 0 ) return;

		int frame = (int)(StateTime * animation.FrameRate);
		frame = animation.LoopMode == Sprite.LoopMode.Loop ? frame % count : Math.Min( frame, count - 1 );

		if ( Renderer.CurrentFrameIndex != frame )
			Renderer.CurrentFrameIndex = frame;
	}

	// Rotation the camera sees, 1 (facing it) to 8.
	private int ViewRotation()
	{
		var camera = Scene.Camera;
		if ( !camera.IsValid() && Scene.IsEditor ) camera = Application.Editor.Camera;
		if ( !camera.IsValid() ) return 1;

		var delta = WorldPosition - camera.WorldPosition;
		if ( delta.WithZ( 0 ).IsNearlyZero() ) return 1;

		float toThing = MathF.Atan2( delta.y, delta.x ).RadianToDegree();
		float relative = toThing - WorldRotation.Yaw() + 180f + 22.5f;
		relative = ((relative % 360f) + 360f) % 360f;

		return (int)(relative / 45f) % 8 + 1;
	}

	private Sprite.Animation FindAnimation( string name )
	{
		var sprite = Renderer.IsValid() ? Renderer.Sprite : null;
		if ( sprite is null ) return null;

		int index = sprite.GetAnimationIndex( name );
		return index < 0 ? null : sprite.GetAnimation( index );
	}
}
