using System;

namespace Source1;

/// <summary>Places converted mount effects; gameplay damage stays on the owning prefab.</summary>
[Title( "Source Mounted Effect" )]
[Category( "Source 1" )]
[Alias( "Source1.SourceMountedEffect", "SourceMountedEffect" )]
public sealed class MountedEffect : Component
{
	[Property] public string MountName { get; set; } = "hl2";
	[Property] public string Particle { get; set; }
	[Property] public string Sound { get; set; }

	protected override async void OnStart()
	{
		if ( Application.IsDedicatedServer ) return;
		try
		{
			await Sandbox.Mounting.Directory.Mount( MountName );
			if ( !IsValid ) return;
			MountedEffects.Attach( Particle, GameObject, MountName );
			if ( Sounds.Sound( Sound, MountName ) is { } sound ) GameObject.PlaySound( sound );
		}
		catch ( Exception e ) { Log.Warning( $"Mounted effect '{Particle}': {e.Message}" ); }
	}
}
