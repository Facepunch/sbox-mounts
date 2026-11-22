namespace GoldSrc.HalfLife;

internal sealed class ClassicBounce( Vector3 velocity, Angles spin, float gravityScale, float friction, BBox? hull = null )
{
	[ConVar( "hl1_classic_physics", ConVarFlags.Replicated | ConVarFlags.Server, Help = "Use GoldSrc bounce physics for newly spawned Half-Life debris and throwables." )]
	public static bool Enabled { get; set; }

	internal const float Gravity = 800;
	internal static Vector3 GravityFor( Scene scene, bool classic ) => classic ? Vector3.Down * Gravity : scene.PhysicsWorld.Gravity;
	const float SurfaceGap = 0.03125f;
	const float SettleSpeed = 30;

	internal Vector3 Velocity = velocity;
	internal Angles Spin = spin;
	internal float GravityScale = gravityScale;
	internal bool Grounded { get; private set; }
	internal bool Settled => Grounded && Velocity.IsNearlyZero();

	internal SceneTraceResult Step( GameObject owner, GameObject ignore = null, bool worldOnly = false, bool shell = false )
	{
		var position = owner.WorldPosition;
		var delta = Time.Delta;
		var gravity = Gravity * GravityScale * delta;
		if ( Grounded )
			Grounded = Velocity.z <= 0 && Trace( position, position + Vector3.Down * 2 ).Hit;
		if ( Settled ) return default;
		var movement = Velocity;
		if ( !Grounded && !shell ) movement.z -= gravity * 0.5f;
		var hit = Trace( position, position + movement * delta );
		if ( !Grounded ) Velocity.z -= gravity;
		var angles = owner.WorldRotation.Angles() + Spin * delta;
		if ( hit.StartedSolid )
		{
			Velocity = Vector3.Zero;
			Spin = default;
			Grounded = true;
			return default;
		}
		owner.WorldPosition = hit.EndPosition;
		if ( hit.Hit )
		{
			owner.WorldPosition += hit.Normal * SurfaceGap;
			if ( shell )
			{
				var stop = hit.Normal.z > 0.9f && movement.z <= 0 && movement.z >= -gravity * 3;
				Grounded = stop;
				Velocity = stop ? Vector3.Zero : Vector3.Reflect( movement, hit.Normal ) * 0.5f + Vector3.Down * gravity;
				angles = stop ? new Angles( 0, angles.yaw, 0 ) : new Angles( angles.pitch, -angles.yaw, angles.roll ) * 0.9f;
			}
			else
			{
				// GoldSrc MOVETYPE_BOUNCE clips the normal component using 2 minus entity friction.
				Velocity = ClipVelocity( Velocity, hit.Normal, 2 - friction );
				if ( hit.Normal.z > 0.7f )
				{
					if ( Velocity.LengthSquared < SettleSpeed * SettleSpeed )
					{
						Grounded = true;
						Velocity = Vector3.Zero;
						Spin = default;
					}
					else if ( Velocity.z < Gravity * delta )
					{
						Grounded = true;
						Velocity = Velocity.WithZ( 0 );
					}
					if ( !Settled )
					{
						var remainder = Trace( owner.WorldPosition, owner.WorldPosition + Velocity * ((1 - hit.Fraction) * delta * 0.9f) );
						if ( !remainder.StartedSolid ) owner.WorldPosition = remainder.EndPosition;
					}
				}
			}
		}
		if ( Grounded && !shell && !Settled )
		{
			var speed = Velocity.Length;
			var drop = System.MathF.Max( speed, 100 ) * 4 * friction * delta;
			Velocity *= System.MathF.Max( 0, speed - drop ) / speed;
		}
		owner.WorldRotation = angles.ToRotation();
		return hit;

		SceneTraceResult Trace( Vector3 start, Vector3 end )
		{
			var scene = owner.Scene;
			var trace = hull is { } box ? scene.Trace.Box( box, start, end ) : scene.Trace.Ray( start, end );
			trace = trace.IgnoreGameObjectHierarchy( owner ).WithoutTags( "trigger", "debris" );
			if ( ignore.IsValid() ) trace = trace.IgnoreGameObjectHierarchy( ignore );
			if ( worldOnly ) trace = trace.IgnoreDynamic().WithoutTags( "npc", "player", "playercontroller" );
			return trace.Run();
		}
	}

	internal static Vector3 ClipVelocity( Vector3 velocity, Vector3 normal, float overbounce )
	{
		static float StopSmall( float value ) => System.MathF.Abs( value ) < 0.1f ? 0 : value;
		var clipped = velocity - normal * (velocity.Dot( normal ) * overbounce);
		return new Vector3( StopSmall( clipped.x ), StopSmall( clipped.y ), StopSmall( clipped.z ) );
	}
}
