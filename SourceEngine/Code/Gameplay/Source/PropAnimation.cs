using System;

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
		if ( !model.IsValid() ) return;
		var graph = model.AnimationClips().SequenceGraph( ModelSequences.Of( model ).Get( sequence ) );
		if ( graph is null ) return;
		if ( previous == sequence ) renderer.Animation.Graph = null;
		renderer.Animation.Graph = graph;
		renderer.UseAnimGraph = true;
	}
}
