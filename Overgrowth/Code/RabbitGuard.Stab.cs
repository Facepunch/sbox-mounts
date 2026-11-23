using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal void Stab( Vector3 origin, Vector3 direction, bool light )
	{
		if ( direction.IsNearlyZero() || CharacterLoader.FromModel( Renderer.Model )?.Surface is not { } surface ) return;
		var faces = surface.StabFaces( Renderer, origin, direction, out var posed, out float size );
		if ( faces.Count == 0 ) return;

		// BloodSurface projects the original decal along the strike, with a random rotation around it.
		Vector3 jitter = new( Random.Shared.Float( -1f, 1f ), Random.Shared.Float( -1f, 1f ), Random.Shared.Float( -1f, 1f ) );
		var x = direction.Cross( jitter ).Normal;
		var y = x.Cross( direction ).Normal;
		var projection = new Vector4[faces.Count * 3];
		var clouds = new List<Vector3>();
		var splats = new List<Vector3>();
		for ( int i = 0; i < faces.Count; i++ )
		{
			Vector3 sum = default;
			for ( int j = 0; j < 3; j++ )
			{
				var (position, uv) = surface.CornerAt( faces[i] + j, posed );
				sum += position;
				var offset = position - origin;
				projection[i * 3 + j] = new( uv.x, uv.y, offset.Dot( x ) / size + 0.5f, 0.5f - offset.Dot( y ) / size );
			}
			if ( Random.Shared.Next( faces.Count ) == 0 ) clouds.Add( sum / 3f );
			if ( light && Random.Shared.Next( 40 ) == 0 ) splats.Add( sum / 3f );
		}
		Renderer.GetOrAddComponent<CutMarks>().DrawStab( projection );
		if ( clouds.Count + splats.Count > 0 ) PlayStabParticles( clouds.ToArray(), splats.ToArray(), direction );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayStabParticles( Vector3[] clouds, Vector3[] splats, Vector3 direction )
	{
		if ( BloodLevel == 0 || Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		foreach ( var point in clouds )
		{
			mount.EmitBillboard( "Data/Particles/bloodcloud.xml", point, (ParticleScatter() + direction) * 40f, BloodColor );
			mount.EmitBillboard( "Data/Particles/bloodcloud.xml", point, (ParticleScatter() - direction) * 40f, BloodColor );
		}
		foreach ( var point in splats ) mount.EmitBillboard( "Data/Particles/bloodsplat.xml", point, ParticleScatter() * 80f, BloodColor );
	}

	static Vector3 ParticleScatter() => new( Random.Shared.Float( -1f, 1f ), Random.Shared.Float( -1f, 1f ), Random.Shared.Float( -1f, 1f ) );
}
