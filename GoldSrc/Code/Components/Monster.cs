using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoldSrc;

/// <summary>
/// A GoldSrc monster.
///
/// None of the AI is simulated. What a map like Half-Life's tram ride actually needs is the model,
/// stood where the map puts it, holding whatever sequence its scripted_sequence chose - the whole
/// opening is NPCs possessed by scripts, and the player can't reach any of them.
/// </summary>
[Category( "GoldSrc" )]
[Icon( "person" )]
public sealed class Monster : MapEntity
{
	[Property] public SkinnedModelRenderer Renderer { get; set; }

	/// <summary>
	/// The looping sequence the monster sits in when nothing else is driving it - its
	/// scripted_sequence's "m_iszIdle", or the model's own idle when there's no script.
	/// </summary>
	[Property] public string IdleSequence { get; set; }

	protected override void OnStart() => Play( IdleSequence );

	/// <summary>
	/// Hold the monster in a named sequence, the way CCineMonster::StartSequence does. A model only
	/// carries the animations its own monster needed, so an unknown name falls back to an idle
	/// rather than leaving it in the bind pose.
	/// </summary>
	public void Play( string sequence )
	{
		if ( !Renderer.IsValid() || !Renderer.Model.IsValid() )
			return;

		var names = Renderer.Model.AnimationNames;

		if ( string.IsNullOrEmpty( sequence ) || !names.Contains( sequence ) )
			sequence = Idle( names );

		if ( sequence is null )
			return;

		Renderer.Sequence.Looping = true;
		Renderer.Sequence.Name = sequence;
	}

	/// <summary>
	/// An unscripted GoldSrc monster plays ACT_IDLE. Activities aren't carried through the built
	/// model, so the first idle-labelled sequence stands in - "idle1" on the humanoids, "idle01"
	/// on the G-Man, "idle2" on the forklift.
	/// </summary>
	private static string Idle( IReadOnlyList<string> names )
		=> names.FirstOrDefault( x => x.StartsWith( "idle", StringComparison.OrdinalIgnoreCase ) );
}
