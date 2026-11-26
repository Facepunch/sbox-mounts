namespace Overgrowth;

internal sealed class WeaponAttachment
{
	readonly record struct Hand( string Bone, string Set, string[] Channels, Vector3 Center );
	readonly Hand[] hands;
	readonly Transform correction;
	readonly Vector3 modelOffset;

	WeaponAttachment( Hand[] hands, Transform correction, Vector3 modelOffset )
	{
		this.hands = hands;
		this.correction = correction;
		this.modelOffset = modelOffset;
	}

	internal static WeaponAttachment Create( LevelItem item )
	{
		var mount = Sandbox.Mounting.Directory.Get( "overgrowth" ) as GameMount;
		if ( mount?.GetByPath( item.Holder.Model.ResourcePath ) is not CharacterLoader character ||
			item.Definition?.Element( "grip" ) is not { } grip ) return null;
		string chain = grip.Attribute( "ik_attach" );
		if ( string.IsNullOrEmpty( chain ) ) return null;
		int right = character.AttachmentBone( chain, 0, false ), left = character.AttachmentBone( chain, 0, true );
		bool mirrored = item.Bone == $"bone_{left}";
		int bone = mirrored ? left : right;
		if ( bone < 0 || item.Bone != $"bone_{bone}" ) return null;

		var correction = new Transform( Vector3.Zero, Rotation.FromAxis( Vector3.Left, 90f ) );
		if ( grip.Attribute( "anim_base" ) is { Length: > 0 } basePath )
		{
			Transform Read( string path ) => ItemGrip.Read( mount.FileSystem, mount.Resolve( path ), bone, mirrored, true, false, 1f );
			correction = correction.ToWorld( Read( basePath ).ToLocal( Read( grip.Attribute( "anim" ) ) ) );
		}
		var modelOffset = -(item.GetComponent<ModelRenderer>().Model.Attachments.GetTransform( "asset_origin" )?.Position ?? Vector3.Zero);
		var hands = new[] { right, left }.Where( i => i >= 0 ).Distinct().Select( i => new Hand(
			$"bone_{i}", $"item_bone_{i}", WeaponPoseTrack.Names( i ), character.AttachmentCenters.GetValueOrDefault( i ) ) ).ToArray();
		return new( hands, correction, modelOffset );
	}

	internal Transform Sample( SkinnedModelRenderer holder, Transform grip )
	{
		float total = 0f;
		var animated = grip;
		var graph = holder.Animation.Debug;
		foreach ( var hand in hands )
		{
			if ( !graph.TryGetFloatChannel( hand.Set, hand.Channels[0], out float weight ) || weight <= 0.0001f ||
				!holder.TryGetBoneTransform( hand.Bone, out var bone ) ) continue;
			float Read( int channel )
			{
				graph.TryGetFloatChannel( hand.Set, hand.Channels[channel], out var value );
				return value;
			}
			var rotation = new Rotation( Read( 4 ) / weight, Read( 5 ) / weight, Read( 6 ) / weight, Read( 7 ) / weight );
			var position = new Vector3( Read( 1 ), Read( 2 ), Read( 3 ) ) / weight;
			var basis = Matrix.CreateRotation( correction.Rotation ) * Matrix.CreateRotation( rotation );
			Rotation itemRotation = System.Numerics.Quaternion.Normalize( System.Numerics.Quaternion.CreateFromRotationMatrix( basis ) );
			var reference = bone.WithPosition( bone.PointToWorld( hand.Center ) ).WithScale( 1f );
			var target = reference.ToWorld( new Transform( position + rotation * correction.Position + itemRotation * modelOffset, itemRotation ) );
			total += weight;
			animated = Transform.Lerp( animated, target, weight / total, true );
		}
		return total > 0f ? Transform.Lerp( grip, animated, total, true ) : grip;
	}
}
