namespace GoldSrc.HalfLife;

internal static class GroundShake
{
	internal static void Emit( Scene scene, Vector3 center, float amplitude, float frequency, float duration, float radius )
	{
		foreach ( var player in scene.GetAllComponents<PlayerController>() )
		{
			if ( radius > 0 && player.WorldPosition.DistanceSquared( center ) >= radius * radius ) continue;
			using ( Rpc.FilterInclude( player.Network.Owner ?? Connection.Local ) )
				ShakeCamera( player, amplitude, frequency, duration );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ShakeCamera( PlayerController player, float amplitude, float frequency, float duration )
	{
		if ( !player.IsValid() || player.IsProxy || !player.IsOnGround || player.Scene.Camera is not { } camera ) return;
		// Half-Life transmits amplitude as unsigned 4.12 fixed point.
		camera.AddShake( System.MathF.Min( amplitude, ushort.MaxValue / 4096f ), frequency, duration );
	}
}
