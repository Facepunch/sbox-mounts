using System;

namespace Source1;

public abstract partial class BaseMount
{
	readonly Dictionary<string, SourceAnimations> animations = new( StringComparer.OrdinalIgnoreCase );

	internal void AddAnimations( string model, SourceAnimations value )
	{
		lock ( animations ) animations[model] = value;
	}

	/// <summary>The animations of a mounted animated model, by its path within this mount.</summary>
	internal SourceAnimations Animations( string model )
	{
		var path = $"mount://{Ident}/{model}";
		lock ( animations )
		{
			if ( animations.TryGetValue( path, out var found ) )
				return found;
		}

		// Building the model reads its animations.
		Model.Load( path );
		lock ( animations ) return animations.GetValueOrDefault( path );
	}
}
