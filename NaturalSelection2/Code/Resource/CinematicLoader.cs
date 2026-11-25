using Sandbox.Mounting;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace NS2;

internal sealed class CinematicLoader( string path ) : ResourceLoader<GameMount>
{
	protected override object Load() => Host.ReadAllBytes( path );

	static readonly ConditionalWeakTable<ResourceLoader, Action<Vector3, Rotation, GameObject>[]> effects = new();

	internal static void Play( string relative, Vector3 position, Rotation facing, GameObject parent = null )
	{
		if ( !Graphics.IsAvailable ) return;
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		var loader = mount?.GetByPath( $"mount://ns2/ns2/{relative}" ) ?? mount?.GetByPath( $"mount://ns2/core/{relative}" );
		if ( loader is null ) return;

		foreach ( var play in effects.GetValue( loader, Read ) ) play( position, facing, parent );
	}

	static Action<Vector3, Rotation, GameObject>[] Read( ResourceLoader loader )
	{
		// CIN resources load synchronously. The bytes can come from a separately compiled mount.
		if ( loader.GetOrCreate().Result is not byte[] data ) return [];
		using var stream = new MemoryStream( data, writable: false );
		var file = CinematicFile.Read( stream );
		var nodes = new List<Action<Vector3, Rotation, GameObject>>();
		foreach ( var node in file.Nodes )
		{
			var visible = node.Get( "Visible" );
			if ( !visible.IsVisibleAt( 0 ) && !visible.HasVisibilityKeys ) continue;
			if ( node.Name is "Emitter" or "Physics Emitter" ) nodes.Add( new CinematicEmitter( node, file.Duration ).Play );
			else if ( node.Name == "Actor" ) nodes.Add( new CinematicActor( node, file.Duration ).Play );
			else if ( node.Name == "Point Light" ) nodes.Add( new CinematicLight( node, file.Duration ).Play );
		}
		return nodes.ToArray();
	}
}
