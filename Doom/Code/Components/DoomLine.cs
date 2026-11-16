namespace Doom;

/// <summary>
/// A linedef with a special.
/// </summary>
[Title( "Doom Line" )]
[Category( "Doom" )]
[Icon( "linear_scale" )]
public sealed class DoomLine : Component, Component.IPressable, Component.IDamageable
{
	[Property, Hide] public DoomLevel Level { get; set; }

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
			PlaySound( Level?.SwitchSound );
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
		PlaySound( Action is SectorAction.Exit or SectorAction.SecretExit ? Level?.ExitSwitchSound : Level?.SwitchSound );

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

	private bool Fire( GameObject activator ) => Level.IsValid() && Level.Activate( this, activator );

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
