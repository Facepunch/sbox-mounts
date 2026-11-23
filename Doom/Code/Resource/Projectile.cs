using System;
using Doom.Wad;

namespace Doom;

/// <summary>
/// A missile as a prefab: its full bright sprite flying, and a <see cref="DoomProjectile"/> that moves
/// it, hurts what it hits and plays its explosion. Built at the mount's scale.
/// </summary>
public sealed class ProjectilePrefabLoader( WadStack stack, ProjectileInfo info, SpriteCanvas canvas, string spritePath ) : ResourceLoader<GameMount>
{
	private PrefabFile _prefab;

	protected override object Load()
	{
		float scale = Host.UnitScale;
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", info.Name );

		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;

			var go = new GameObject( true, info.Name );
			go.NetworkMode = NetworkMode.Object;
			go.Tags.Add( "projectile" );

			var renderer = EffectPrefabLoader.AddRenderer( go, spritePath, canvas, scale );

			var projectile = go.AddComponent<DoomProjectile>();
			projectile.Renderer = renderer;
			projectile.Scale = scale;
			projectile.Homing = info.Homing;
			projectile.Splash = info.Splash;
			projectile.Speed = info.Speed * 35f * scale;
			projectile.Damage = info.Damage;
			projectile.Radius = info.Radius * scale;
			projectile.Height = info.Height * scale;
			projectile.ExplosionTime = info.Explosion.Duration;
			projectile.LaunchSound = LoadSound( info.LaunchSound );
			projectile.ExplodeSound = LoadSound( info.ExplodeSound );
		}

		return _prefab = builder.Create()
			?? throw new InvalidOperationException( $"{Path}: could not create the projectile prefab." );
	}

	private SoundEvent LoadSound( string lump )
	{
		if ( lump is null || stack.FindSound( lump, out var owner ) is null ) return null;
		return GameResource.Load<SoundEvent>( Host.Url( GameMount.SoundEventPath( owner.Name, lump ) ) );
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( _prefab );
		_prefab = null;
		base.Shutdown();
	}
}

/// <summary>
/// The Icon of Sin's spawn cube as a prefab: its full bright sprite turning, and a
/// <see cref="DoomBrainCube"/> carrying what can arrive where it lands.
/// </summary>
public sealed class BrainCubePrefabLoader( WadStack stack, SpriteCanvas canvas, string spritePath ) : ResourceLoader<GameMount>
{
	private PrefabFile _prefab;

	protected override object Load()
	{
		float scale = Host.UnitScale;
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", BrainCube.Type.Name );

		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;

			var go = new GameObject( true, BrainCube.Type.Name );
			go.NetworkMode = NetworkMode.Object;

			EffectPrefabLoader.AddRenderer( go, spritePath, canvas, scale );

			var cube = go.AddComponent<DoomBrainCube>();
			cube.Speed = BrainCube.Speed * 35f * scale;
			cube.FirePrefab = LoadPrefab( HitEffects.SpawnFire );
			cube.FlySound = LoadSound( "DSBOSCUB" );
			cube.ArriveSound = LoadSound( "DSTELEPT" );

			foreach ( var (below, id) in BrainCube.Spawns )
			{
				cube.Thresholds.Add( below );
				cube.Monsters.Add( Host.ThingTable.TryGetValue( id, out var type ) ? LoadPrefab( type ) : null );
			}
		}

		return _prefab = builder.Create()
			?? throw new InvalidOperationException( $"{Path}: could not create the spawn cube prefab." );
	}

	private PrefabFile LoadPrefab( ThingType type )
		=> Host.ThingPrefabUrl( type ) is { } url ? PrefabFile.Load( url ) : null;

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

/// <summary>
/// A sprite that plays through once where it's spawned and goes, such as a bullet puff. Each client
/// spawns its own, so it isn't networked.
/// </summary>
public sealed class EffectPrefabLoader( ThingType type, SpriteCanvas canvas, string spritePath ) : ResourceLoader<GameMount>
{
	private PrefabFile _prefab;

	protected override object Load()
	{
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", type.Name );

		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;

			var go = new GameObject( true, type.Name );
			go.NetworkMode = NetworkMode.Never;

			var renderer = AddRenderer( go, spritePath, canvas, Host.UnitScale );
			renderer.Lighting = (type.Flags & ThingTypeFlags.Bright) == 0;

			go.AddComponent<DoomEffect>().Lifetime = type.Frames.Length * type.Tics / 35f;
		}

		return _prefab = builder.Create()
			?? throw new InvalidOperationException( $"{Path}: could not create the effect prefab." );
	}

	/// <summary>A point-filtered billboard of the sprite at one texel per map unit, full bright, casting no shadow.</summary>
	internal static SpriteRenderer AddRenderer( GameObject go, string spritePath, SpriteCanvas canvas, float scale )
	{
		var renderer = go.AddComponent<SpriteRenderer>();
		renderer.Sprite = GameResource.Load<Sprite>( spritePath );
		renderer.Size = new Vector2( Math.Max( canvas.Width, canvas.Height ) ) * scale;
		renderer.TextureFilter = Sandbox.Rendering.FilterMode.Point;
		renderer.Billboard = SpriteRenderer.BillboardMode.YOnly;
		renderer.Opaque = true;
		renderer.IsSorted = true;
		renderer.Shadows = false;
		renderer.Lighting = false;
		return renderer;
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( _prefab );
		_prefab = null;
		base.Shutdown();
	}
}
