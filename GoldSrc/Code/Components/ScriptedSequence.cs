using Sandbox;

namespace GoldSrc;

/// <summary>
/// CCineMonster - a scripted_sequence. It takes possession of the monster named by "m_iszEntity"
/// and holds it in an animation, which is how Half-Life poses its opening cast.
///
/// Only the possession is modelled. Without AI there is nothing to interrupt and no sequence ever
/// runs to completion, which is why the "target" key isn't forwarded - CCineMonster fires it from
/// SequenceDone. See MapLoader.ForwardsTarget.
/// </summary>
[Category( "GoldSrc" )]
[Icon( "movie" )]
public sealed class ScriptedSequence : MapEntity
{
	/// <summary>The monster this script possesses, from "m_iszEntity".</summary>
	[Property] public Monster Actor { get; set; }

	/// <summary>"m_iszPlay" - what the monster does once the script is triggered.</summary>
	[Property] public string PlaySequence { get; set; }

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		if ( Actor.IsValid() )
			Actor.Play( PlaySequence );
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );
}
