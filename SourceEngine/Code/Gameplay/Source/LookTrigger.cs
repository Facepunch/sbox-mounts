using System;
using System.Globalization;

namespace Source1;

[Hide]
public sealed class LookTrigger : PlayerTrigger
{
	[Property] public string Target { get; set; }
	[Property] public float FieldOfView { get; set; }
	[Property] public float LookTime { get; set; }
	[Property] public float Timeout { get; set; }
	[Property] public bool Once { get; set; }
	[Property] public bool UseVelocity { get; set; }
	[Property] public Doo OnTimeout { get; set; }

	sealed class Watch( PlayerController player, float timeout )
	{
		public readonly PlayerController Player = player;
		public float Deadline = timeout == 0 ? float.PositiveInfinity : Time.Now + timeout;
		public float? LookingSince;
	}

	GameObject _target;
	readonly Dictionary<GameObject, Watch> _watching = [];
	readonly List<Watch> _tick = [];

	protected override void ActivateActor( GameObject actor )
	{
		if ( actor.GetComponent<PlayerController>() is { } player ) _watching[actor] = new( player, Timeout );
	}

	protected override void ActorLeft( GameObject actor ) => _watching.Remove( actor );

	protected override void OnFixedUpdate()
	{
		if ( _watching.Count == 0 ) return;
		if ( !_target.IsValid() && !string.IsNullOrEmpty( Target ) ) _target = Scene.Directory.FindByName( Target ).FirstOrDefault();
		_tick.AddRange( _watching.Values );
		foreach ( var watch in _tick )
		{
			if ( GameObject.IsDestroyed ) break;
			var player = watch.Player;
			if ( !player.IsValid() || !_watching.ContainsKey( player.GameObject ) ) continue;
			var timeout = Time.Now >= watch.Deadline;
			if ( timeout ) _watching.Remove( player.GameObject );
			else
			{
				if ( !_target.IsValid() ) continue;
				var eye = player.EyeTransform;
				var heading = UseVelocity ? (player.Body?.Velocity ?? Vector3.Zero).Normal : eye.Forward;
				if ( heading.Dot( (_target.WorldPosition - eye.Position).Normal ) <= FieldOfView )
				{
					watch.LookingSince = null;
					continue;
				}
				watch.LookingSince ??= Time.Now;
				if ( Time.Now - watch.LookingSince.Value < LookTime ) continue;
				watch.LookingSince = null;
				watch.Deadline = float.PositiveInfinity;
			}
			Fire( timeout ? OnTimeout : Activate, player.GameObject );
			if ( Once ) GameObject.Destroy();
		}
		_tick.Clear();
	}

	internal override Doo.Block BuildInput( string input, string parameter )
	{
		var method = input.ToLowerInvariant() switch
		{
			"fieldofview" => nameof( SetFieldOfView ),
			"looktime" => nameof( SetLookTime ),
			_ => null
		};
		if ( method is not null && float.TryParse( parameter, NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) && float.IsFinite( value ) )
			return this.Call( method, new Doo.LiteralExpression { LiteralValue = value } );
		return base.BuildInput( input, parameter );
	}

	public void SetFieldOfView( float value ) => FieldOfView = value;
	public void SetLookTime( float value ) => LookTime = value;
}
