using System;

namespace Source1.Hl2;

/// <summary>
/// Physics objects a player has just launched or punted with the gravity gun, by whom. The mark lasts until the object's
/// first collision, whatever it hits: an NPC that is only hurt by props a player sent at it, such as the antlion guard,
/// is told of that collision with the object's speed before and after it.
/// </summary>
static class PlayerThrows
{
	/// <summary>Host: a player launched or punted this object.</summary>
	public static void Mark( GameObject thing, GameObject player )
	{
		var body = thing?.GetComponentInParent<Rigidbody>();
		if ( !body.IsValid() || !player.IsValid() )
			return;

		var mark = body.GetComponent<ThrownMark>() ?? body.AddComponent<ThrownMark>();
		mark.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
		mark.Thrower = player;
		mark.Speed = body.Velocity.Length;
	}

	/// <summary>Host: someone else sent this object flying since, such as an NPC that swatted it.</summary>
	public static void Forget( GameObject thing )
	{
		if ( thing?.GetComponentInParent<Rigidbody>()?.GetComponent<ThrownMark>() is { } mark )
			mark.Destroy();
	}
}

/// <summary>Told when an object a player threw strikes it, with the object's speed just before and just after the hit.</summary>
interface IThrownHit
{
	void OnThrownHit( GameObject thing, float mass, GameObject thrower, float speedBefore, float speedAfter, Vector3 point );
}

/// <summary>
/// The mark on a thrown object. Its speed is read every physics step before the step runs; on its first collision the
/// speed after the hit is read on the next step (or taken as nothing if the object broke), then the mark goes.
/// </summary>
[Hide]
sealed class ThrownMark : Component, Component.ICollisionListener
{
	public GameObject Thrower { get; set; }
	public float Speed { get; set; }
	IThrownHit _struck;
	Vector3 _point;
	float _mass;
	bool _hit;

	protected override void OnFixedUpdate()
	{
		var body = GetComponent<Rigidbody>();
		if ( _hit )
		{
			Deliver( body.IsValid() ? body.Velocity.Length : 0 );
			return;
		}
		if ( body.IsValid() )
			Speed = body.Velocity.Length;
	}

	void Component.ICollisionListener.OnCollisionStart( Collision collision )
	{
		var other = collision.Other.GameObject;
		// Leaving the thrower's hands isn't a hit
		if ( _hit || !Networking.IsHost || collision.Other.IsTrigger || other.IsValid() && Thrower.IsValid() && other.IsDescendant( Thrower ) || other == Thrower )
			return;
		_hit = true;
		_struck = other?.GetComponentInParent<IThrownHit>();
		_point = collision.Contact.Point;
		var body = GetComponent<Rigidbody>();
		_mass = body.IsValid() ? MathF.Max( body.Mass, body.MassOverride ) : 0;
	}

	/// <summary>A prop that broke on the hit lost all its speed.</summary>
	protected override void OnDestroy()
	{
		if ( _hit && _struck is not null )
			Deliver( 0 );
	}

	void Deliver( float after )
	{
		var struck = _struck;
		_struck = null;
		if ( struck is Component { IsValid: true } )
			struck.OnThrownHit( GameObject, _mass, Thrower, Speed, after, _point );
		if ( IsValid && !GameObject.IsDestroyed )
			Destroy();
	}
}
