using Sandbox.Animation;

namespace Overgrowth;

internal sealed class WeaponStance( string path, string coordinate, (float At, WeaponStance Stance)[] inputs )
{
	internal string Path => path;
	internal bool IsClip => inputs.Length == 0;

	internal static WeaponStance Load( BaseFileSystem fs, string path, Dictionary<string, WeaponStance> loaded )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) throw new InvalidDataException( "Missing weapon stance path." );
		if ( loaded.TryGetValue( path, out var stance ) ) return stance;
		if ( path.EndsWith( ".anm" ) ) return loaded[path] = new( path, null, [] );
		var group = XmlAsset.Read( fs, path, "SyncedAnimationGroup" );
		var inputs = group.RequireElement( "Animations" ).Elements( "Animation" ).OrderBy( x => x.Number( "coord" ) )
			.Select( x => (x.Number( "coord" ).Value, Load( fs, x.Attribute( "path" ), loaded )) ).ToArray();
		return loaded[path] = new( path, group.RequireElement( "CoordLabel" ).Value, inputs );
	}

	internal string MaskName( bool mirrored ) => mirrored ? path + "_left" : path;

	internal GraphBuilder.Node Mask( GraphBuilder graph, IReadOnlyDictionary<string, GraphBuilder.Node> parameters, bool mirrored )
	{
		if ( IsClip ) return graph.BoneMask( MaskName( mirrored ) );
		var result = inputs[0].Stance.Mask( graph, parameters, mirrored );
		for ( int i = 1; i < inputs.Length; ++i )
		{
			var weight = graph.Clamp( graph.Remap( parameters[coordinate], new( inputs[i - 1].At, inputs[i].At ), new( 0f, 1f ) ), 0f, 1f );
			result = graph.BoneMaskBlend( result, inputs[i].Stance.Mask( graph, parameters, mirrored ), weight );
		}
		return result;
	}
}
