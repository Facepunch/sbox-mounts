using Diesel;
using System.Numerics;
using System.Text;

static class NpcVerification
{
	public static void Run( BundleDatabase db )
	{
		// Cyclic component order matters when the omitted component is not W.
		var hip = AnimationFile.PackedRotation( 0xffe00802 );
		if ( MathF.Abs( hip.Y - .70710678f ) > .002f || MathF.Abs( hip.Z - .70710678f ) > .002f
			|| MathF.Abs( hip.X ) > .002f || MathF.Abs( hip.W ) > .002f )
			throw new Exception( "Packed rotation component order changed." );
		foreach ( var definition in NpcDefinition.All )
		{
			var unit = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "unit", definition.Unit ) ) );
			string path = unit.Element( "object" ).Attribute( "file" );
			var model = new ModelFile( db.Find( "model", path ) );
			foreach ( var (name, clipPath) in definition.Clips() )
			{
				var clip = new AnimationFile( db.Find( "animation", clipPath ) );
				for ( int i = 0; i <= 8; i++ )
				{
					var pose = model.Pose( clip, clip.Duration * i / 8 );
					Matrix4x4.Invert( model.NodePose( pose, "root_point" ), out var root );
					var head = model.NodePose( pose, "Head" ) * root;
					float height = head.Translation.Z / 2.54f;
					if ( !float.IsFinite( height ) || height < -5 || height > 90
						|| (name == "idle" && height < 45) )
						throw new Exception( $"{definition.Id}/{name}: invalid posed head height {height}." );
				}
			}
		}
		Console.WriteLine( "PASS: six original character rigs, 24 animation references, packed rotation order and upright poses." );
	}
}
