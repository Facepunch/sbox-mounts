using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	float waterSprayClock;
	readonly Queue<(double Time, Vector3 Surface)> delayedSplashes = new();

	void UpdateWaterSpray( WaterHotspot water, Vector3 center, Vector3 head, float height, Vector3 velocity, float dt )
	{
		const float step = 1f / 120f;
		waterSprayClock += dt;
		if ( waterSprayClock < step ) return;
		float flatSpeed = velocity.WithZ( 0f ).Length / 40f;
		float verticalSpeed = MathF.Abs( velocity.z ) / 40f;
		bool burst = LegRadius / 40f - height < 2f && velocity.LengthSquared > 160f * 160f &&
			(!IsOnGround || Downed || RollProgress < 1f);
		float bodyHeight = Math.Max( head.z - center.z, 0f ) / 40f + LegRadius / 40f + 0.3f;
		bool wake = height > -1f && (flatSpeed > 2f || verticalSpeed > 1f) && height > 0.6f - bodyHeight;
		var surface = center - water.WorldRotation.Up * (height * 40f);
		List<WaterHotspot.Drop> drops = null;
		while ( waterSprayClock >= step )
		{
			waterSprayClock -= step;
			double now = Time.NowDouble - waterSprayClock;
			if ( burst )
			{
				Ring( surface, 80f, 5f );
				if ( Random.Shared.Next( 2 ) == 0 ) delayedSplashes.Enqueue( (now + 0.2, surface) );
			}
			while ( delayedSplashes.TryPeek( out var splash ) && splash.Time < now )
			{
				delayedSplashes.Dequeue();
				Ring( splash.Surface, 100f, -3f );
			}
			if ( wake )
			{
				var offset = new Vector3( Random.Shared.Float( -4f, 4f ), Random.Shared.Float( -4f, 4f ), -8f );
				(drops ??= []).Add( new( surface + offset, Vector3.Up * (40f * Math.Min( 3f, flatSpeed * 0.3f + verticalSpeed * 0.4f )) ) );
			}
		}
		if ( drops is not null ) water.EmitSpray( drops.ToArray() );

		void Ring( Vector3 origin, float lift, float spread )
		{
			var offset = Rotation.FromYaw( Random.Shared.Float( 0f, 360f ) ).Forward * 12f;
			(drops ??= []).Add( new( origin + offset, Vector3.Up * lift + offset * spread ) );
		}
	}
}

public sealed partial class WaterHotspot
{
	internal record struct Drop( Vector3 Position, Vector3 Velocity );
	ParticleEffect spray;

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	internal void EmitSpray( Drop[] drops )
	{
		if ( Application.IsDedicatedServer || Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		if ( !spray.IsValid() )
		{
			var definition = mount.GetBillboard( "Data/Particles/watersplash.xml" );
			var obj = new GameObject( GameObject, false, "Overgrowth water spray" ) { NetworkMode = NetworkMode.Never };
			spray = obj.AddComponent<ParticleEffect>();
			spray.Lifetime = definition.MaxAlpha / definition.OpacityDecay;
			var renderer = obj.AddComponent<ParticleSpriteRenderer>();
			renderer.Sprite = definition.Sprite;
			renderer.Lighting = true;
			renderer.DepthFeather = (definition.MinSize + definition.MaxSize) * 20f;
			renderer.CameraFadeNear = 8f;
			renderer.CameraFadeFar = 88f;
			float step = 0f, now = 0f;
			spray.OnPreStep = dt => { step = dt; now = Time.Now; };
			spray.OnStep = ( p, _ ) =>
			{
				float diameter = MathX.Lerp( definition.MinSize, definition.MaxSize, p.Rand( 1 ) ) * 40f;
				p.Size = diameter - 80f * definition.SizeDecay * p.Age;
				float alpha = MathX.Lerp( definition.MinAlpha, definition.MaxAlpha, p.Rand( 2 ) ) - definition.OpacityDecay * p.Age;
				p.Alpha = alpha >= 0.5f ? alpha * 0.5f : 0f;
				p.Angles.roll += (p.Rand( 3 ) * 720f - 360f) * 0.3f * diameter / p.Size.x * step;
				p.Velocity += (Vector3.Down * (392f * definition.Gravity) + BillboardParticle.Wind( p.Position / 40f, now ) * (40f * definition.Wind)) * step;
			};
			obj.Enabled = true;
		}

		foreach ( var drop in drops )
		{
			var p = spray.Emit( drop.Position, 0f );
			p.Alpha = 0f;
			p.Velocity = drop.Velocity;
			p.Angles.roll = Random.Shared.Float( -360f, 360f );
		}
	}
}
