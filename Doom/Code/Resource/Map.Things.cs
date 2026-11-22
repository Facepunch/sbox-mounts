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

	/// <summary>
	/// Places an instance of the thing type's mounted prefab, then sets what depends on where it
	/// stands: riding a movable plane, the sector's light, a teleport destination's tag.
	/// </summary>
	protected virtual void SpawnThing( Thing thing, ThingType type, GameObject parent )
	{
		if ( GameMount.GetThingOwner( Wads, type ) is not { } owner ) return;

		var prefab = PrefabFile.Load( Host.Url( GameMount.ThingPrefabPath( owner.Name, type ) ) );
		if ( prefab is null ) return;

		int sector = SectorAt( thing.X, thing.Y );
		bool hanging = (type.Flags & ThingTypeFlags.Hanging) != 0;
		bool moving = sector >= 0 && _dynamic[sector];

		if ( moving )
			parent = hanging ? _ceilingObjects[sector] : _floorObjects[sector];

		float z = sector < 0 ? 0 : hanging ? _sectors[sector].CeilingHeight - type.Height : _sectors[sector].FloorHeight;
		var go = GameObject.Clone( prefab, Transform.Zero, parent );
		if ( !go.IsValid() ) return;

		// Clone treats a transform as local to the parent, and movable planes sit away from the origin,
		// so place the thing in world space once it's parented.
		go.WorldPosition = World( thing.X, thing.Y, z );
		go.WorldRotation = Rotation.FromYaw( thing.Angle );

		if ( sector >= 0 && go.Components.TryGet<DoomTeleportDestination>( out _ ) )
			go.Tags.Add( DoomTeleportDestination.TagFor( _sectors[sector].Tag ) );

		// Things on a moving floor or ceiling need a collider that can follow it.
		if ( moving && go.Components.TryGet<BoxCollider>( out var collider ) )
			collider.Static = false;

		// Full bright frames ignore sector light. Others take the sector's light step, darkened
		// the same way the world shader darkens walls.
		if ( sector >= 0 && (type.Flags & ThingTypeFlags.Bright) == 0 && go.Components.TryGet<SpriteRenderer>( out var renderer ) )
		{
			float light = MathF.Floor( _lights[sector].Max / 16f ) * 16f / 255f;
			renderer.Color = new Color( light * light, light * light, light * light, 1f );
		}
	}
}
