namespace Overgrowth;

public sealed partial class LevelItem : Component
{
	[Property, Sync] public SkinnedModelRenderer Holder { get; set; }
	[Property, Sync] public string Bone { get; set; }
	[Property, Sync] public Transform Offset { get; set; }
	[Property] public float RangeExtension { get; set; }
	[Property] public float RangeMultiplier { get; set; } = 1f;

	[Sync] internal SkinnedModelRenderer StuckIn { get; set; }

	protected override void OnPreRender()
	{
		if ( StuckIn.IsValid() && StuckIn.TryGetBoneTransform( Bone, out var bone ) )
			WorldTransform = bone.ToWorld( Offset );
		else UpdateAttachment();
	}

	void UpdateAttachment()
	{
		if ( !Holder.IsValid() || string.IsNullOrEmpty( Bone ) || !Holder.TryGetBoneTransform( Bone, out var hand ) ) return;
		var pose = hand.ToWorld( Offset );
		if ( Holder != previousHolder || Bone != previousBone )
		{
			previousHolder = Holder;
			previousBone = Bone;
			guard = Holder.GameObject.GetComponentInParent<RabbitGuard>( true );
			weaponPose = WeaponAttachment.Create( this );
		}
		WorldTransform = guard.IsValid() && guard.EquipsForAnimation( this ) && weaponPose is not null
			? weaponPose.Sample( Holder, pose ) : pose;
	}

	RabbitGuard guard;
	SkinnedModelRenderer previousHolder;
	string previousBone;
	WeaponAttachment weaponPose;

