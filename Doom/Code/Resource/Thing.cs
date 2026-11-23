using System;
using Doom.Wad;

namespace Doom;

/// <summary>
/// A thing type as a prefab: a sprite playing its spawn animation and a collision box if it's solid,
/// or a spawn point or teleport destination for things that aren't drawn. Built at the mount's
/// scale. Maps place instances and only set what depends on where they stand (sector light, tags).
/// </summary>
public sealed class ThingPrefabLoader( WadStack stack, ThingType type, SpriteFrameInfo[] frames, SpriteCanvas? canvas, string spritePath ) : ResourceLoader<GameMount>
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

			switch ( type.Category )
			{
				case ThingCategory.PlayerStart:
					{
						var spawn = go.AddComponent<SpawnPoint>();
						spawn.Priority = 4 - (type.Id - 1);
						break;
					}

				case ThingCategory.DeathmatchStart:
					{
						var spawn = go.AddComponent<SpawnPoint>();
						go.Tags.Add( "deathmatch" );
						break;
					}

				case ThingCategory.TeleportDestination:
					go.AddComponent<DoomTeleportDestination>();
					break;

			}

			if ( type.Name == "barrel" )
			{
				var rigidbody = go.AddComponent<Rigidbody>();
				rigidbody.Locking = new PhysicsLock()
				{
					Pitch = true,
					Yaw = true,
					Roll = true
				};
				rigidbody.MassOverride = 30f;
			}

			if ( sprite is not null && frames is { Length: > 0 } && canvas is { } c )
			{
				var first = frames[0];
				bool bright = (type.Flags & ThingTypeFlags.Bright) != 0;

				// The renderer fits the texture into Size keeping its aspect ratio, so a square of the
				// canvas's longest side gives one texel per map unit. Every frame shares the canvas, so
				// they all line up on the origin.
				var renderer = go.AddComponent<SpriteRenderer>();
				renderer.Sprite = sprite;
				renderer.Size = new Vector2( Math.Max( c.Width, c.Height ) ) * scale;
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
					box.Static = !go.Components.TryGet<Rigidbody>( out _ );
				}
			}

			if ( (type.Flags & ThingTypeFlags.Explosive) != 0 )
			{
				var barrel = go.AddComponent<DoomExplosiveBarrel>();
				barrel.Health = type.Health;
				barrel.Radius = 128f * scale;
				barrel.ExplodeSound = LoadSound( "DSBAREXP" );
			}
		}

		return _prefab = builder.Create()
			?? throw new InvalidOperationException( $"{Path}: could not create the thing prefab." );
	}

	private SoundEvent LoadSound( string lump )
	{
		if ( stack.FindSound( lump, out var owner ) is null ) return null;
		return GameResource.Load<SoundEvent>( Host.Url( GameMount.SoundEventPath( owner.Name, lump ) ) );
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( _prefab );
		_prefab = null;
		base.Shutdown();
	}
}
