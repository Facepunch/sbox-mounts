using Sandbox;

namespace GoldSrc.Maps;

internal static class BrushInteraction
{
	public static bool IsPlayer( GameObject other ) => other.IsValid()
		&& (other.Tags.Has( "player" ) || other.GetComponentInParent<PlayerController>().IsValid());

	public static Vector3 Velocity( GameObject other )
	{
		if ( !other.IsValid() ) return Vector3.Zero;
		var character = other.GetComponentInParent<CharacterController>();
		if ( character.IsValid() ) return character.Velocity;
		var body = other.GetComponentInParent<Rigidbody>();
		return body.IsValid() ? body.Velocity : Vector3.Zero;
	}

	public static void SetVelocity( GameObject other, Vector3 velocity )
	{
		if ( !other.IsValid() ) return;
		var character = other.GetComponentInParent<CharacterController>();
		if ( character.IsValid() ) character.Velocity = velocity;
		else
		{
			var body = other.GetComponentInParent<Rigidbody>();
			if ( body.IsValid() ) body.Velocity = velocity;
		}
	}

	public static GameObject Ground( GameObject other )
	{
		if ( !other.IsValid() ) return null;
		var player = other.GetComponentInParent<PlayerController>();
		if ( player.IsValid() ) return player.GroundObject;
		var character = other.GetComponentInParent<CharacterController>();
		return character.IsValid() && character.IsOnGround ? character.GroundObject : null;
	}

	public static void Damage( GameObject source, GameObject other, float amount, string kind = "crush" )
	{
		if ( !other.IsValid() || amount == 0f ) return;
		var damage = new DamageInfo
		{
			Damage = amount,
			Attacker = source,
			Weapon = source,
			Origin = source.WorldPosition,
			Position = other.WorldPosition
		};
		damage.Tags.Add( kind );
		foreach ( var receiver in other.GetComponentsInParent<Component.IDamageable>() )
			receiver.OnDamage( damage );
	}
}
