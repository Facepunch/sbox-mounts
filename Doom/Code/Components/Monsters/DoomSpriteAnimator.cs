using System;

namespace Doom;

/// <summary>
/// Shows a monster's sprite the way Doom's renderer did (R_ProjectSprite): the frame its current
/// state is on, drawn from whichever of the eight rotations faces the camera. The state and when it
/// started are synced from the host, and every client picks the rotation for its own camera.
/// The sprite needs one animation per state and rotation, named by <see cref="MonsterAnimations.Name"/>.
/// </summary>
[Title( "Doom Sprite Animator" )]
[Category( "Doom" )]
[Icon( "360" )]
public sealed class DoomSpriteAnimator : Component, Component.ExecuteInEditor
{
	[Property] public SpriteRenderer Renderer { get; set; }

	/// <summary>The state being shown, such as <see cref="MonsterAnimations.See"/>.</summary>
	[Property, Sync( SyncFlags.FromHost )] public string State { get; private set; } = MonsterAnimations.Spawn;

	/// <summary>How long the current state has been playing. Frames are picked from this, so a rotation change keeps its place.</summary>
	[Sync( SyncFlags.FromHost )] public TimeSince StateTime { get; private set; }

	/// <summary>Starts a state from its first frame, or <paramref name="from"/> seconds in, even if it's already playing. Host only.</summary>
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

	/// <summary>
	/// Which rotation the camera sees, 1 (facing it) to 8: the angle from the camera to the thing
	/// against the way the thing faces, in 45 degree steps centred on each rotation.
	/// </summary>
	private int ViewRotation()
	{
		// The scene's own camera when it has one, such as a thumbnail's; otherwise, in the editor, the viewport's.
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
