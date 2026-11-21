namespace IdTech3;

/// <summary>Something that reacts to being targeted ("target" / "targetname").</summary>
public interface IId3Usable
{
	void Use( GameObject activator );
}

/// <summary>Holds the idTech 3 entity name so triggers and buttons can find their targets.</summary>
[Title( "idTech3 Entity" ), Category( "idTech3" ), Icon( "label" )]
public sealed class Id3Entity : Component
{
	[Property] public string ClassName { get; set; }
	[Property] public string TargetName { get; set; }
	[Property] public string Target { get; set; }

	/// <summary>Use every entity named <paramref name="target"/>.</summary>
	public static void FireTargets( Scene scene, string target, GameObject activator )
	{
		if ( scene is null || string.IsNullOrEmpty( target ) ) return;
		foreach ( var e in scene.GetAllComponents<Id3Entity>() )
		{
			if ( !string.Equals( e.TargetName, target, StringComparison.OrdinalIgnoreCase ) ) continue;
			foreach ( var usable in e.GameObject.Components.GetAll<IId3Usable>() )
				usable.Use( activator );
		}
	}

	public static GameObject FindTarget( Scene scene, string target )
	{
		if ( scene is null || string.IsNullOrEmpty( target ) ) return null;
		return scene.GetAllComponents<Id3Entity>()
			.FirstOrDefault( e => string.Equals( e.TargetName, target, StringComparison.OrdinalIgnoreCase ) )?.GameObject;
	}

	public static PlayerController PlayerOf( Collider other )
	{
		if ( !other.IsValid() ) return null;
		return other.GameObject.Components.GetInAncestorsOrSelf<PlayerController>();
	}
}

public enum LiquidType { Water, Slime, Lava }

/// <summary>Water, slime and lava volumes: buoyancy/drag for players, damage for slime and lava.</summary>
[Title( "idTech3 Liquid" ), Category( "idTech3" ), Icon( "water" )]
public sealed class Liquid : Component, Component.ITriggerListener
{
	[Property] public LiquidType Type { get; set; }

	readonly HashSet<PlayerController> _inside = [];
	TimeSince _damage;

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( Id3Entity.PlayerOf( other ) is { } pc ) _inside.Add( pc );
	}

	void ITriggerListener.OnTriggerExit( Collider other )
	{
		if ( Id3Entity.PlayerOf( other ) is { } pc ) _inside.Remove( pc );
	}

	protected override void OnFixedUpdate()
	{
		_inside.RemoveWhere( x => !x.IsValid() );
		bool hurt = _damage > 1f;
		if ( hurt ) _damage = 0;

		foreach ( var pc in _inside )
		{
			if ( pc.IsProxy || !pc.Body.IsValid() ) continue;
			var body = pc.Body;

			// swimming: cancel most of gravity, add drag, rise while holding jump, sink while ducking
			var v = body.Velocity;
			float gravity = Scene.PhysicsWorld.Gravity.Length;
			v += Vector3.Up * gravity * 0.85f * Time.Delta;
			v *= 1f - MathF.Min( 1f, 3f * Time.Delta );
			if ( Input.Down( "jump" ) ) v.z = MathF.Max( v.z, 200f );
			else if ( Input.Down( "duck" ) ) v.z = MathF.Min( v.z, -150f );
			body.Velocity = v;
			pc.PreventGrounding( 0.1f );

			if ( hurt && Type != LiquidType.Water )
			{
				float damage = Type == LiquidType.Lava ? 30f : 10f;
				foreach ( var d in pc.GameObject.Components.GetAll<Component.IDamageable>( FindMode.EverythingInSelfAndAncestors ) )
					d.OnDamage( new DamageInfo( damage, GameObject, GameObject ) );
			}
		}
	}
}

/// <summary>trigger_teleport: sends players to misc_teleporter_dest / target_position.</summary>
[Title( "idTech3 Teleporter" ), Category( "idTech3" ), Icon( "shuffle" )]
public sealed class Teleporter : Component, Component.ITriggerListener
{
	[Property] public Vector3 Destination { get; set; }
	[Property] public float DestinationYaw { get; set; }
	[Property] public SoundEvent Sound { get; set; }

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		var pc = Id3Entity.PlayerOf( other );
		if ( pc is null || pc.IsProxy ) return;

