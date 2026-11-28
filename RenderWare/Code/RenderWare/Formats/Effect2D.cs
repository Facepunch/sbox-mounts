namespace Renderware;

/// <summary>
/// San Andreas' 2d effect plugin: the effects (lights etc) attached to a model, which III and VC kept in the IDE
/// 2dfx sections instead. Only lights are kept.
/// https://gtamods.com/wiki/2d_Effect_(RW_Section)
/// </summary>
public class Effect2DList : Chunk
{
	public List<LightEffect> Lights { get; } = new();

	public Effect2DList( BinaryReader br, ChunkHeader header ) : base( header )
	{
		int count = br.ReadInt32();
		for ( int i = 0; i < count && br.BaseStream.Length - br.BaseStream.Position >= 20; i++ )
		{
			var position = br.ReadVector3();
			int type = br.ReadInt32();
			int size = br.ReadInt32();
			long next = br.BaseStream.Position + size;

			if ( type == 0 && size >= 76 )
				Lights.Add( LightEffect.Read( br, position ) );

			br.BaseStream.Position = next;
		}
	}
}
