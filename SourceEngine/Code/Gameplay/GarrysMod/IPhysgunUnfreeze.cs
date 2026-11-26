namespace Source1.GarrysMod;

/// <summary>Controls Physics Gun reload for individual bodies of the targeted entity.</summary>
public interface IPhysgunUnfreeze : ISceneEvent<IPhysgunUnfreeze>
{
	bool CanUnfreeze( Connection player, Rigidbody body ) => true;
	void OnUnfrozen( Connection player, Rigidbody body ) { }
}
