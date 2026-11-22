namespace GoldSrc.HalfLife;

internal static partial class BloodEffects
{
	static readonly TagSet SpatterIgnore = new( ["npc", "player", "playercontroller", "ragdoll", "debris", "trigger"] );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void Spray( Npc.BloodColor color, Vector3 point, int damage )
	{
		if ( HalfLifeMount.SharedAssets is not { IsMounted: true } mount ) return;
		var size = System.Math.Clamp( damage * (Connection.All.Count > 1 ? 2 : 1) / 10, 3, 16 );
		var shade = (int)color + Game.Random.Int( 1, 3 );
		if ( mount.GetIndexedSprite( "bloodspray" ) is { } mist )
		{
			var go = new GameObject( false, "Blood spray" );
			go.NetworkMode = NetworkMode.Never;
			go.WorldPosition = point + Vector3.Up * Game.Random.Float( 2, 4 );
			go.WorldRotation = Rotation.FromRoll( Game.Random.Float( 0, 360 ) );
			var sprite = go.AddComponent<SpriteRenderer>();
			sprite.Sprite = mist;
			sprite.Color = mount.PaletteColor( shade ).WithAlpha( 250f / 255 );
			var anim = mist.Animations[0];
			var texture = anim.Frames[0].Texture;
			sprite.Size = new Vector2( texture.Width, texture.Height ) * Game.Random.Float( size / 35f, size / 25f );
			sprite.PlaybackSpeed = (anim.Frames.Count - 1) * 4f / anim.FrameRate;
			go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0.25f;
			go.Enabled = true;
		}

		if ( mount.GetIndexedSpriteFrames( "blood" ) is not { Length: > 0 } frames ) return;
		var groups = new ParticleEffect[frames.Length];
		var tint = mount.PaletteColor( shade - 1 );
		var count = size + Game.Random.Int( 1, 8 ) + Game.Random.Int( 1, 8 );
		for ( int i = 0; i < count; i++ )
		{
			var index = frames.Length > 1 ? Game.Random.Int( 1, frames.Length - 1 ) : 0;
			if ( index > 8 ) index = frames.Length - 1;
			var group = groups[index] ??= CreateSpatter( frames[index], point, size, tint );
			if ( group.Emit( point, 0 ) is { } drop )
				drop.Velocity = new Vector3( Game.Random.Float( -96, 95 ), Game.Random.Float( -96, 95 ), Game.Random.Float( -32, 95 ) );
		}
	}

	static ParticleEffect CreateSpatter( Sprite frame, Vector3 point, int size, Color tint )
	{
		var go = new GameObject( false, "Blood droplets" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = point;
		var effect = go.AddComponent<ParticleEffect>();
		effect.Lifetime = new ParticleFloat( 1, 3 );
		effect.ApplyShape = true;
		var extent = frame.Animations[0].Frames[0].Texture.Width * size * 0.5f;
		effect.InitialScale = new ParticleFloat( extent / 25, extent / 15 );
		effect.ApplyColor = true;
		effect.Tint = tint;
		effect.ApplyAlpha = true;
		effect.Alpha = 250f / 255;
		effect.ApplyRotation = true;
		effect.InitialRoll = new ParticleFloat( 0, 360 );
		effect.AngularVelocity = new Vector3( 0, 0, Game.Random.Float( -256, -255 ) );
		effect.Force = true;
		effect.ForceDirection = go.Scene.PhysicsWorld.Gravity * 0.5f;
		effect.Collision = true;
		effect.CollisionIgnore = SpatterIgnore;
		effect.CollisionRadius = 0.01f;
		effect.Bounce = 0;
		go.AddComponent<ParticleSpriteRenderer>().Sprite = frame;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0;
		go.Enabled = true;
		return effect;
	}
}
