using System;

namespace Overgrowth;

public sealed partial class CutMarks
{
	readonly List<CharacterSurface.Drip> drips = new();
	float dripClock;

	internal void AddDrip( Vector3 point )
	{
		var renderer = GetComponent<SkinnedModelRenderer>();
		if ( CharacterLoader.FromModel( renderer.Model )?.Surface is not { } surface ) return;
		WakeDrying();
		var posed = surface.Pose( renderer );
		if ( posed.Length > 0 && surface.NearestDrip( posed, point ) is { } drip )
			drips.Add( drip );
	}

	internal void AddHeadDrip( CharacterLoader character, Vector3 direction )
	{
		WakeDrying();
		if ( character.InitialChainCenter( "head", 1 ) is { } origin && character.Surface.DirectedDrip( origin, direction ) is { } drip )
			drips.Add( drip );
	}

	protected override void OnUpdate()
	{
		if ( texture is not null && DryTick != displayedDryTick ) SetDryTick( GetComponent<ModelRenderer>() );
		if ( drips.Count == 0 ) return;
		const float step = 4f / 120f;
		dripClock += Time.Delta;
		if ( dripClock < step ) return;
		var renderer = GetComponent<SkinnedModelRenderer>();
		if ( CharacterLoader.FromModel( renderer.Model )?.Surface is not { } surface ) return;
		var posed = surface.Pose( renderer );
		if ( posed.Length == 0 ) return;
		var lines = new List<Vector4>();
		while ( dripClock >= step && drips.Count > 0 )
		{
			dripClock -= step;
			for ( int i = drips.Count - 1; i >= 0; i-- )
			{
				var drip = drips[i];
				if ( drip.Amount < 0f ) { drips.RemoveAt( i ); continue; }
				if ( !surface.AdvanceDrip( drip, posed, lines ) )
				{
					drips.RemoveAt( i );
					if ( drip.CanDrip && Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount )
						mount.EmitBillboard( "Data/Particles/blooddrop.xml", surface.DripPosition( drip, posed ), Vector3.Zero, RabbitGuard.BloodColor );
				}
				drip.Amount -= 1f / 120f;
			}
		}
		if ( drips.Count == 0 ) dripClock = 0f;
		if ( lines.Count > 0 ) PaintLines( lines, Random.Shared.Next() );
	}
}
