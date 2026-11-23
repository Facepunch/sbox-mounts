using Sandbox.Animation;

namespace Overgrowth;

// Equipment changes graph states and masks. All profiles for one character sample the same imported clips.
internal sealed class CharacterAnimationLibrary
{
	internal Skeleton Rig { get; }
	internal RabbitMirror Mirror { get; }
	internal CharacterMorph[] Morphs { get; }
	internal SkeletonData Skeleton { get; }
	internal float Scale { get; }
	internal (string Name, float Weight)[] PainMask { get; }
	readonly BaseFileSystem fs;
	readonly string path, referenceRig;
	readonly ILookup<string, string> mapping;
	readonly Dictionary<string, RabbitRetarget> sources = new();
	readonly Dictionary<(string Name, RabbitClip.CenterMotion Motion, bool Feet, bool Mirrored), RabbitClip> clips = new();

	internal CharacterAnimationLibrary( GameMount mount, string path, XmlAsset character, SkeletonData skeleton, float scale )
	{
		fs = mount.FileSystem;
		this.path = path;
		Skeleton = skeleton;
		Scale = scale;
		var morphs = Morphs = CharacterMorph.Read( character );
		var bones = new SkeletonBuilder();
		for ( int i = 0; i < skeleton.BindPose.Length; i++ )
		{
			int parent = skeleton.AnimationParents[i];
			var local = parent < 0 ? skeleton.BindPose[i] : skeleton.BindPose[parent].ToLocal( skeleton.BindPose[i] );
			bones.AddBone( $"bone_{i}", local, parent < 0 ? null : $"bone_{parent}" );
		}

		mapping = XmlAsset.Read( fs, "Data/Animations/retarget.xml", "retarget" ).Elements( "rig" )
			.SelectMany( rig => rig.Elements( "anim" ).Select( anim => (anim.Value, Rig: rig.Attribute( "path" )) ) )
			.ToLookup( entry => entry.Value, entry => entry.Rig, System.StringComparer.OrdinalIgnoreCase );
		referenceRig = mapping["Data/Animations/r_ledge.anm"].Last();
		sources[referenceRig] = new RabbitRetarget( fs, referenceRig, skeleton, scale, out var referenceSkeleton );
		var mirror = Mirror = referenceSkeleton.HierParents.SequenceEqual( skeleton.HierParents )
			? new RabbitMirror( referenceSkeleton, scale ) : new RabbitMirror( skeleton, 1f );
		if ( morphs.Any( m => m.Steps == 1 ) )
			bones.AddMorphChannels( morphs.Where( m => m.Steps == 1 ).Select( m => m.Name ).ToArray() );
		if ( morphs.Any( m => m.Steps > 1 ) )
			bones.AddFloatChannelSet( "multistep", morphs.Where( m => m.Steps > 1 ).Select( m => m.Name ).ToArray() );
		bones.AddFloatChannelSet( "status", RabbitClip.StatusChannels );
		foreach ( var hand in new[] { "leftarm", "rightarm" } )
			if ( skeleton.IKBones.TryGetValue( hand, out var chain ) )
				bones.AddFloatChannelSet( $"item_bone_{chain.BoneId}", WeaponPoseTrack.Names( chain.BoneId ) );
		var floatChannels = morphs.Select( m => m.Name ).Concat( RabbitClip.StatusChannels ).ToHashSet();
		bones.AddBoneMask( RabbitGuard.JumpArms, RabbitClip.ReadLayerMask( fs, $"Data/Animations/{RabbitGuard.JumpArms}.anm",
			skeleton.Bones.Count, floatChannels: floatChannels, bindItem: false ), defaultFloatChannelWeight: 0f );
		foreach ( var draw in RabbitGuard.HandAnimations )
			foreach ( bool mirrored in new[] { false, true } )
			{
				var mask = RabbitClip.ReadLayerMask( fs, $"Data/Animations/{draw}.anm", skeleton.Bones.Count, mirrored ? mirror : null, floatChannels, bindItem: false );
				bones.AddBoneMask( RabbitGuard.DrawLayer( draw, mirrored ), mask, defaultFloatChannelWeight: 0f );
			}
		foreach ( var stance in mount.WeaponStances.Values.Where( stance => stance.IsClip ) )
		{
			foreach ( bool mirrored in new[] { false, true } )
			{
				var mask = RabbitClip.ReadLayerMask( fs, stance.Path, skeleton.Bones.Count, mirrored ? mirror : null, floatChannels );
				bones.AddBoneMask( stance.MaskName( mirrored ), mask, defaultFloatChannelWeight: 0f );
			}
		}
		foreach ( var flinch in mount.WeaponFlinches.Append( character.Element( "animations" ).Attribute( "blockflinch" ) ).Where( file => file is not null ).Distinct() )
		{
			var mask = RabbitClip.ReadLayerMask( fs, flinch, skeleton.Bones.Count );
			foreach ( var shape in morphs.Where( m => m.Name is "fist_l" or "fist_r" ) ) mask[shape.Name] = 1f;
			bones.AddBoneMask( flinch, mask, defaultFloatChannelWeight: 0f );
		}
		var pain = RabbitClip.ReadLayerMask( fs, "Data/Animations/r_painflinch.anm", skeleton.Bones.Count );
		foreach ( var shape in morphs.Where( m => m.Name is "fist_l" or "fist_r" ) ) pain[shape.Name] = 1f;
		pain["rightarm_blend"] = 1f;
		// The flinch asset contains one pose. Group equal weights so overlapping
		// cuts can compose their masks without allocating another graph per hit.
		var painMask = PainMask = pain.Values.Distinct().Where( weight => weight > 0f ).Order()
			.Select( (weight, i) => (Name: $"pain_{i}", Weight: weight) ).ToArray();
		foreach ( var group in painMask )
			bones.AddBoneMask( group.Name, pain.ToDictionary( pair => pair.Key, pair => pair.Value == group.Weight ? 1f : 0f ), defaultFloatChannelWeight: 0f );
		var pickupMask = RabbitClip.ReadLayerMask( fs, "Data/Animations/r_pickup.anm", skeleton.Bones.Count );
		bones.AddBoneMask( "pickup_right", pickupMask, defaultFloatChannelWeight: 0f );
		bones.AddBoneMask( "pickup_left", pickupMask.ToDictionary( pair => $"bone_{mirror.Opposite( int.Parse( pair.Key[5..] ) )}", pair => pair.Value ), defaultFloatChannelWeight: 0f );
		var knifeMask = RabbitClip.ReadLayerMask( fs, "Data/Animations/r_knifeslash.anm", skeleton.Bones.Count );
		var left = knifeMask.ToDictionary( pair => $"bone_{mirror.Opposite( int.Parse( pair.Key[5..] ) )}", pair => pair.Value );
		foreach ( var shape in morphs.Where( shape => shape.Name is "fist_l" or "fist_r" ) ) knifeMask[shape.Name] = left[shape.Name] = 1f;
		knifeMask["rightarm_blend"] = left["leftarm_blend"] = 1f;
		bones.AddBoneMask( "knife_right", knifeMask, defaultFloatChannelWeight: 0f );
		bones.AddBoneMask( "knife_left", left, defaultFloatChannelWeight: 0f );
		Rig = bones.Create( path + "/rabbit.skel" );
	}

	internal RabbitRetarget SourceFor( string name )
	{
		var sourcePath = mapping[$"Data/Animations/{name}.anm"].LastOrDefault() ?? referenceRig;
		if ( !sources.TryGetValue( sourcePath, out var source ) )
			sources[sourcePath] = source = new RabbitRetarget( fs, sourcePath, Skeleton, Scale, out _ );
		return source;
	}

	internal RabbitClip Clip( string name, RabbitClip.CenterMotion motion = RabbitClip.CenterMotion.Full, bool feet = false, bool mirrored = false )
	{
		var key = (name, motion, feet, mirrored);
		if ( !clips.TryGetValue( key, out var clip ) )
			clips[key] = clip = RabbitClip.Load( fs, $"{path}/clips/{motion}/{(feet ? "gait" : "pose")}", Rig, Skeleton, name,
				SourceFor( name ), Morphs, sampleFeet: feet, centerMotion: motion, mirror: mirrored ? Mirror : null );
		return clip;
	}
}
