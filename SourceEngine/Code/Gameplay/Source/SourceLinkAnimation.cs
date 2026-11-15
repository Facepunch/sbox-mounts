using System;
using Sandbox.Engine.Resources;
using Sandbox.Npcs.Layers;

namespace Source1;

/// <summary>
/// A mounted model's own jump, glide, land and climb activities, played while its NPC crosses a nav link.
/// A model without them keeps its usual animation.
/// </summary>
public sealed class SourceLinkAnimation
{
	const string Jump = "jump", Drop = "drop", Ladder = "ladder", Climb = "climb";

	readonly string _jump, _glide, _land, _climbUp, _climbDown, _dismount;
	(string Type, int Direction) _motion = ("", 0);
	bool _vertical, _climbed, _landing;

	public SourceLinkAnimation( SourceModelSequences sequences )
	{
		_jump = sequences.FindActivity( "ACT_JUMP" );
		_glide = sequences.FindActivity( "ACT_GLIDE" );
		_land = sequences.FindActivity( "ACT_LAND" );
		_climbUp = sequences.FindActivity( "ACT_CLIMB_UP" );
		_climbDown = sequences.FindActivity( "ACT_CLIMB_DOWN", _climbUp );
		_dismount = sequences.FindActivity( "ACT_CLIMB_DISMOUNT" );
	}

	static string Kind( NavMeshLinkType type )
	{
		if ( type is null ) return "";
		if ( type == NavMeshLinkType.Jump ) return Jump;
		if ( type == NavMeshLinkType.Drop ) return Drop;
		if ( type == NavMeshLinkType.Ladder ) return Ladder;
		if ( type == NavMeshLinkType.Climb ) return Climb;
		return "";
	}

	public IEnumerable<(string Name, bool Loop)> Choices => [(_jump, false), (_glide, true), (_land, false), (_climbUp, true), (_climbDown, true), (_dismount, false)];

	/// <summary>The clip to play this frame for the synced traversal, or the landing after a jump or drop. Null leaves the usual animation.</summary>
	public string Clip( AnimationLayer layer, SourceAnimationPlayer player, out bool restart )
	{
		(string Type, int Direction) motion = layer.IsValid() ? (Kind( layer.Traversal ), layer.TraversalDirection) : ("", 0);
		var climbing = motion.Type is Ladder or Climb;
		// A climb link goes up or down the face, then steps off or on at the upper height.
		var vertical = climbing && MathF.Abs( layer.MoveVelocity.z ) > 1;
		var entered = motion != _motion;

		if ( entered )
		{
			_landing = motion.Type == "" && _motion.Type is Jump or Drop && _land is not null;
			_climbed = false;

			// Source stops the NPC for its landing
			if ( _landing && layer.IsValid() && !layer.IsProxy )
				layer.GetComponent<NavigationLayer>()?.HoldMovement( player.ClipDuration( _land ) );
		}
		else if ( _landing && player.Current == _land && player.Finished )
		{
			_landing = false;
		}

		restart = entered || vertical != _vertical;
		_motion = motion;
		_vertical = vertical;
		_climbed |= vertical;

		return motion.Type switch
		{
			Jump => _jump is not null && (entered || player.Current == _jump && !player.Finished) ? _jump : _glide ?? _jump,
			Drop => _glide ?? _jump,
			Ladder or Climb when motion.Direction > 0 => _climbUp is null ? null : vertical || !_climbed ? _climbUp : _dismount,
			Ladder or Climb => vertical ? _climbDown : null,
			_ => _landing ? _land : null
		};
	}
}