		var rot = Rotation.FromYaw( DestinationYaw );
		pc.WorldPosition = Destination;
		pc.Transform.ClearInterpolation();
		pc.EyeAngles = new Angles( 0, DestinationYaw, 0 );
		if ( pc.Body.IsValid() ) pc.Body.Velocity = rot.Forward * 400f;
		if ( Sound is not null ) Sandbox.Sound.Play( Sound, Destination );
	}
}

/// <summary>trigger_push / target_push: launches players towards a target position (jump pads).</summary>
[Title( "idTech3 Jump Pad" ), Category( "idTech3" ), Icon( "keyboard_double_arrow_up" )]
public sealed class JumpPad : Component, Component.ITriggerListener
{
	/// <summary>Apex position. When <see cref="UseTarget"/> is false, <see cref="Velocity"/> is used as is.</summary>
	[Property] public Vector3 Target { get; set; }
	[Property] public bool UseTarget { get; set; } = true;
	[Property] public Vector3 Velocity { get; set; }
	[Property] public SoundEvent Sound { get; set; }

	TimeSince _lastSound;

	/// <summary>AimAtTarget from g_trigger.c, using the scene's gravity.</summary>
	public Vector3 LaunchVelocity( Vector3 from )
	{
		if ( !UseTarget ) return Velocity;
		float gravity = Scene.IsValid() ? Scene.PhysicsWorld.Gravity.Length : 800f;
		if ( gravity <= 0 ) gravity = 800f;

		float height = Target.z - from.z;
		if ( height <= 0 ) return (Target - from).Normal * 400f;
		float time = MathF.Sqrt( height / (0.5f * gravity) );
		if ( time <= 0 ) return Vector3.Zero;

		var horizontal = (Target - from).WithZ( 0 );
		float dist = horizontal.Length;
		var dir = dist > 0.01f ? horizontal / dist : Vector3.Zero;
		var v = dir * (dist / time);
		v.z = time * gravity;
		return v;
	}

	readonly HashSet<Collider> _touching = [];

	void ITriggerListener.OnTriggerEnter( Collider other ) { _touching.Add( other ); Push( other ); }
	void ITriggerListener.OnTriggerExit( Collider other ) => _touching.Remove( other );

	protected override void OnFixedUpdate()
	{
		_touching.RemoveWhere( x => !x.IsValid() );
		foreach ( var c in _touching.ToArray() ) Push( c );
	}

	void Push( Collider other )
	{
		var pc = Id3Entity.PlayerOf( other );
		if ( pc is null || pc.IsProxy || !pc.Body.IsValid() ) return;

		var velocity = LaunchVelocity( pc.WorldPosition );
		if ( velocity.LengthSquared < 1 ) return;
		pc.PreventGrounding( 0.3f );
		pc.Body.Velocity = velocity;

		if ( Sound is not null && _lastSound > 0.5f )
		{
			_lastSound = 0;
			Sandbox.Sound.Play( Sound, WorldPosition );
		}
	}
}

/// <summary>trigger_multiple / trigger_once: fires its targets when a player enters.</summary>
[Title( "idTech3 Trigger" ), Category( "idTech3" ), Icon( "sensors" )]
public sealed class TriggerRelay : Component, Component.ITriggerListener
{
	[Property] public string Target { get; set; }
	[Property] public float Wait { get; set; } = 0.5f;
	[Property] public bool Once { get; set; }

	TimeSince _last = 100;
	bool _fired;

	readonly HashSet<Collider> _touching = [];

	void ITriggerListener.OnTriggerEnter( Collider other ) { _touching.Add( other ); Touch( other ); }
	void ITriggerListener.OnTriggerExit( Collider other ) => _touching.Remove( other );

	protected override void OnFixedUpdate()
	{
		_touching.RemoveWhere( x => !x.IsValid() );
		foreach ( var c in _touching.ToArray() ) Touch( c );
	}

	void Touch( Collider other )
	{
		if ( Once && _fired ) return;
		if ( _last < Wait ) return;
		if ( Id3Entity.PlayerOf( other ) is not { } pc ) return;
		_last = 0;
		_fired = true;
		Id3Entity.FireTargets( Scene, Target, pc.GameObject );
	}
}
