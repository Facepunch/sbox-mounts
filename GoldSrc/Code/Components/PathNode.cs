using Sandbox;

namespace GoldSrc.Maps;

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

		var train = activator.IsValid() ? activator.GetComponent<TrackTrain>() : null;
		if ( train.IsValid() ) train.FirePathOutput( OnPass );
		else Fire( OnPass, activator );
	}

	public void DeadEnd( GameObject activator )
	{
		var train = activator.IsValid() ? activator.GetComponent<TrackTrain>() : null;
		if ( train.IsValid() ) train.FirePathOutput( OnDeadEnd );
		else Fire( OnDeadEnd, activator );
	}

	public override void ChangeTarget( string target )
	{
		Target = target;
		if ( !IsTrack ) Next = Map.IsValid() ? Map.Find( target ) as PathNode : null;
	}

	public PathNode Nearest( Vector3 position )
	{
		var nearest = this;
		var distance = (WorldPosition - position).WithZ( 0f ).LengthSquared;
		var node = NextEnabled( true, false );
		for ( var count = 0; node.IsValid() && node != this; count++ )
		{
			if ( count == 9999 ) return null;
			var nextDistance = (node.WorldPosition - position).WithZ( 0f ).LengthSquared;
			if ( nextDistance < distance )
			{
				distance = nextDistance;
				nearest = node;
			}
			node = node.NextEnabled( true, false );
		}
		return nearest;
	}

	public PathNode LookAhead( ref Vector3 origin, float distance, bool move )
	{
		var current = this;
		var position = origin;
		var originalDistance = distance;
		var backwards = distance < 0f;
		if ( backwards ) distance = -distance;
		for ( var guard = 0; distance > 0f; guard++ )
		{
			if ( guard >= 10000 ) return null;
			var next = current.NextEnabled( !backwards, move );
			if ( !backwards && !next.IsValid() )
			{
				var previous = current.NextEnabled( false, false );
				if ( !move && previous.IsValid() )
					origin = current.WorldPosition + (current.WorldPosition - previous.WorldPosition).Normal * distance;
				return null;
			}
			var destination = backwards ? current.WorldPosition : next.WorldPosition;
			var delta = destination - position;
			var length = delta.Length;
			if ( backwards && length == 0f )
			{
				if ( !next.IsValid() )
				{
					var forward = current.NextEnabled( true, false );
					if ( !move && forward.IsValid() )
						origin = current.WorldPosition + (current.WorldPosition - forward.WorldPosition).Normal * distance;
					return null;
				}
				current = next;
			}
			else if ( !backwards && length == 0f && !next.NextEnabled( true, move ).IsValid() )
				return distance == originalDistance ? null : current;
			else if ( length > distance )
			{
				origin = position + delta * (distance / length);
				return current;
			}
			else
			{
				distance -= length;
				position = destination;
				origin = position;
				if ( backwards && !next.IsValid() ) return null;
				current = next;
			}
		}
		origin = position;
		return current;
	}

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
