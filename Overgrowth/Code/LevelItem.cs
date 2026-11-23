namespace Overgrowth;

public sealed class LevelItem : Component
{
	[Property] public SkinnedModelRenderer Holder { get; set; }
	[Property] public string Bone { get; set; }
	[Property] public Transform Offset { get; set; }
	[Property] public float RangeExtension { get; set; }
	[Property] public float RangeMultiplier { get; set; } = 1f;

	protected override void OnPreRender() => UpdateAttachment();

	void UpdateAttachment()
	{
		if ( Holder.IsValid() && Holder.TryGetBoneTransform( Bone, out var hand ) )
			WorldTransform = hand.ToWorld( Offset );
	}

	internal static void Attach( GameMount mount, GameObject item, Model model, RabbitGuard actor, XmlAsset connection )
	{
		if ( mount.GetByPath( actor.Renderer.Model.ResourcePath ) is not CharacterLoader character ||
			mount.GetByPath( model.ResourcePath ) is not ObjectLoader loader ) return;
		var type = connection.Number( "type" ) ?? 0;
		bool mirrored = connection.Number( "mirrored" ) == 1;
		string anim, chain;
		int depth = 0;
		if ( type == 2 )
		{
			var path = connection.Attribute( "attachment" );
			if ( string.IsNullOrEmpty( path ) ) return;
			var attachment = XmlAsset.Read( mount.FileSystem, mount.Resolve( path ), "attachment" );
			anim = attachment.Element( "anim" )?.Value;
			chain = attachment.Element( "attach" )?.Attribute( "ik_chain" );
			depth = (int)(attachment.Element( "attach" )?.Number( "bone" ) ?? 0);
		}
		else
		{
			var slot = loader.ItemDefinition.Element( type == 0 ? "grip" : "sheathe" );
			anim = slot?.Attribute( "anim" );
			chain = slot?.Attribute( "ik_attach" );
		}
		if ( string.IsNullOrEmpty( anim ) || string.IsNullOrEmpty( chain ) ) return;
		int bone = character.AttachmentBone( chain, depth, mirrored );
		if ( bone < 0 ) return;
		var grip = ItemGrip.Read( mount.FileSystem, mount.Resolve( anim ), bone, mirrored, type != 2, chain == "hip", character.AttachmentScale );
		var center = -(model.GetAttachment( "asset_origin" )?.Position ?? Vector3.Zero);
		item.SetParent( actor.GameObject, true );
		var attachmentComponent = item.AddComponent<LevelItem>();
		attachmentComponent.Holder = actor.Renderer;
		attachmentComponent.Bone = $"bone_{bone}";
		attachmentComponent.Offset = grip.WithPosition( character.AttachmentCenters.GetValueOrDefault( bone ) + grip.PointToWorld( center ) );
		attachmentComponent.UpdateAttachment();
		if ( type == 0 )
		{
			var range = loader.ItemDefinition.Element( "range" );
			attachmentComponent.RangeExtension = range?.Number( "extend" ) ?? 0f;
			attachmentComponent.RangeMultiplier = range?.Number( "multiply" ) ?? 1f;
			if ( mirrored ) actor.LeftHandItem = attachmentComponent;
			else actor.RightHandItem = attachmentComponent;
		}
	}

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
		if ( GetComponent<Rigidbody>() is { } body ) body.GravityScale = RabbitGuard.FallGravityScale( Scene.PhysicsWorld );
	}
}
