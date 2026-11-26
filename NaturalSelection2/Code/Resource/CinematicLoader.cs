using Sandbox.Mounting;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace NS2;

internal sealed class CinematicLoader( string path ) : ResourceLoader<GameMount>
{
	protected override object Load() => Host.ReadAllBytes( path );

	sealed record Clip( float Duration, CinematicEmitter[] Emitters, Action<Vector3, Rotation, GameObject>[] SceneNodes );
	static readonly ConditionalWeakTable<ResourceLoader, Clip> effects = new();

	static Clip Find( string relative )
	{
		if ( !Graphics.IsAvailable ) return null;
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		var loader = mount?.GetByPath( $"mount://ns2/ns2/{relative}" ) ?? mount?.GetByPath( $"mount://ns2/core/{relative}" );
		return loader is null ? null : effects.GetValue( loader, Read );
	}

	internal static float Play( string relative, Vector3 position, Rotation facing, GameObject parent = null )
	{
		if ( Find( relative ) is not { } clip ) return 0;
		foreach ( var emitter in clip.Emitters ) emitter.Play( position, facing, parent );
		foreach ( var play in clip.SceneNodes ) play( position, facing, parent );
		return clip.Duration;
	}

	internal static void AttachEmitters( string relative, GameObject parent )
	{
		if ( Find( relative ) is not { } clip ) return;
		foreach ( var emitter in clip.Emitters ) emitter.Play( parent.WorldPosition, parent.WorldRotation, parent, endless: true );
	}

	static Clip Read( ResourceLoader loader )
	{
		// CIN resources load synchronously. The bytes can come from a separately compiled mount.
		if ( loader.GetOrCreate().Result is not byte[] data ) return new( 0, [], [] );
		using var stream = new MemoryStream( data, writable: false );
		var file = CinematicFile.Read( stream );
		var emitters = new List<CinematicEmitter>();
		var nodes = new List<Action<Vector3, Rotation, GameObject>>();
		foreach ( var node in file.Nodes )
		{
			var visible = node.Get( "Visible" );
			if ( !visible.IsVisibleAt( 0 ) && !visible.HasVisibilityKeys ) continue;
			if ( node.Name is "Emitter" or "Physics Emitter" ) emitters.Add( new CinematicEmitter( node, file.Duration ) );
			else if ( node.Name == "Actor" ) nodes.Add( new CinematicActor( node, file.Duration ).Play );
			else if ( node.Name == "Point Light" ) nodes.Add( new CinematicLight( node, file.Duration ).Play );
		}
		return new( file.Duration, emitters.ToArray(), nodes.ToArray() );
	}
}