	Transform? lastHeldPose;
	Vector3 carryVelocity, carryAngularVelocity;

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || Time.Delta <= 0f ) return;
		if ( !Holder.IsValid() )
		{
			if ( GetComponent<Rigidbody>() is not { Enabled: true } body ) return;
			if ( pendingEmbed is { } stuck )
			{
				pendingEmbed = null;
				body.Velocity = body.AngularVelocity = Vector3.Zero;
				body.Enabled = false;
				GetComponent<ModelCollider>().Enabled = false;
				WorldTransform = stuck;
				thrower = null;
				return;
			}
			incomingVelocity = body.Velocity;
			if ( thrower is not null )
			{
				if ( flyingStraight ) AlignWithFlight( body );
				TraceFlight( body );
			}
			return;
		}
		UpdateAttachment();
		var pose = WorldTransform;
		if ( lastHeldPose is { } previous )
		{
			var center = GetComponent<ModelRenderer>().Model.Attachments.GetTransform( "mass_center" )?.Position ?? Vector3.Zero;
			carryVelocity = (pose.PointToWorld( center ) - previous.PointToWorld( center )) / Time.Delta;
			var delta = Rotation.Difference( previous.Rotation, pose.Rotation );
			var axis = new Vector3( delta.x, delta.y, delta.z ).Normal;
			carryAngularVelocity = axis * ((delta.w < 0f ? -1f : 1f) * delta.Angle().DegreeToRadian() / Time.Delta);
		}
		lastHeldPose = pose;
	}

	internal void Drop()
	{
		UpdateAttachment();
		Holder = null;
		GameObject.SetParent( null, true );
		var body = AddPhysics( GameObject, GetComponent<ModelRenderer>().Model );
		body.GravityScale = RabbitGuard.FallGravityScale( Scene.PhysicsWorld );
		body.Velocity = incomingVelocity = carryVelocity;
		body.AngularVelocity = carryAngularVelocity;
		lastHeldPose = null;
		carryVelocity = carryAngularVelocity = Vector3.Zero;
	}

	internal Vector3 PhysicsPosition => WorldTransform.PointToWorld( GetComponent<ModelRenderer>().Model.Attachments.GetTransform( "mass_center" )?.Position ?? Vector3.Zero );
	internal int Hands => Definition?.Element( "grip" )?.Number( "hands" ) is { } hands ? (int)hands : 1;
	XmlAsset itemDefinition;
	internal XmlAsset Definition => itemDefinition ??= (Sandbox.Mounting.Directory.Get( "overgrowth" )?.GetByPath( GetComponent<ModelRenderer>().Model.ResourcePath ) as ObjectLoader)?.ItemDefinition;

	ItemLine[] lines;
	ItemLine[] GetLines()
	{
		if ( lines is not null ) return lines;
		var model = GetComponent<ModelRenderer>().Model;
		var mount = Sandbox.Mounting.Directory.Get( "overgrowth" );
		return lines = mount?.GetByPath( model.ResourcePath ) is ObjectLoader loader ? ItemLine.Read( loader.ItemDefinition, model ) : [];
	}

	internal bool TryContact( LevelItem other, out Vector3 point, out string material, out string otherMaterial )
	{
		UpdateAttachment();
		other.UpdateAttachment();
		float closest = float.PositiveInfinity;
		point = default;
		material = otherMaterial = null;
		foreach ( var a in GetLines() )
		{
			var segment = a.ToWorld( WorldTransform );
			foreach ( var b in other.GetLines() )
			{
				var (first, second) = ItemLine.Closest( segment, b.ToWorld( other.WorldTransform ) );
				float distance = first.DistanceSquared( second );
				if ( distance >= closest ) continue;
				closest = distance;
				point = (first + second) * 0.5f;
				material = a.Material;
				otherMaterial = b.Material;
			}
		}
		return material is not null;
	}

	internal static void Attach( GameMount mount, GameObject item, Model model, RabbitGuard actor, int type, bool mirrored, string attachmentPath = null )
	{
		if ( mount.GetByPath( actor.Renderer.Model.ResourcePath ) is not CharacterLoader character ||
			mount.GetByPath( model.ResourcePath ) is not ObjectLoader loader ) return;
		string anim, chain;
		int depth = 0;
		if ( type == 2 )
		{
			if ( string.IsNullOrEmpty( attachmentPath ) ) return;
			var attachment = XmlAsset.Read( mount.FileSystem, mount.Resolve( attachmentPath ), "attachment" );
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
		var center = -(model.Attachments.GetTransform( "asset_origin" )?.Position ?? Vector3.Zero);
		if ( item.GetComponent<ModelCollider>() is { } collider ) collider.Enabled = false;
		if ( item.GetComponent<Rigidbody>() is { } body ) body.Enabled = false;
		item.SetParent( actor.GameObject, true );
		var attachmentComponent = item.GetOrAddComponent<LevelItem>();
		attachmentComponent.StuckIn = null;
		attachmentComponent.pendingEmbed = null;
		attachmentComponent.thrower = null;
		attachmentComponent.flyingStraight = false;
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
		else if ( type == 1 && loader.ItemDefinition.Element( "type" )?.Value == "weapon" && loader.ItemDefinition.Element( "label" )?.Value != "scabbard" )
		{
			// In the asset's default sheathed pose the weapon hangs on the left hip.
			if ( mirrored ) actor.RightHipWeapon = attachmentComponent;
			else actor.LeftHipWeapon = attachmentComponent;
		}
	}

	internal static Rigidbody AddPhysics( GameObject item, Model model )
	{
		var collider = item.GetComponent<ModelCollider>( true ) ?? item.AddComponent<ModelCollider>();
		collider.Model = model;
		var body = item.GetComponent<Rigidbody>( true ) ?? item.AddComponent<Rigidbody>();
		body.MassOverride = model.Physics.Parts[0].Mass;
		body.OverrideMassCenter = true;
		body.MassCenterOverride = model.Attachments.GetTransform( "mass_center" )?.Position ?? Vector3.Zero;
		body.EnhancedCcd = true;
		body.CollisionEventsEnabled = true;
		body.Enabled = collider.Enabled = true;
		item.GetOrAddComponent<LevelItem>();
		return body;
	}

	protected override void OnStart()
	{
		if ( GetComponent<Rigidbody>() is { } body ) body.GravityScale = RabbitGuard.FallGravityScale( Scene.PhysicsWorld );
	}
}
