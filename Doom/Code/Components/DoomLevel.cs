namespace Doom;

/// <summary>
/// The root of a loaded level.
/// </summary>
[Title( "Doom Level" )]
[Category( "Doom" )]
[Icon( "map" )]
public sealed class DoomLevel : Component
{
	/// <summary>World units per map unit.</summary>
	[Property, Hide] public float Scale { get; set; } = 1f;

	/// <summary>Every sector that can move.</summary>
	[Property, Hide] public List<DoomSector> Sectors { get; set; } = [];

	[Property, Hide] public List<DoomTeleportDestination> TeleportDestinations { get; set; } = [];

	[Property, Hide] public SoundEvent DoorOpenSound { get; set; }
	[Property, Hide] public SoundEvent DoorCloseSound { get; set; }
	[Property, Hide] public SoundEvent BlazeOpenSound { get; set; }
	[Property, Hide] public SoundEvent BlazeCloseSound { get; set; }
	[Property, Hide] public SoundEvent PlatStartSound { get; set; }
	[Property, Hide] public SoundEvent PlatStopSound { get; set; }
	[Property, Hide] public SoundEvent StoneMoveSound { get; set; }
	[Property, Hide] public SoundEvent SwitchSound { get; set; }
	[Property, Hide] public SoundEvent ExitSwitchSound { get; set; }
	[Property, Hide] public SoundEvent TeleportSound { get; set; }

	public IEnumerable<DoomSector> Tagged( int tag ) => Sectors.Where( s => s.IsValid() && s.Tag == tag );

	/// <summary>
	/// Runs a line's special. Returns true if anything started, which is what decides whether a
	/// once-only switch is used up.
	/// </summary>
	public bool Activate( DoomLine line, GameObject activator )
	{
		bool started = Run( line.Action, line, activator );
		if ( line.Secondary != SectorAction.None )
			started |= Run( line.Secondary, line, activator );
		return started;
	}

	private bool Run( SectorAction action, DoomLine line, GameObject activator )
	{
		if ( line.Trigger == LineTrigger.Door )
			return line.Back.IsValid() && line.Back.UseDoor( action, line.Repeat, activator );

		switch ( action )
		{
			case SectorAction.Teleport:
				return Teleport( line.Tag, activator );

			case SectorAction.Exit or SectorAction.SecretExit:
				Log.Info( $"Doom: {(action == SectorAction.Exit ? "exit" : "secret exit")} switch used" );
				return true;

			case SectorAction.LightTurnOn or SectorAction.LightTurnOff or SectorAction.LightStartStrobing or SectorAction.ScrollTextureLeft:
				return false;
		}

		// Crushers and perpetual lifts that were stopped resume before anything new starts.
		bool started = action switch
		{
			SectorAction.CeilingCrushAndRaise or SectorAction.CeilingFastCrushAndRaise or SectorAction.CeilingSilentCrushAndRaise
				=> Tagged( line.Tag ).Aggregate( false, ( any, s ) => s.ResumeCrusher() | any ),
			SectorAction.PlatPerpetualRaise
				=> Tagged( line.Tag ).Aggregate( false, ( any, s ) => s.ResumePlat() | any ),
			_ => false
		};

		foreach ( var sector in Tagged( line.Tag ).ToArray() )
			started |= sector.Start( action, line.Amount );

		return started;
	}

	/// <summary>EV_Teleport: to the destination in a sector with the line's tag, facing its angle.</summary>
	private bool Teleport( int tag, GameObject activator )
	{
		var destination = TeleportDestinations.FirstOrDefault( d => d.IsValid() && d.SectorTag == tag );
		if ( !destination.IsValid() || !activator.IsValid() ) return false;

		if ( TeleportSound is not null )
		{
			Sound.Play( TeleportSound, activator.WorldPosition );
			Sound.Play( TeleportSound, destination.WorldPosition );
		}

		var player = activator.GetComponentInParent<PlayerController>();
		var subject = player.IsValid() ? player.GameObject : activator;

		subject.WorldPosition = destination.WorldPosition;
		subject.WorldRotation = destination.WorldRotation;

		if ( player.IsValid() )
		{
			player.EyeAngles = destination.WorldRotation.Angles();
			if ( player.Body.IsValid() ) player.Body.Velocity = Vector3.Zero;
		}

		subject.Transform.ClearInterpolation();
		return true;
	}
}

/// <summary>A teleport destination thing (type 14). Teleport lines find it by the tag of the sector it stands in.</summary>
[Title( "Doom Teleport Destination" )]
[Category( "Doom" )]
[Icon( "place" )]
public sealed class DoomTeleportDestination : Component
{
	[Property] public int SectorTag { get; set; }
}
