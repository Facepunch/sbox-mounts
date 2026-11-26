using System;
using System.Globalization;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Property, Hide] public Dictionary<int, Transform> PlacedBodyPose { get; set; }

	internal void SetPlacedBodyPose( string value )
	{
		var tokens = value.Split( (char[])null, StringSplitOptions.RemoveEmptyEntries );
		var pose = new Dictionary<int, Transform>();
		for ( int i = 0; i + 7 < tokens.Length; i += 8 )
		{
			float Number( int offset ) => float.Parse( tokens[i + offset], CultureInfo.InvariantCulture );
			int bone = int.Parse( tokens[i], CultureInfo.InvariantCulture );
			var position = new Vector3( Number( 3 ), Number( 1 ), Number( 2 ) ) * 40f;
			var rotation = new Rotation( Number( 6 ), Number( 4 ), Number( 5 ), Number( 7 ) );
			pose[bone] = WorldTransform.ToLocal( new Transform( position, rotation, WorldScale ) );
		}
		PlacedBodyPose = pose;
	}

	void SpawnPlacedBody()
	{
		if ( !Networking.IsHost || !Renderer.IsValid() ) return;
		var model = Renderer.Model;
		if ( CharacterLoader.FromModel( model ) is not { } character || model.Physics is not { } description ) return;
		var corpse = new GameObject( false, GameObject.Name ) { WorldTransform = WorldTransform };
		corpse.Tags.Add( "ragdoll" );
		var prop = corpse.AddComponent<Prop>();
		prop.Model = model;
		prop.BodyGroups = Renderer.BodyGroups;
		prop.MaterialGroup = Renderer.MaterialGroup;
		prop.Tint = Renderer.Tint;
		var bones = model.CreateBoneObjects( corpse );
		foreach ( var part in description.Parts )
		{
			var bone = model.Bones.GetBone( part.BoneName );
			if ( bone is null || !bones.TryGetValue( bone, out var go ) || !go.IsValid() ) continue;
			int id = int.Parse( bone.Name.AsSpan( 5 ), CultureInfo.InvariantCulture );
			if ( !PlacedBodyPose.TryGetValue( id, out var local ) || !character.AttachmentCenters.TryGetValue( id, out var center ) ) continue;
			var world = WorldTransform.ToWorld( local );
			go.Flags |= GameObjectFlags.Absolute | GameObjectFlags.PhysicsBone;
			go.WorldTransform = world.WithPosition( world.Position - world.Rotation * (center * world.Scale) );
		}
		using ( Scene.BatchGroup() ) corpse.Enabled = true;
		var physics = corpse.GetComponent<ModelPhysics>();
		var renderer = corpse.GetComponent<SkinnedModelRenderer>();
		if ( !physics.IsValid() || !renderer.IsValid() )
		{
			corpse.Destroy();
			return;
		}
		physics.StartAsleep = true;
		renderer.UseAnimGraph = false;
		ConfigureCorpse( corpse, physics );
		corpse.GetComponent<RagdollDamping>().SetPlacedPose();
		corpse.NetworkSpawn();
		GameObject.Destroy();
	}
}
