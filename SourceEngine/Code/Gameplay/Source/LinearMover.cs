using System;
using System.Globalization;

namespace Source1;

/// <summary>A brush that travels between two authored positions at a constant speed.</summary>
[Hide]
public class LinearMover : MovingBrush, IMapInput
{
	[Property] public Vector3 Closed { get; set; }
	[Property] public Vector3 Travel { get; set; }
	[Property] public float BlockDamage { get; set; }
	[Property] public float Speed { get; set; }
	[Property] public SoundEvent StartSound { get; set; }
	[Property] public SoundEvent StopSound { get; set; }
	[Property] public Doo OnFullyOpen { get; set; }
	[Property] public Doo OnFullyClosed { get; set; }

	Collider[] _colliders;
	float? _destination;
	SoundHandle _sound;

	internal void Rebase( Transform frame )
	{
		Travel = frame.PointToWorld( Travel ) - frame.Position;
		Closed = frame.PointToWorld( Closed );
	}

	public void SetPosition( float fraction )
	{
		if ( !Networking.IsHost || !float.IsFinite( fraction ) || Travel.LengthSquared == 0 ) return;
		if ( (Closed + Travel * fraction).DistanceSquared( LocalPosition ) <= 0.000001f ) return;
		if ( !_destination.HasValue ) PlayMovementSound( true );
		_destination = fraction;
	}

	public void SetSpeed( float speed )
	{
		if ( Networking.IsHost && float.IsFinite( speed ) ) Speed = Math.Max( 0, speed );
	}

	protected override void OnFixedUpdate()
	{
		if ( _destination is not { } fraction || Speed <= 0 ) return;
		var end = Closed + Travel * fraction;
		var delta = end - LocalPosition;
		var step = Speed * Time.Delta;
		var arrived = delta.LengthSquared <= step * step;
		var next = arrived ? end : LocalPosition + delta.Normal * step;
		var world = GameObject.Parent.WorldTransform.ToWorld( LocalTransform.WithPosition( next ) );
		_colliders ??= GetComponentsInChildren<PhysicsCollider>( true ).ToArray();
		foreach ( var collider in _colliders )
		{
			var blocker = FindBlocker( collider, world );
			if ( !blocker.IsValid() ) continue;
			if ( BlockDamage > 0 )
				Crush( blocker, BlockDamage, collider.FindClosestPoint( blocker.WorldPosition ) );
			return;
		}
		LocalPosition = next;
		if ( !arrived ) return;
		_destination = null;
		PlayMovementSound( false );
		if ( fraction == 1 ) RunDoo( OnFullyOpen, c => c.SetArgument( "user", GameObject ) );
		if ( fraction == 0 ) RunDoo( OnFullyClosed, c => c.SetArgument( "user", GameObject ) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayMovementSound( bool moving )
	{
		_sound?.Stop();
		if ( (moving ? StartSound : StopSound) is { } sound ) _sound = GameObject.PlaySound( sound );
	}

	protected override void OnDestroy() => _sound?.Stop();

	Doo.Block IMapInput.BuildInput( string input, string parameter, bool hasUser ) => BuildInput( input, parameter );

	internal virtual Doo.Block BuildInput( string input, string parameter )
	{
		var name = input.ToLowerInvariant();
		var value = 0f;
		switch ( name )
		{
			case "open": value = 1; break;
			case "close": break;
			case "setspeed":
			case "setposition":
				if ( !float.TryParse( parameter, NumberStyles.Float, CultureInfo.InvariantCulture, out value ) || !float.IsFinite( value ) ) return null;
				break;
			default: return null;
		}
		return Input( name == "setspeed" ? nameof( SetSpeed ) : nameof( SetPosition ), value );
	}

	protected Doo.Block Input( string method, Variant value )
	{
		return new Doo.InvokeBlock
		{
			InvokeType = Doo.InvokeType.Member,
			TargetComponent = new Doo.TargetComponent { Type = Doo.TargetComponent.TargetType.Direct, ComponentValue = this },
			Member = $"{GetType().FullName}.{method}",
			Arguments = [new Doo.LiteralExpression { LiteralValue = value }]
		};
	}
}
