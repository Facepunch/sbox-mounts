namespace Source1.GarrysMod;

/// <summary>Called on the held entity when the Physics Gun attempts a freeze.</summary>
public interface IPhysgunFreeze : ISceneEvent<IPhysgunFreeze>
{
	/// <summary>The body is null when the held entity moves without a rigidbody.</summary>
	bool CanFreeze( PhysicsGun weapon, Rigidbody body, Connection player ) => true;
	void OnFrozen( Connection player, Rigidbody body ) { }
}
