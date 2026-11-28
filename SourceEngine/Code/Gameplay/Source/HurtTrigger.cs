using System;
using System.Globalization;

namespace Source1;

[Hide]
public sealed class HurtTrigger : PlayerTrigger
{
	[Property] public float Damage { get; set; }
	[Property] public float Cap { get; set; }
	[Property] public bool Doubles { get; set; }
	[Property] public TagSet DamageTypes { get; set; } = new();
	[Property] public Vector3 LocalCenter { get; set; }
	[Property] public Doo OnHurt { get; set; }
	[Property] public Doo OnHurtPlayer { get; set; }

	readonly HashSet<GameObject> _hurt = [];
	readonly List<GameObject> _victims = [];
	TimeUntil _nextHit, _reset;
	float _current;

	protected override void OnStart() => _current = Damage;

	internal override Doo.Block BuildInput( string input, string parameter )
	{
		if ( input.Equals( "SetDamage", StringComparison.OrdinalIgnoreCase )
			&& float.TryParse( parameter, NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) && float.IsFinite( value ) )
			return this.Call( nameof( SetDamage ), new Doo.LiteralExpression { LiteralValue = value } );
		return base.BuildInput( input, parameter );
	}

	public void SetDamage( float value ) => _current = value;
	protected override void ActivateActor( GameObject actor ) { }

	protected override void ActorLeft( GameObject actor )
	{
		if ( !_hurt.Contains( actor ) ) Hurt( actor );
		if ( Occupants.Count == 0 ) _hurt.Clear();
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || Disabled || Occupants.Count == 0 || _nextHit > 0 ) return;
		if ( Doubles && _reset <= 0 ) _current = Damage;
		_nextHit = 0.5f;
		_hurt.Clear();
		_victims.AddRange( Occupants );
		foreach ( var actor in _victims )
			if ( Occupants.Contains( actor ) ) Hurt( actor );
		_victims.Clear();

		if ( Doubles && _hurt.Count > 0 )
		{
			_current = Math.Min( _current * 2, Cap );
			_reset = 3;
		}
	}

	void Hurt( GameObject actor )
	{
		if ( !actor.IsValid() || actor.GetComponent<IDamageable>() is not { } target ) return;
		_hurt.Add( actor );
		var output = actor.GetComponent<PlayerController>().IsValid() ? OnHurtPlayer : OnHurt;
		if ( _current < 0 )
		{
			if ( Npcs.HealthOf( actor ) is { } health )
				health.Health = Math.Min( health.MaxHealth, health.Health - _current * 0.5f );
		}
		else
		{
			var origin = WorldTransform.PointToWorld( LocalCenter );
			var hit = new DamageInfo( _current * 0.5f, GameObject, GameObject )
			{
				Origin = origin, Position = actor.GetBounds().ClosestPoint( origin )
			};
			foreach ( var tag in DamageTypes ) hit.Tags.Add( tag );
			target.OnDamage( hit );
		}
		Fire( output, actor );
	}

	static readonly string[] DamageTagNames = ["crush", "bullet", "slash", "burn", "vehicle", "fall", "explosion", "melee", "shock", "sonic", "energybeam", "prevent_physics_force", "nevergib", "alwaysgib", "drown", "paralyze", "nervegas", "poison", "radiation", "drownrecover", "acid", "slowburn", "removenoragdoll", "physgun", "plasma", "airboat", "dissolve", "blast_surface", "direct", "buckshot"];

	internal static TagSet ReadTags( long mask )
	{
		var tags = new TagSet();
		for ( var bit = 0; bit < DamageTagNames.Length; bit++ )
			if ( (mask & (1L << bit)) != 0 ) tags.Add( DamageTagNames[bit] );
		return tags;
	}
}
