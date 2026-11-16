using System;
using Doom.Wad;
using Thing = Doom.Wad.Thing;

namespace Doom;

/// <summary>
/// Things: player starts, teleport destinations, and everything else as a sprite playing its
/// spawn animation. A thing standing in a movable sector rides its floor (or ceiling, if it hangs).
/// </summary>
partial class MapLoader
{
	/// <summary>Skill whose things are spawned. Default is Hurt Me Plenty, the game's default.</summary>
	protected virtual ThingFlags Skill => ThingFlags.Medium;

	/// <summary>Whether a thing appears. Default follows vanilla single player: its skill, and not multiplayer-only.</summary>
	protected virtual bool ShouldSpawn( Thing thing, ThingType type )
	{
		if ( type.Category is ThingCategory.PlayerStart or ThingCategory.DeathmatchStart ) return true;

		// temp, these are all useless atm
		if ( type.Category
			is ThingCategory.Monster
			or ThingCategory.Health
			or ThingCategory.Armor
			or ThingCategory.Key
			or ThingCategory.Powerup
			or ThingCategory.Weapon
			or ThingCategory.Ammo )
			return false;

		if ( (thing.Flags & ThingFlags.MultiplayerOnly) != 0 ) return false;
		return (thing.Flags & Skill) != 0;
	}

	private void SpawnThings()
	{
		var table = Host.ThingTable;
		var unknown = new SortedSet<int>();
		var parent = new GameObject( true, "Things" ) { Parent = Root };

		foreach ( var thing in Map.Things )
		{
			if ( !table.TryGetValue( thing.Type, out var type ) )
			{
				unknown.Add( thing.Type );
				continue;
			}

			if ( ShouldSpawn( thing, type ) )
				SpawnThing( thing, type, parent );
		}

		if ( unknown.Count > 0 )
			Log.Info( $"Doom: {Map.Name} has things of unknown types: {string.Join( ", ", unknown )}" );
	}

	protected virtual void SpawnThing( Thing thing, ThingType type, GameObject parent )
	{
		int sector = SectorAt( thing.X, thing.Y );
		bool hanging = (type.Flags & ThingTypeFlags.Hanging) != 0;

		var go = new GameObject( true, type.Name ) { Parent = parent };
		if ( sector >= 0 && _dynamic[sector] )
			go.Parent = hanging ? _ceilingObjects[sector] : _floorObjects[sector];

		float z = sector < 0 ? 0 : hanging ? _sectors[sector].CeilingHeight - type.Height : _sectors[sector].FloorHeight;
		go.WorldPosition = World( thing.X, thing.Y, z );
		go.WorldRotation = Rotation.FromYaw( thing.Angle );

		switch ( type.Category )
		{
			case ThingCategory.PlayerStart:
				go.AddComponent<SpawnPoint>();
				return;

			case ThingCategory.DeathmatchStart:
				go.AddComponent<SpawnPoint>();
				go.Tags.Add( "deathmatch" );
				return;

			case ThingCategory.TeleportDestination:
				var destination = go.AddComponent<DoomTeleportDestination>();
				destination.SectorTag = sector >= 0 ? _sectors[sector].Tag : 0;
				Level.TeleportDestinations.Add( destination );
				return;
		}

		if ( (type.Flags & ThingTypeFlags.Solid) != 0 )
		{
			var box = go.AddComponent<BoxCollider>();
			box.Scale = new Vector3( type.Radius * 2, type.Radius * 2, type.Height ) * _scale;
			box.Center = new Vector3( 0, 0, type.Height * 0.5f * _scale );
			box.Static = go.Parent == parent;
		}

		AddSprite( go, type, sector );
	}

	private void AddSprite( GameObject go, ThingType type, int sector )
	{
		if ( !GameMount.TryGetThingSprite( Wads, type, out var owner, out var frames ) )
			return;

		var sprite = GameResource.Load<Sprite>( Host.Url( GameMount.ThingPath( owner.Name, type ) ) );
		if ( sprite is null ) return;

		var first = frames[0];
		int size = Math.Max( first.Width, first.Height );

		var renderer = go.AddComponent<SpriteRenderer>();
		renderer.Sprite = sprite;
		renderer.Size = new Vector2( size, size ) * _scale;
		renderer.FlipHorizontal = first.Flipped;
		renderer.TextureFilter = Sandbox.Rendering.FilterMode.Point;
		renderer.Billboard = SpriteRenderer.BillboardMode.YOnly;
		renderer.Opaque = true;
		renderer.IsSorted = true;
		renderer.Shadows = true;

		// Full bright frames ignore sector light. Others take the sector's light step, darkened
		// the same way the world shader darkens walls.
		bool bright = (type.Flags & ThingTypeFlags.Bright) != 0;
		renderer.Lighting = !bright;
		if ( !bright && sector >= 0 )
		{
			float light = MathF.Floor( _lights[sector].Max / 16f ) * 16f / 255f;
			renderer.Color = new Color( light * light, light * light, light * light, 1f );
		}

		if ( go.Components.TryGet<BoxCollider>( out var collider ) )
		{
			collider.Scale = new Vector3( first.Width, first.Width, first.Height ) * _scale;
			collider.Center = new Vector3( 0, 0, first.Height * 0.5f * _scale );
		}
	}
}
