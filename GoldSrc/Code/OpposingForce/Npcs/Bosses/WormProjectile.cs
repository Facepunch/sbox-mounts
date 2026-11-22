using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Hide]
public abstract class WormProjectile : NpcProjectile
{
	internal abstract string SpriteName { get; }
	internal SpriteRenderer Glow { get; private set; }

	protected override void OnStart()
	{
		if ( Sandbox.Mounting.Directory.Get( "opfor" ) is not OpposingForceMount mount || mount.GetIndexedSprite( SpriteName ) is not { } sprite ) return;
		Glow = GameObject.AddComponent<SpriteRenderer>();
		Glow.Sprite = sprite;
		Glow.Additive = true;
		Glow.Size = 24;
	}
}

[Hide]
public sealed class AcidCloud : WormProjectile
{
	internal override string HitTag => "acid";
	internal override string SpriteName => "ballsmoke";

	protected override void OnUpdate()
	{
		if ( !Glow.IsValid() ) return;
		Glow.Size = 24 + 48 * (float)Age;
		Glow.Color = Color.Green.WithAlpha( (1 - (float)Age / Lifetime).Clamp( 0, 1 ) );
	}
}

[Hide]
public sealed class WarpOrb : WormProjectile
{
	internal override string SpriteName => "boss_glow";
	internal override float Lifetime => 6;
	internal override void HandleContact( SceneTraceResult hit ) => Velocity = Vector3.Zero;

	internal override void Guide()
	{
		if ( Age < 2 ) return;
		Velocity = Vector3.Zero;
		if ( Age < 4 ) return;
		var floor = Scene.Trace.Ray( WorldPosition + Vector3.Up * 1000, WorldPosition + Vector3.Down * 1000 )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "npc", "player", "trigger" ).Run();
		if ( floor.Hit && PrefabFile.Load( "mount://opfor/entities/npcs/monster_shocktrooper.prefab" ) is { } prefab )
		{
			var npc = GameObject.Clone( prefab, WorldTransform.WithPosition( floor.EndPosition + Vector3.Up * 48 ) );
			npc.NetworkSpawn( true, null );
		}
		GameObject.Destroy();
	}
}
