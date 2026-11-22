using GoldSrc.HalfLife;

namespace GoldSrc.Maps;

public sealed class Funnel : MapEntity
{
	[Property] public bool Reverse { get; set; }

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || !Networking.IsHost ) return;
		Emit( WorldPosition, Reverse );
		Kill();
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void Emit( Vector3 origin, bool reverse )
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ) return;
		var go = new GameObject( false, "Funnel particles" ) { WorldPosition = origin, NetworkMode = NetworkMode.Never };
		var effect = go.AddComponent<ParticleEffect>();
		effect.MaxParticles = 256;
		effect.InitialScale = 2;
		go.AddComponent<ParticleSpriteRenderer>().Sprite = PaletteParticle.Sprite;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0;
		go.Enabled = true;

		var color = mount.PaletteColor( 244 );
		for ( int x = -8; x < 8; x++ )
		for ( int y = -8; y < 8; y++ )
		{
			var offset = new Vector3( x * 32, y * 32, Game.Random.Float( 100, 800 ) );
			var speed = System.Math.Max( (origin.z + offset.z) / 8, 64 ) + Game.Random.Float( 64, 128 );
			effect.Lifetime = offset.Length / speed;
			if ( effect.Emit( reverse ? origin : origin + offset, 0 ) is not {} particle ) continue;
			particle.Velocity = offset.Normal * (reverse ? speed : -speed);
			particle.Color = color;
		}
	}
}
