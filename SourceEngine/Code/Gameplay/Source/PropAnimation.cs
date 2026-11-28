using System;

namespace Source1;

[Hide]
public sealed class PropAnimation : Component, IMapInput
{
	[Property, Sync( SyncFlags.FromHost ), Change( nameof( Apply ) )]
	public string Sequence { get; set; }

	[Property, Sync( SyncFlags.FromHost ), Change( nameof( ApplySkin ) )]
	public int Skin { get; set; } = -1;

	Doo.Block IMapInput.BuildInput( string input, string parameter, bool hasUser )
	{
		if ( input.Equals( "Skin", StringComparison.OrdinalIgnoreCase ) && int.TryParse( parameter, out var skin ) && skin >= 0 )
			return this.Call( nameof( SetSkin ), new Doo.LiteralExpression { LiteralValue = skin } );
		if ( input.Equals( "SetAnimation", StringComparison.OrdinalIgnoreCase ) && parameter.Length > 0 )
			return this.Call( nameof( SetAnimation ), new Doo.LiteralExpression { LiteralValue = parameter } );
		return null;
	}

	public void SetSkin( int skin ) => Skin = skin;

	void ApplySkin( int previous, int skin )
	{
		if ( skin < 0 ) return;
		var renderer = GetComponent<ModelRenderer>( true );
		var model = renderer?.Model;
		if ( model.IsValid() && skin < model.MaterialGroupCount )
			renderer.MaterialGroup = model.GetMaterialGroupName( skin );
	}

	public void SetAnimation( string sequence )
	{
		if ( Sequence == sequence ) Restart();
		else Sequence = sequence;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Restart() => Apply( Sequence, Sequence );

	protected override void OnStart()
	{
		Apply( null, Sequence );
		ApplySkin( -1, Skin );
	}

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
