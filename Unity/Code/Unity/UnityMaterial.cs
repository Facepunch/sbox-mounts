namespace UnityMount;

/// <summary>
/// Parsed Unity 2021.3 Material (ClassID 21) including URP, Standard, Skybox, and ShaderGraph properties.
/// </summary>
public sealed class UnityMaterialData
{
	public readonly record struct TexEnv( PPtr Texture, Vector2 Scale, Vector2 Offset );

	public string Name { get; init; }
	public SerializedFile SourceFile { get; init; }
	public PPtr Shader { get; init; }
	public int CustomRenderQueue { get; set; }
	public HashSet<string> Keywords { get; } = new( StringComparer.OrdinalIgnoreCase );
	public Dictionary<string, string> StringTags { get; } = new( StringComparer.OrdinalIgnoreCase );
	public Dictionary<string, TexEnv> Textures { get; } = new( StringComparer.OrdinalIgnoreCase );
	public Dictionary<string, int> Ints { get; } = new( StringComparer.OrdinalIgnoreCase );
	public Dictionary<string, float> Floats { get; } = new( StringComparer.OrdinalIgnoreCase );
	public Dictionary<string, Color> Colors { get; } = new( StringComparer.OrdinalIgnoreCase );

	public static UnityMaterialData Read( UnityWorkspace workspace, SerializedFile file, UnityObjectInfo obj )
	{
		using var r = workspace.OpenObjectReader( file, obj );

		var mat = new UnityMaterialData
		{
			Name = r.ReadAlignedString(),
			SourceFile = file,
			Shader = r.ReadPPtr(),
		};

		// Unity 2021.2.18+ / 2021.3: m_ValidKeywords + m_InvalidKeywords
		int validKwCount = r.ReadInt32();
		for ( int i = 0; i < validKwCount; i++ )
			mat.Keywords.Add( r.ReadAlignedString() );

		int invalidKwCount = r.ReadInt32();
		for ( int i = 0; i < invalidKwCount; i++ )
			_ = r.ReadAlignedString();

		_ = r.ReadUInt32(); // m_LightmapFlags
		_ = r.ReadBool();   // m_EnableInstancingVariants
		_ = r.ReadBool();   // m_DoubleSidedGI
		r.Align( 4 );

		mat.CustomRenderQueue = r.ReadInt32();

		int tagCount = r.ReadInt32();
		for ( int i = 0; i < tagCount; i++ )
		{
			var k = r.ReadAlignedString();
			var v = r.ReadAlignedString();
			mat.StringTags[k] = v;
		}

		int disabledPasses = r.ReadInt32();
		for ( int i = 0; i < disabledPasses; i++ )
			_ = r.ReadAlignedString();

		// m_SavedProperties: m_TexEnvs, m_Ints (2021.1+), m_Floats, m_Colors
		int texCount = r.ReadInt32();
		for ( int i = 0; i < texCount; i++ )
		{
			var propName = r.ReadAlignedString();
			var texPtr = r.ReadPPtr();
			var scale = r.ReadVector2();
			var offset = r.ReadVector2();
			mat.Textures[propName] = new TexEnv( texPtr, scale, offset );
		}

		int intCount = r.ReadInt32();
		for ( int i = 0; i < intCount; i++ )
		{
			var propName = r.ReadAlignedString();
			mat.Ints[propName] = r.ReadInt32();
		}

		int floatCount = r.ReadInt32();
		for ( int i = 0; i < floatCount; i++ )
		{
			var propName = r.ReadAlignedString();
			mat.Floats[propName] = r.ReadSingle();
		}

		int colorCount = r.ReadInt32();
		for ( int i = 0; i < colorCount; i++ )
		{
			var propName = r.ReadAlignedString();
			mat.Colors[propName] = r.ReadColor();
		}

		// m_BuildTextureStacks (2020.1+)
		int stackCount = r.ReadInt32();
		for ( int i = 0; i < stackCount; i++ )
		{
			_ = r.ReadAlignedString();
			_ = r.ReadAlignedString();
		}

		return mat;
	}
}
