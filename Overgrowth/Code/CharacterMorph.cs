namespace Overgrowth;

internal sealed record CharacterMorph( string Name, int Steps )
{
	internal string[] Targets { get; } = Enumerable.Range( 0, Steps ).Select( i => Steps == 1 ? Name : $"{Name}{i}" ).ToArray();

	internal static CharacterMorph[] Read( XmlAsset character ) => character.Element( "morphs" )?.Children
		.Where( x => x.Attribute( "default" ) != "true" )
		.Select( x => new CharacterMorph( x.Name, (int)(x.Number( "num_steps" ) ?? 1) ) ).ToArray() ?? [];

	internal static void AddTargets( BaseFileSystem fs, string characterPath, string modelPath, ObjLoader.ObjData model, Mesh mesh, float scale )
	{
		foreach ( var morph in Read( XmlAsset.Read( fs, characterPath, "character" ) ) )
		{
			for ( int step = 0; step < morph.Steps; step++ )
			{
				string name = morph.Targets[step];
				var path = $"{modelPath[..^4]}_{name}.obj";
				// LoadObjMorph pairs expanded face corners; OBJ vertex numbering can differ.
				var target = ObjLoader.LoadMorphPositions( fs, path, model.Center, model.FaceVertexCount );
				var deltas = new List<MorphDelta>();
				for ( int i = 0; i < model.Positions.Count; i++ )
				{
					var position = target[i] * scale - model.Positions[i];
					if ( position.LengthSquared > 0f )
						deltas.Add( new MorphDelta( i, position, Vector3.Zero ) );
				}
				if ( deltas.Count > 0 ) mesh.AddMorph( name, deltas.ToArray() );
			}
		}
	}
}
