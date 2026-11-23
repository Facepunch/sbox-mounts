namespace Overgrowth;

// RiggedObject resolves the animation's reference against the final physics bone,
// then applies the item's grip correction and converts from ANM to item axes.
internal sealed class WeaponAttachment
{
	readonly record struct Hand( string Bone, string Set, string[] Channels, Vector3 Center );
	readonly Hand[] hands;
	readonly Transform correction;

	WeaponAttachment( Hand[] hands, Transform correction )
	{
		this.hands = hands;
		this.correction = correction;
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
		var center = -(item.GetComponent<ModelRenderer>().Model.Attachments.GetTransform( "asset_origin" )?.Position ?? Vector3.Zero);
		correction.Position = correction.PointToWorld( center );
		var hands = new[] { right, left }.Where( i => i >= 0 ).Distinct().Select( i => new Hand(
			$"bone_{i}", $"item_bone_{i}", WeaponPoseTrack.Names( i ), character.AttachmentCenters.GetValueOrDefault( i ) ) ).ToArray();
		return new( hands, correction );
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
			var rotation = new Rotation( Read( 4 ), Read( 5 ), Read( 6 ), Read( 7 ) ).Normal;
			var local = new Transform( new Vector3( Read( 1 ), Read( 2 ), Read( 3 ) ) / weight, rotation );
			var reference = bone.WithPosition( bone.PointToWorld( hand.Center ) ).WithScale( 1f );
			var target = reference.ToWorld( local.ToWorld( correction ) );
			total += weight;
			animated = Transform.Lerp( animated, target, weight / total, true );
		}
		return total > 0f ? Transform.Lerp( grip, animated, total, true ) : grip;
	}
}
