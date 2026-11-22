using System;

namespace Doom;

/// <summary>
/// A thing type as a prefab: a sprite playing its spawn animation and a collision box if it's solid,
/// or a spawn point or teleport destination for things that aren't drawn. Built at the mount's
/// scale. Maps place instances and only set what depends on where they stand (sector light, tags).
/// </summary>
public sealed class ThingPrefabLoader( ThingType type, SpriteFrameInfo[] frames, string spritePath ) : ResourceLoader<GameMount>
{
	private PrefabFile _prefab;

	protected override object Load()
	{
		float scale = Host.UnitScale;
		var sprite = spritePath is null ? null : GameResource.Load<Sprite>( spritePath );

		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", type.Name );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;

			var go = new GameObject( true, type.Name );
			var thing = go.AddComponent<DoomThing>();
			thing.Type = type.Id;
			thing.Category = type.Category;

			switch ( type.Category )
			{
				case ThingCategory.PlayerStart:
					go.AddComponent<SpawnPoint>();
					break;

				case ThingCategory.DeathmatchStart:
					go.AddComponent<SpawnPoint>();
					go.Tags.Add( "deathmatch" );
					break;

				case ThingCategory.TeleportDestination:
					go.AddComponent<DoomTeleportDestination>();
					break;
			}

			if ( sprite is not null && frames is { Length: > 0 } )
			{
				var first = frames[0];
				int size = Math.Max( first.Width, first.Height );
				bool bright = (type.Flags & ThingTypeFlags.Bright) != 0;

				var renderer = go.AddComponent<SpriteRenderer>();
				renderer.Sprite = sprite;
				renderer.Size = new Vector2( size, size ) * scale;
				renderer.FlipHorizontal = first.Flipped;
				renderer.TextureFilter = Sandbox.Rendering.FilterMode.Point;
				renderer.Billboard = SpriteRenderer.BillboardMode.YOnly;
				renderer.Opaque = true;
				renderer.IsSorted = true;
				renderer.Shadows = true;

				// Full bright frames ignore sector light; maps tint the rest by their sector's light.
				renderer.Lighting = !bright;

				if ( (type.Flags & ThingTypeFlags.Solid) != 0 )
				{
					var box = go.AddComponent<BoxCollider>();
					box.Scale = new Vector3( first.Width, first.Width, first.Height ) * scale;
					box.Center = new Vector3( 0, 0, first.Height * 0.5f * scale );
					box.Static = true;
				}
			}
		}

		return _prefab = builder.Create()
			?? throw new InvalidOperationException( $"{Path}: could not create the thing prefab." );
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( _prefab );
		_prefab = null;
		base.Shutdown();
	}
}
