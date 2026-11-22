using Sandbox;

namespace GoldSrc.Maps;

/// <summary>
/// CCineMonster - a scripted_sequence. It takes possession of the monster named by "m_iszEntity"
/// and holds it in an animation, which is how Half-Life poses its opening cast.
/// </summary>
[Category( "GoldSrc" )]
[Icon( "movie" )]
public sealed class ScriptedSequence : MapEntity
{
	/// <summary>The monster this script possesses, from "m_iszEntity".</summary>
	[Property] public int MoveTo { get; set; }
	[Property] public Monster Actor { get; set; }

	/// <summary>"m_iszPlay" - what the monster does once the script is triggered.</summary>
	[Property] public string PlaySequence { get; set; }

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		if ( !Actor.IsValid() ) return;
		if ( Actor.Npc.IsValid() )
			Actor.Npc.StartScript( PlaySequence, WorldTransform, MoveTo, () => Fire( OnTrigger, activator ) );
		else
			Actor.Play( PlaySequence );
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );
}
