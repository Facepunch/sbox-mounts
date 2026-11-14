using Sandbox;

namespace GoldSrc;

/// <summary>
/// path_track and path_corner: one node of a linked path. GoldSrc builds the list by having each
/// node "target" the next one and then walking it backwards to fill in the previous links, which
/// is what the map loader reproduces.
/// </summary>
[Title( "GoldSrc Path Node" )]
[Icon( "linear_scale" )]
public sealed class PathNode : MapEntity
{
	[Property] public bool IsTrack { get; set; } = true;
	/// <summary>The node named by this one's "target" key.</summary>
	[Property] public PathNode Next { get; set; }

	/// <summary>Whichever node targets this one. Only used when a train runs the path backwards.</summary>
	[Property] public PathNode Previous { get; set; }
	[Property] public PathNode Alternate { get; set; }
	[Property] public bool AlternateReverse { get; set; }
	[Property] public bool UseAlternate { get; set; }

	/// <summary>
	/// The "speed" key. A non-zero value becomes the train's new speed as it passes.
	/// </summary>
	[Property] public float Speed { get; set; }

	/// <summary>
	/// path_corner's "wait" key: how long a func_train pauses here.
	/// </summary>
	[Property] public float Wait { get; set; }

	/// <summary>
	/// SF_PATH_DISABLED. Trains stop before a disabled node; they do not skip past it.
	/// </summary>
	[Property] public bool Disabled { get; set; }

	/// <summary>SF_PATH_DISABLE_TRAIN disables player control, not movement.</summary>
	[Property] public bool DisablesControls { get; set; }
	[Property] public bool WaitForTrigger { get; set; }
	[Property] public bool Teleport { get; set; }

	/// <summary>SF_PATH_FIREONCE.</summary>
	[Property] public bool FireOnce { get; set; }

	/// <summary>
	/// The "message" key, fired as the train passes. "target" only links the next node.
	/// </summary>
	[Property, Group( "Events" )] public Doo OnPass { get; set; }
	[Property, Group( "Events" )] public Doo OnDeadEnd { get; set; }

	private bool _passed;

	/// <summary>
	/// CPathTrack::Use toggles the disabled flag rather than firing anything.
	/// </summary>
	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || !IsTrack ) return;
		if ( Alternate.IsValid() ) UseAlternate = !UseAlternate;
		else Disabled = !Disabled;
	}

	public override void TurnOn( GameObject activator = null )
	{
		if ( !CanTrigger || !IsTrack ) return;
		if ( Alternate.IsValid() ) UseAlternate = false;
		else Disabled = false;
	}

	public override void TurnOff( GameObject activator = null )
	{
		if ( !CanTrigger || !IsTrack ) return;
		if ( Alternate.IsValid() ) UseAlternate = true;
		else Disabled = true;
	}

	/// <summary>
	/// Called by a train as it reaches this node.
	/// </summary>
	public void Pass( GameObject activator )
	{
		if ( FireOnce && _passed )
			return;

		_passed = true;

		Fire( OnPass, activator );
	}

	public void DeadEnd( GameObject activator ) => Fire( OnDeadEnd, activator );

	/// <summary>
	/// Resolve the selected branch. Orientation samples can ignore the disabled flag.
	/// </summary>
	public PathNode NextEnabled( bool forward, bool testDisabled = true )
	{
		var node = Alternate.IsValid() && UseAlternate && forward != AlternateReverse
			? Alternate : forward ? Next : Previous;
		return node.IsValid() && (!testDisabled || !node.Disabled) ? node : null;
	}
}
