using System;
using Sandbox.Animation;

namespace Source1;

[Hide]
public sealed class PropAnimation : Component, IMapInput
{
	[Property, Sync( SyncFlags.FromHost ), Change( nameof( Apply ) )]
	public string Sequence { get; set; }

	Doo.Block IMapInput.BuildInput( string input, string parameter, bool hasUser ) =>
		input.Equals( "SetAnimation", StringComparison.OrdinalIgnoreCase ) && parameter.Length > 0
			? this.Call( nameof( SetAnimation ), new Doo.LiteralExpression { LiteralValue = parameter } ) : null;

	public void SetAnimation( string sequence )
	{
		if ( Sequence == sequence ) Restart();
		else Sequence = sequence;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Restart() => Apply( Sequence, Sequence );

	protected override void OnStart() => Apply( null, Sequence );

	void Apply( string previous, string sequence )
	{
		if ( string.IsNullOrEmpty( sequence ) || GetComponent<SkinnedModelRenderer>( true ) is not {} renderer ) return;
		var model = renderer.Model;
		if ( !model.IsValid() || ModelSequences.Of( model ).Get( sequence ) is not {} metadata ) return;
		var clips = model.AnimationClips();
		if ( !clips.TryGetValue( metadata.Name, out var clip ) || clips.Skeleton is null ) return;

		var builder = Graph.Builder;
		builder.WithSkeleton( clips.Skeleton );
		renderer.Animation.Graph = builder.Create( builder.Clip( clip, new() { Looping = metadata.Looping, SampleRootMotion = false } ), metadata.Name );
		renderer.UseAnimGraph = true;
	}
}
