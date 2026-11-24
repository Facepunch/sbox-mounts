using Halo2.Blam;

namespace Halo2;

internal sealed partial class ShotgunLoader
{
	sealed record ImpactDecal( string Name, DecalDefinition Definition, Vector2 Modifier, Vector2 Radius,
		Vector2[] Sizes, Vector2[] Centers, float Lifetime, ParticleGradient Tint );

	readonly Dictionary<TagEntry, ImpactDecal> impactDecals = new();
	uint decalSeed = (uint)System.Random.Shared.Next();

	ImpactDecal ReadImpactDecal( TagEntry contact )
	{
		int events = Address( contact ) + 20;
		if ( map.ReadInt32( events ) < 2 ) return null;
		// Simple bullet marks are the single decal part in the contact event.
		int parts = Block( events ) + 56 + 24;
		if ( map.ReadInt32( parts ) != 1 ) return null;
		int part = Block( parts );
		if ( Reference( part + 12 ) is not { PrimaryClass: "aced" } tag ) return null;
		int header = Address( tag );
		if ( Short( header + 4 ) != 2 || Reference( header + 8 ) is not null ) return null;
		if ( Reference( header + 136 ) is not {} bitmap ) return null;
		int sequences = Address( bitmap ) + 60;
		int count = map.ReadInt32( sequences );
		string path = Host.AssetPath( map, bitmap.TagPath, "vtex" );
		var texture = Texture.Load( count > 0 ? path + "/sheet.vtex" : path );
		if ( !texture.IsValid() || texture.IsError || (count > 0 && texture.SequenceCount != count) ) return null;

		var sizes = new Vector2[System.Math.Max( count, 1 )];
		var centers = new Vector2[sizes.Length];
		sizes[0] = Vector2.One * 2;
		if ( count > 0 )
		{
			int sequence = Block( sequences );
			var scale = new Vector2( texture.Width, texture.Height ) / Float( header + 164 );
			for ( int i = 0; i < count; i++, sequence += 60 )
			{
				int sprite = Block( sequence + 52 );
				var bounds = Halo2TextureLoader.SpriteBounds( map, sprite + 8 );
				sizes[i] = bounds.Size * scale;
				centers[i] = (bounds.Size * 0.5f - new Vector2( Float( sprite + 24 ), Float( sprite + 28 ) )) * scale;
			}
		}
		float lifetime = Float( header + 52 );
		var tint = new ParticleGradient
		{
			Type = ParticleGradient.ValueType.Gradient,
			GradientA = new Gradient( new Gradient.ColorFrame( 0, Color.White ), new Gradient.ColorFrame( 1 - Float( header + 60 ) / lifetime, Color.White ),
				new Gradient.ColorFrame( 1, Color.White.WithAlpha( 0 ) ) )
		};
		return new ImpactDecal( tag.TagPath.Split( '\\' )[^1], new DecalDefinition { ColorTexture = texture, Width = 1, Height = 1 },
			new Vector2( Float( part + 40 ), Float( part + 44 ) ), new Vector2( Float( header + 16 ), Float( header + 20 ) ) * 120,
			sizes, centers, lifetime, tint );
	}

	internal bool PlacePelletImpact( Surface surface, GameObject target, Vector3 point, Vector3 normal, Vector3 direction )
	{
		LoadMaterialEffects();
		if ( !target.IsValid() || pelletEffects is null || ContactTag( pelletEffects, surface, true ) is not {} contact ) return false;
		if ( !impactDecals.TryGetValue( contact, out var data ) )
			impactDecals[contact] = data = ReadImpactDecal( contact );
		if ( data is null ) return false;

		float Sample( Vector2 range ) => range.x + (range.y - range.x) * (BlamRandom.Next( ref decalSeed ) / 65535f);
		float modifier = Sample( data.Modifier );
		var transform = new Transform( point, Rotation.LookAt( -normal, Vector3.Random ) );
		// The original high-word selection multiplies by the last sequence index.
		int sequence = ((int)BlamRandom.Next( ref decalSeed ) * (data.Sizes.Length - 1)) >> 16;
		float radius = Sample( data.Radius ) * (modifier == 0 ? 1 : modifier);
		var parent = target;
		if ( target.GetComponentInChildren<SkinnedModelRenderer>() is { CreateBoneObjects: true } skin && skin.GetClosestBone( point ) is {} bone )
			parent = skin.GetBoneObject( bone ) ?? target;
		var mark = new GameObject( parent, false, data.Name );
		mark.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		var center = data.Centers[sequence] * radius;
		mark.WorldTransform = transform.WithPosition( point + transform.Rotation.Left * center.x + transform.Rotation.Up * center.y );
		var decal = mark.AddComponent<Decal>();
		decal.Decals = [data.Definition];
		decal.Transient = true;
		decal.SheetSequence = data.Definition.ColorTexture.SequenceCount > 0;
		decal.SequenceId = (uint)sequence;
		decal.Size = data.Sizes[sequence] * radius;
		decal.Rotation = 0;
		decal.LifeTime = data.Lifetime;
		decal.ColorTint = data.Tint;
		decal.SortLayer = 2;
		mark.AddComponent<TemporaryEffect>();
		mark.Enabled = true;

		var dust = surface.PrefabCollection.BulletImpact ?? surface.GetBaseSurface()?.PrefabCollection.BulletImpact;
		if ( dust.IsValid() )
		{
			var impact = dust.Clone( new CloneConfig { Transform = transform, StartEnabled = true } );
			impact.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
			impact.SetParent( parent, true );
			if ( impact.GetComponent<TemporaryEffect>() is {} effect ) effect.BecomeOrphan = true;
		}
		surface.PlaceSplatter( point, direction, target );
		return true;
	}
}
