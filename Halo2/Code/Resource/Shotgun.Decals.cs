namespace Halo2;

internal sealed partial class ShotgunLoader
{
	DecalDefinition stoneDecal;
	Vector2 stoneRadius, stoneModifier;
	Vector2[] stoneSizes, stoneCenters;
	uint decalSeed = (uint)System.Random.Shared.Next();
	ParticleGradient stoneTint;
	float stoneLifetime;

	internal bool PlaceStoneImpact( Surface surface, GameObject target, Vector3 point, Vector3 normal, Vector3 direction )
	{
		if ( !target.IsValid() || !surface.IsValid() || MaterialName( surface ) != "hard_terrain_concrete" ) return false;
		if ( !stoneDecal.IsValid() )
		{
			var contact = map.Tags.FirstOrDefault( t => t.PrimaryClass == "effe" && t.TagPath == @"effects\impact\impact_bullet_small\hard_terrain_concrete" );
			if ( contact is null ) return false;
			int part = Block( Block( Address( contact ) + 20 ) + 56 + 24 );
			var tag = Reference( part + 12 );
			if ( tag is null ) return false;
			int header = Address( tag );
			var bitmap = Reference( header + 136 );
			if ( bitmap is null ) return false;
			var texture = Texture.Load( Host.AssetPath( map, bitmap.TagPath, "vtex" ) + "/sheet.vtex" );
			if ( !texture.IsValid() || texture.IsError || texture.SequenceCount == 0 ) return false;

			stoneRadius = new Vector2( Float( header + 16 ), Float( header + 20 ) ) * 120;
			stoneModifier = new Vector2( Float( part + 40 ), Float( part + 44 ) );
			stoneSizes = new Vector2[texture.SequenceCount];
			stoneCenters = new Vector2[texture.SequenceCount];
			int sequences = Block( Address( bitmap ) + 60 );
			var scale = new Vector2( texture.Width, texture.Height ) / Float( header + 164 );
			for ( int i = 0; i < stoneSizes.Length; i++ )
			{
				int sprite = Block( sequences + i * 60 + 52 );
				var bounds = Halo2TextureLoader.SpriteBounds( map, sprite + 8 );
				stoneSizes[i] = bounds.Size * scale;
				stoneCenters[i] = (bounds.Size * 0.5f - new Vector2( Float( sprite + 24 ), Float( sprite + 28 ) )) * scale;
			}
			stoneLifetime = Float( header + 52 );
			float hold = stoneLifetime - Float( header + 60 );
			stoneTint = new ParticleGradient
			{
				Type = ParticleGradient.ValueType.Gradient,
				GradientA = new Gradient( new Gradient.ColorFrame( 0, Color.White ), new Gradient.ColorFrame( hold / stoneLifetime, Color.White ),
					new Gradient.ColorFrame( 1, Color.White.WithAlpha( 0 ) ) )
			};
			stoneDecal = new DecalDefinition { ColorTexture = texture, Width = 1, Height = 1 };
		}

		float Sample( Vector2 range ) => range.x + (range.y - range.x) * (BlamRandom.Next( ref decalSeed ) / 65535f);
		float modifier = Sample( stoneModifier );
		var transform = new Transform( point, Rotation.LookAt( -normal, Vector3.Random ) );
		// The original high-word selection multiplies by the last sequence index.
		int sequence = ((int)BlamRandom.Next( ref decalSeed ) * (stoneSizes.Length - 1)) >> 16;
		float radius = Sample( stoneRadius ) * modifier;
		var parent = target;
		if ( target.GetComponentInChildren<SkinnedModelRenderer>() is { CreateBoneObjects: true } skin && skin.GetClosestBone( point ) is {} bone )
			parent = skin.GetBoneObject( bone ) ?? target;
		var mark = new GameObject( parent, false, "stone_med" );
		mark.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		var center = stoneCenters[sequence] * radius;
		mark.WorldTransform = transform.WithPosition( point + transform.Rotation.Left * center.x + transform.Rotation.Up * center.y );
		var decal = mark.AddComponent<Decal>();
		decal.Decals = [stoneDecal];
		decal.Transient = true;
		decal.SheetSequence = true;
		decal.SequenceId = (uint)sequence;
		decal.Size = stoneSizes[sequence] * radius;
		decal.Rotation = 0;
		decal.LifeTime = stoneLifetime;
		decal.ColorTint = stoneTint;
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
