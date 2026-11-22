namespace GoldSrc.HalfLife;

[Hide]
public sealed class AcidMortar : NpcProjectile
{
	TimeUntil _nextSpray;
	internal override string HitTag => "acid";
	internal override void PrepareLaunch()
	{
		BroadcastSpray( WorldPosition, Vector3.Up, 24 );
		_nextSpray = 0.4f;
	}

	internal override void Guide()
	{
		Velocity += FlightGravity * Time.Delta;
		if ( _nextSpray > 0 ) return;
		_nextSpray = 0.2f;
		BroadcastSpray( WorldPosition, -Velocity.Normal, 3 );
	}

	protected override void OnStart()
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount || mount.GetIndexedSprite( "mommaspit" ) is not { } spit ) return;
		var renderer = GameObject.AddComponent<SpriteRenderer>();
		renderer.Sprite = spit;
		var texture = spit.Animations[0].Frames[0].Texture;
		renderer.Size = new Vector2( texture.Width, texture.Height ) * 2.5f;
	}

	internal override void OnImpact( IDamageable direct, Vector3 position, Vector3 normal )
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
		{
			var pitch = Game.Random.Float( 0.9f, 1.1f );
			mount.NpcAudio.Emit( "bullchicken/bc_acid1.wav", position, pitch: pitch );
			mount.NpcAudio.Emit( "bullchicken/bc_spithit", position, pitch: pitch );
		}
		Impact( position, direct is null ? normal : -Velocity.Normal, direct is null );
		var center = position + normal;
		Blast.DamageInRadius( GameObject, Shooter, center, Damage, 250, "acid" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Impact( Vector3 position, Vector3 normal, bool mark )
	{
		Spray( position, normal, 24 );
		if ( mark ) ImpactDecal.Place( position, normal, "mommablob", 0 );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void BroadcastSpray( Vector3 position, Vector3 direction, int count ) => Spray( position, direction, count );

	static void Spray( Vector3 position, Vector3 direction, int count )
		=> SpriteSpray.Emit( "Mortar spray", "mommaspout", position, direction, count, 130, 0.8f );
}
