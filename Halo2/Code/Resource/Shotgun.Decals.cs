using Halo2.Blam;

namespace Halo2;

internal sealed partial class ShotgunLoader
{
	sealed record ImpactDecal( TagEntry Tag, string Name, DecalDefinition Definition, Vector2 Modifier, Vector2 Radius,
		Vector2[] Sizes, Vector2[] Centers, int MaxOverlap, float RejectionDistance, float Lifetime, ParticleGradient Tint );

	readonly Dictionary<TagEntry, (bool Handled, ImpactDecal Decal)> contactVisuals = new();
	uint decalSeed = (uint)System.Random.Shared.Next();

	(bool Handled, ImpactDecal Decal) ReadContactVisuals( TagEntry contact )
	{
		int events = Address( contact ) + 20;
		int eventCount = map.ReadInt32( events );
		if ( eventCount == 0 ) return default;
		int row = Block( events );
		int part = 0;
		bool hasParticles = false;
		for ( int i = 0; i < eventCount; i++, row += 56 )
		{
			hasParticles |= map.ReadInt32( row + 48 ) > 0;
			int parts = map.ReadInt32( row + 24 );
			if ( parts == 0 ) continue;
			if ( parts != 1 || part != 0 ) return default;
			part = Block( row + 24 );
		}
		if ( part == 0 ) return (hasParticles, null);
		if ( Reference( part + 12 ) is not { PrimaryClass: "aced" } tag ) return default;
		int header = Address( tag );
		if ( Short( header + 4 ) != 2 || Reference( header + 8 ) is not null ) return default;
		if ( Reference( header + 136 ) is not {} bitmap ) return default;
		int sequences = Address( bitmap ) + 60;
		int count = map.ReadInt32( sequences );
		string path = Host.AssetPath( map, bitmap.TagPath, "vtex" );
		var texture = Texture.Load( count > 0 ? path + "/sheet.vtex" : path );
		if ( !texture.IsValid() || texture.IsError || (count > 0 && texture.SequenceCount != count) ) return default;

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
		float rejection = Float( header + 24 );
		return (true, new ImpactDecal( tag, tag.TagPath.Split( '\\' )[^1], new DecalDefinition { ColorTexture = texture, Width = 1, Height = 1 },
			new Vector2( Float( part + 40 ), Float( part + 44 ) ), new Vector2( Float( header + 16 ), Float( header + 20 ) ) * 120,
			sizes, centers, Short( header + 6 ), Float( header + 20 ) * 120 * (rejection == 0 ? 1 : rejection), lifetime, tint ));
	}

	internal bool PlacePelletImpact( Surface surface, GameObject target, Vector3 point, Vector3 normal, Vector3 direction )
	{
		LoadMaterialEffects();
		if ( !target.IsValid() || pelletEffects is null || ContactTag( pelletEffects, surface, true ) is not {} contact ) return false;
		if ( !contactVisuals.TryGetValue( contact, out var visuals ) )
			contactVisuals[contact] = visuals = ReadContactVisuals( contact );
		if ( !visuals.Handled ) return false;

		var transform = new Transform( point, Rotation.LookAt( -normal, Vector3.Random ) );
		var parent = target;
		if ( target.GetComponentInChildren<SkinnedModelRenderer>() is { CreateBoneObjects: true } skin && skin.GetClosestBone( point ) is {} bone )
			parent = skin.GetBoneObject( bone ) ?? target;
		if ( visuals.Decal is {} decal ) PlaceMark( decal, parent, point, normal );

		if ( !surface.IsValid() ) return true;
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

	void PlaceMark( ImpactDecal data, GameObject parent, Vector3 point, Vector3 normal )
	{
		float Fraction() => BlamRandom.Next( ref decalSeed ) * (1f / 65535f);
		float Sample( Vector2 range ) => range.x + (range.y - range.x) * Fraction();
		float modifier = data.Modifier.x == data.Modifier.y ? data.Modifier.x : Sample( data.Modifier );
		if ( data.MaxOverlap > 0 )
		{
			int overlaps = 0;
			foreach ( var existing in parent.Scene.GetAllComponents<PelletMark>() )
			{
				if ( existing.Tag != data.Tag ) continue;
				float distance = existing.Radius + data.RejectionDistance;
				if ( existing.WorldTransform.PointToWorld( existing.Contact ).DistanceSquared( point ) > distance * distance ) continue;
				if ( ++overlaps >= data.MaxOverlap ) return;
			}
		}

		var tangent = Shotgun.Perpendicular( normal );
		var second = Vector3.Cross( normal, tangent ).Normal;
		float angle = Fraction() * System.MathF.Tau;
		var up = second * System.MathF.Sin( angle ) + tangent * System.MathF.Cos( angle );
		var transform = new Transform( point, Rotation.LookAt( -normal, up ) );

		// The original high-word selection multiplies by the last sequence index.
		bool sheet = data.Definition.ColorTexture.SequenceCount > 0;
		int sequence = sheet ? ((int)BlamRandom.Next( ref decalSeed ) * (data.Sizes.Length - 1)) >> 16 : 0;
		float radius = Sample( data.Radius ) * (modifier == 0 ? 1 : modifier);
		// The color draw advances the stream even for these fixed white decal tints.
		Fraction();
		var mark = new GameObject( parent, false, data.Name );
		mark.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		var center = data.Centers[sequence] * radius;
		mark.WorldTransform = transform.WithPosition( transform.Position + transform.Rotation.Left * center.x + transform.Rotation.Up * center.y );
		var footprint = mark.AddComponent<PelletMark>();
		footprint.Tag = data.Tag;
		footprint.Contact = mark.WorldTransform.PointToLocal( point );
		footprint.Radius = radius;
		var decal = mark.AddComponent<Decal>();
		decal.Decals = [data.Definition];
		decal.Transient = true;
		decal.SheetSequence = sheet;
		decal.SequenceId = (uint)sequence;
		decal.Size = data.Sizes[sequence] * radius;
		decal.Rotation = 0;
		decal.LifeTime = data.Lifetime;
		decal.ColorTint = data.Tint;
		decal.SortLayer = 2;
		mark.AddComponent<TemporaryEffect>();
		mark.Enabled = true;
	}
}

[Hide]
internal sealed class PelletMark : Component
{
	internal TagEntry Tag { get; set; }
	internal Vector3 Contact { get; set; }
	internal float Radius { get; set; }
}
