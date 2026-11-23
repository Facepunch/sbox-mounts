namespace NS2;

/// <summary>Plays Spark prop graphs that advance to another clip on completion.</summary>
[Hide]
public sealed class PropAnimationSequence : Component, Component.ExecuteInEditor
{
	[Property] public SkinnedModelRenderer Renderer { get; set; }
	[Property] public string[] Clips { get; set; }
	[Property] public int LoopStart { get; set; }

	int index;

	protected override void OnUpdate()
	{
		if ( Clips is not { Length: > 1 } || !Renderer.IsValid() || !Renderer.SceneModel.IsValid() ) return;
		var sequence = Renderer.SceneModel.CurrentSequence;
		if ( !sequence.IsFinished ) return;

		index = index + 1 < Clips.Length ? index + 1 : LoopStart;
		sequence.Name = Clips[index];
		sequence.TimeNormalized = 0;
	}
}
