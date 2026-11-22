namespace Doom;

/// <summary>
/// A linedef with a special.
/// </summary>
[Title( "Doom Line" )]
[Category( "Doom" )]
[Icon( "linear_scale" )]
public sealed class DoomLine : Component, Component.IPressable, Component.IDamageable
{
	[Property, ReadOnly] public int Index { get; set; }
	[Property, ReadOnly] public int Special { get; set; }
	[Property, ReadOnly] public int Tag { get; set; }

	[Property, ReadOnly, Group( "Special" )] public LineTrigger Trigger { get; set; }
	[Property, ReadOnly, Group( "Special" )] public bool Repeat { get; set; }
	[Property, ReadOnly, Group( "Special" )] public SectorAction Action { get; set; }
	[Property, ReadOnly, Group( "Special" )] public SectorAction Secondary { get; set; }
	[Property, ReadOnly, Group( "Special" )] public int Amount { get; set; }
	[Property, ReadOnly, Group( "Special" )] public DoomKey Key { get; set; }

	/// <summary>Only fires when crossed from the front (teleporters, so you can walk back off them).</summary>
	[Property, ReadOnly, Group( "Special" )] public bool FrontOnly { get; set; }

	/// <summary>The sector behind the line: the one a door used directly opens.</summary>
	[Property, ReadOnly] public DoomSector Back { get; set; }

	/// <summary>The movable sectors with this line's tag, which its special acts on.</summary>
	public IEnumerable<DoomSector> Targets => FindTagged<DoomSector>( DoomSector.TagFor( Tag ) );

	/// <summary>Where a teleport line sends you: the first destination standing in a sector with its tag (EV_Teleport).</summary>
	public DoomTeleportDestination Destination => FindTagged<DoomTeleportDestination>( DoomTeleportDestination.TagFor( Tag ) ).FirstOrDefault();

	/// <summary>Objects with an object tag, in this line's own map only, so two maps in one scene stay apart.</summary>
	private IEnumerable<T> FindTagged<T>( string tag ) where T : Component
	{
		var root = GameObject.Root;
		return Scene.FindAllWithTag( tag )
			.Where( go => go.Root == root )
			.Select( go => go.GetComponent<T>() )
			.Where( c => c.IsValid() );
	}

	[Property, Hide] public SoundEvent SwitchSound { get; set; }
	[Property, Hide] public SoundEvent ExitSwitchSound { get; set; }
	[Property, Hide] public SoundEvent TeleportSound { get; set; }

	/// <summary>Line start, world space. The front of the line is on the right going from start to end.</summary>
	[Property, ReadOnly] public Vector3 Start { get; set; }

	[Property, ReadOnly] public Vector3 End { get; set; }

	/// <summary>The switch face on the wall, built as its own model so its texture can flip.</summary>
	[Property, ReadOnly, Group( "Switch" )] public ModelRenderer SwitchRenderer { get; set; }

	/// <summary>The other half of the switch texture pair (SW1xxx to SW2xxx or back).</summary>
	[Property, ReadOnly, Group( "Switch" )] public Material SwitchMaterial { get; set; }

	/// <summary>BUTTONTIME: a repeatable switch flips back after 35 tics.</summary>
	private const float ButtonTime = 1f;

	private TimeUntil _switchBack;
	private bool _switchPressed;

	private readonly Dictionary<GameObject, bool> _sides = [];
	private readonly List<GameObject> _seen = [];
	private Collider _collider;

	private Vector3 Normal
	{
		get
		{
			var d = (End - Start).WithZ( 0 ).Normal;
			return new Vector3( d.y, -d.x, 0 );
		}
	}

	private bool OnFront( Vector3 position ) => Vector3.Dot( (position - Start).WithZ( 0 ), Normal ) > 0;

	protected override void OnStart()
	{
		_collider = GetComponent<Collider>();
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;

		if ( _switchPressed && _switchBack )
		{
			_switchPressed = false;
			SetSwitch( false );
			PlaySound( SwitchSound );
		}

		if ( Trigger != LineTrigger.Walk || !_collider.IsValid() ) return;

		_seen.Clear();

		foreach ( var other in _collider.Touching )
		{
			var player = PlayerOf( other?.GameObject );
			if ( !player.IsValid() || _seen.Contains( player ) ) continue;

			_seen.Add( player );
			bool front = OnFront( player.WorldPosition );

			if ( _sides.TryGetValue( player, out bool wasFront ) && wasFront != front )
				Cross( player, wasFront );

			if ( !IsValid ) return;
			_sides[player] = front;
		}

		foreach ( var gone in _sides.Keys.Where( k => !_seen.Contains( k ) ).ToArray() )
			_sides.Remove( gone );
	}

