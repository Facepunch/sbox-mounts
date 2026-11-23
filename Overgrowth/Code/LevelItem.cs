namespace Overgrowth;

public sealed class LevelItem : Component
{
	internal static void AddPhysics( GameObject item, Model model )
	{
		item.AddComponent<ModelCollider>().Model = model;
		var body = item.AddComponent<Rigidbody>();
		body.MassOverride = model.Physics.Parts[0].Mass;
		body.OverrideMassCenter = true;
		body.MassCenterOverride = model.GetAttachment( "mass_center" )?.Position ?? Vector3.Zero;
		body.EnhancedCcd = true;
		item.AddComponent<LevelItem>();
	}

	protected override void OnStart()
	{
		GetComponent<Rigidbody>().GravityScale = RabbitGuard.FallGravityScale( Scene.PhysicsWorld );
	}
}
