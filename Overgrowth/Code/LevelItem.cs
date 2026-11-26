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
		var pose = hand.ToWorld( Offset ).WithScale( 1f );
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
				if ( GetComponent<ModelCollider>() is { } collider ) collider.Enabled = false;
				WorldTransform = stuck;
				MarkThrowSafe();
				return;
			}
			incomingVelocity = body.Velocity;
			if ( flight == FlightState.Straight ) AlignWithFlight( body );
			else if ( flight is FlightState.Safe or FlightState.Bounced && body.AngularVelocity.LengthSquared > 15f * 15f )
				body.AngularVelocity = body.AngularVelocity.Normal * 15f;
			TraceFlight( body );
			return;
		}
		UpdateAttachment();
		var pose = WorldTransform;
		if ( lastHeldPose is { } previous )
		{
			var center = LocalMassCenter;
			carryVelocity = (pose.PointToWorld( center ) - previous.PointToWorld( center )) / Time.Delta;
			var delta = Rotation.Difference( previous.Rotation, pose.Rotation );
			var axis = new Vector3( delta.x, delta.y, delta.z ).Normal;
			carryAngularVelocity = axis * ((delta.w < 0f ? -1f : 1f) * delta.Angle().DegreeToRadian() / Time.Delta);
		}
		lastHeldPose = pose;
	}

	internal Rigidbody Drop()
	{
		UpdateAttachment();
		Holder = null;
		StuckIn = null;
		GameObject.SetParent( null, true );
		lastHeldPose = null;
		var body = AddPhysics( GameObject, ItemModel );
		if ( !body.IsValid() ) return null;
		body.GravityScale = RabbitGuard.FallGravityScale( Scene.PhysicsWorld );
		body.Velocity = incomingVelocity = carryVelocity;
		body.AngularVelocity = carryAngularVelocity;
		carryVelocity = carryAngularVelocity = Vector3.Zero;
		return body;
	}

	internal Model ItemModel => GetComponent<ModelRenderer>()?.Model;
	Vector3 LocalMassCenter => ItemModel?.Attachments.GetTransform( "mass_center" )?.Position ?? Vector3.Zero;
	internal Vector3 PhysicsPosition => WorldTransform.PointToWorld( LocalMassCenter );
	internal string Label => Definition?.Element( "label" )?.Value;
	internal int Hands => Definition?.Element( "grip" )?.Number( "hands" ) is { } hands ? (int)hands : 1;
	internal XmlAsset Definition => ObjectLoader.FromItemModel( ItemModel )?.ItemDefinition;

	Model lineModel;
	ItemLine[] lines;
	ItemLine[] GetLines()
	{
		var model = ItemModel;
		if ( model != lineModel )
		{
			lineModel = model;
			lines = null;
			flightLines = null;
		}
		if ( lines is not null ) return lines;
		if ( ObjectLoader.FromItemModel( model ) is not { } loader ) return [];
		return lines = ItemLine.Read( loader.ItemDefinition, model );
	}

	internal Vector3 StabDirection()
	{
		var segments = GetLines();
		if ( segments.Length == 0 ) return default;
		UpdateAttachment();
		var last = segments[^1].ToWorld( WorldTransform );
		return (last.End - last.Start).Normal;
	}

	internal Vector3 StrikeLineDirection()
	{
		var segments = GetLines();
		if ( segments.Length == 0 ) return default;
		UpdateAttachment();
		return (WorldTransform.PointToWorld( segments[^1].End ) - WorldTransform.PointToWorld( segments[0].Start )).Normal;
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

	internal static bool Attach( GameMount mount, GameObject item, Model model, RabbitGuard actor, int type, bool mirrored, string attachmentPath = null )
	{
		if ( !item.IsValid() || !actor.IsValid() || !actor.Renderer.IsValid() || !model.IsValid() ||
			CharacterLoader.FromModel( actor.Renderer.Model ) is not { } character ||
			mount?.GetByPath( model.ResourcePath ) is not ObjectLoader loader ) return false;
		string anim, chain;
		int depth = 0;
		if ( type == 2 )
		{
			if ( string.IsNullOrEmpty( attachmentPath ) ) return false;
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
		if ( string.IsNullOrEmpty( anim ) || string.IsNullOrEmpty( chain ) ) return false;
		int bone = character.AttachmentBone( chain, depth, mirrored );
		if ( bone < 0 ) return false;
		var grip = ItemGrip.Read( mount.FileSystem, mount.Resolve( anim ), bone, mirrored, type != 2, chain == "hip", character.AttachmentScale );
		var center = -(model.Attachments.GetTransform( "asset_origin" )?.Position ?? Vector3.Zero);
		if ( item.GetComponent<ModelCollider>() is { } collider ) collider.Enabled = false;
		if ( item.GetComponent<Rigidbody>() is { } body ) body.Enabled = false;
		item.SetParent( actor.GameObject, true );
		var attachmentComponent = item.GetOrAddComponent<LevelItem>();
		attachmentComponent.StuckIn = null;
		attachmentComponent.pendingEmbed = null;
		attachmentComponent.MarkThrowSafe();
		attachmentComponent.lastHeldPose = null;
		attachmentComponent.carryVelocity = attachmentComponent.carryAngularVelocity = Vector3.Zero;
		if ( attachmentComponent.Holder.IsValid() &&
			attachmentComponent.Holder.GetComponentInParent<RabbitGuard>( true ) is { } previous )
		{
			if ( previous.LeftHandItem == attachmentComponent ) previous.LeftHandItem = null;
			if ( previous.RightHandItem == attachmentComponent ) previous.RightHandItem = null;
			if ( previous.LeftHipWeapon == attachmentComponent ) previous.LeftHipWeapon = null;
			if ( previous.RightHipWeapon == attachmentComponent ) previous.RightHipWeapon = null;
		}
		attachmentComponent.Holder = actor.Renderer;
		attachmentComponent.Bone = $"bone_{bone}";
		attachmentComponent.Offset = grip.WithPosition( character.AttachmentCenters.GetValueOrDefault( bone ) + grip.PointToWorld( center / actor.WorldScale.z ) );
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
		return true;
	}

	internal static Rigidbody AddPhysics( GameObject item, Model model )
	{
		if ( !item.IsValid() || !model.IsValid() || model.Physics?.Parts.FirstOrDefault() is not { } part ) return null;
		var collider = item.GetComponent<ModelCollider>( true ) ?? item.AddComponent<ModelCollider>();
		collider.Model = model;
		var body = item.GetComponent<Rigidbody>( true ) ?? item.AddComponent<Rigidbody>();
		body.MassOverride = part.Mass;
		body.OverrideMassCenter = true;
		body.MassCenterOverride = model.Attachments.GetTransform( "mass_center" )?.Position ?? Vector3.Zero;
		body.EnhancedCcd = true;
		body.CollisionEventsEnabled = true;
		body.Enabled = collider.Enabled = true;
		SetItemInertia( body, model );
		var component = item.GetOrAddComponent<LevelItem>();
		component.previousFlightPose = item.WorldTransform;
		item.Tags.Add( CharacterCollisionRules.ThrownItem );
		return body;
	}

	protected override void OnStart()
	{
		// Loose items hit characters through their animated hulls.
		CharacterCollisionRules.Apply( Scene.PhysicsWorld );
		previousFlightPose = WorldTransform;
		if ( GetComponent<Rigidbody>() is { } body )
		{
			body.GravityScale = RabbitGuard.FallGravityScale( Scene.PhysicsWorld );
			SetItemInertia( body, GetComponent<ModelRenderer>()?.Model );
		}
	}

	static void SetItemInertia( Rigidbody body, Model model )
	{
		if ( body.PhysicsBody is not { IsValid: true } physics || model?.Physics?.Parts.FirstOrDefault()?.Hulls.FirstOrDefault() is not { } hull ) return;
		var size = hull.Bounds.Size * body.WorldScale.Abs() + Vector3.One * (0.08f * 40f);
		var squared = size * size;
		physics.SetInertiaTensor( new Vector3( squared.y + squared.z, squared.x + squared.z, squared.x + squared.y ) / 12f, Rotation.Identity );
	}
}