	/// <summary>P_CrossSpecialLine. Walk specials are cleared as soon as they fire, whatever they did.</summary>
	private void Cross( GameObject player, bool fromFront )
	{
		if ( FrontOnly && !fromFront ) return;

		Fire( player );

		if ( !Repeat )
			GameObject.Destroy();
	}

	/// <summary>P_UseSpecialLine / P_ShootSpecialLine. Switches are only used up if they did something.</summary>
	private bool Use( GameObject user )
	{
		if ( !Fire( user ) ) return false;

		if ( Trigger != LineTrigger.Door )
			PressSwitch();

		// The switch face is its own object, so it keeps its flipped texture after this line is gone.
		if ( !Repeat )
			GameObject.Destroy();

		return true;
	}

	/// <summary>P_ChangeSwitchTexture: flip the texture and click. Repeatable switches flip back after a second.</summary>
	private void PressSwitch()
	{
		PlaySound( Action is SectorAction.Exit or SectorAction.SecretExit ? ExitSwitchSound : SwitchSound );

		if ( _switchPressed ) return;

		SetSwitch( true );

		if ( Repeat )
		{
			_switchPressed = true;
			_switchBack = ButtonTime;
		}
	}

	private void SetSwitch( bool pressed )
	{
		if ( SwitchRenderer.IsValid() && SwitchMaterial is not null )
			SwitchRenderer.MaterialOverride = pressed ? SwitchMaterial : null;
	}

	private void PlaySound( SoundEvent sound )
	{
		if ( sound is null ) return;
		Sound.Play( sound, SwitchRenderer.IsValid() ? SwitchRenderer.WorldPosition : WorldPosition );
	}

	/// <summary>
	/// Runs the special. Returns true if anything started, which is what decides whether a
	/// once-only switch is used up.
	/// </summary>
	private bool Fire( GameObject activator )
	{
		bool started = Run( Action, activator );
		if ( Secondary != SectorAction.None )
			started |= Run( Secondary, activator );
		return started;
	}

	private bool Run( SectorAction action, GameObject activator )
	{
		if ( Trigger == LineTrigger.Door )
			return Back.IsValid() && Back.UseDoor( action, Repeat, activator );

		switch ( action )
		{
			case SectorAction.Teleport:
				return Teleport( activator );

			case SectorAction.Exit or SectorAction.SecretExit:
				Log.Info( $"Doom: {(action == SectorAction.Exit ? "exit" : "secret exit")} switch used" );
				return true;

			case SectorAction.LightTurnOn or SectorAction.LightTurnOff or SectorAction.LightStartStrobing or SectorAction.ScrollTextureLeft:
				return false;
		}

		var targets = Targets.ToArray();

		// Crushers and perpetual lifts that were stopped resume before anything new starts.
		bool started = action switch
		{
			SectorAction.CeilingCrushAndRaise or SectorAction.CeilingFastCrushAndRaise or SectorAction.CeilingSilentCrushAndRaise
				=> targets.Aggregate( false, ( any, s ) => s.ResumeCrusher() | any ),
			SectorAction.PlatPerpetualRaise
				=> targets.Aggregate( false, ( any, s ) => s.ResumePlat() | any ),
			_ => false
		};

		foreach ( var sector in targets )
			started |= sector.Start( action, Amount );

		return started;
	}

	/// <summary>EV_Teleport: to the destination, facing its angle.</summary>
	private bool Teleport( GameObject activator )
	{
		var destination = Destination;
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

	private static GameObject PlayerOf( GameObject other )
	{
		if ( !other.IsValid() ) return null;

		var controller = other.GetComponentInParent<PlayerController>();
		if ( controller.IsValid() ) return controller.GameObject;

		return other.Tags.Has( "player" ) ? other : null;
	}

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e )
		=> Trigger is LineTrigger.Use or LineTrigger.Door ? new IPressable.Tooltip( "Use", "touch_app", null ) : null;

	bool IPressable.CanPress( IPressable.Event e ) => !_switchPressed && ( Trigger is LineTrigger.Use or LineTrigger.Door );

	bool IPressable.Press( IPressable.Event e )
	{
		if ( Trigger is not (LineTrigger.Use or LineTrigger.Door) ) return false;
		return Use( PlayerOf( e.Source?.GameObject ) ?? e.Source?.GameObject );
	}

	void IDamageable.OnDamage( in DamageInfo damage )
	{
		if ( Trigger == LineTrigger.Shoot )
			Use( PlayerOf( damage.Attacker ) ?? damage.Attacker );
	}
}
