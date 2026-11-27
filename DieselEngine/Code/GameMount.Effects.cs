namespace Diesel;

public sealed partial class PaydayMount
{
	void RegisterDebris( MountContext context, string unit )
	{
		try
		{
			var path = UnitModel.Xml( Database.Find( "unit", unit ) ).Element( "object" )?.Attribute( "file" );
			if ( path is null || Database.Find( "object", path ) is not { } bytes ) return;
			var bodies = DebrisDefinition.Read( UnitModel.Xml( bytes ) );
			if ( bodies.Length == 0 ) return;
			foreach ( var body in bodies )
				context.Add( ResourceType.Model, $"{unit}.unit/debris/{body.Name}", new PaydayDebrisModelLoader( unit, body.Name ) );
			var prefab = new PaydayEffectPrefabLoader( unit, debris: true ); prefab.Tags.Add( "entity" );
			context.Add( ResourceType.PrefabFile, $"entities/debris/{unit}.prefab", prefab );
		}
		catch ( Exception e ) { Log.Warning( $"PAYDAY debris index {unit}: {e.Message}" ); }
	}
	readonly Dictionary<string, EffectDefinition> effects = [];
	readonly Dictionary<string, (DebrisDefinition.Body Body, UnitModel.Result Geometry)[]> debris = [];
	internal (DebrisDefinition.Body Body, UnitModel.Result Geometry)[] GetDebris( string unit )
	{
		if ( debris.TryGetValue( unit, out var cached ) ) return cached;
		if ( Database.Find( "unit", unit ) is not { } bytes ) return [];
		var path = UnitModel.Xml( bytes ).Element( "object" )?.Attribute( "file" );
		if ( path is null || Database.Find( "object", path ) is not { } obj ) return [];
		return debris[unit] = DebrisDefinition.Read( UnitModel.Xml( obj ) ).Select( b => (b,
			UnitModel.BuildDebris( Database, RuntimeMaterials, unit,
				$"mount://payday/{unit}.unit/debris/{b.Name}.vmdl", b.Objects )) ).ToArray();
	}
	readonly Dictionary<string, Dictionary<ulong, Transform>> effectAnchors = [];
	internal Transform? EffectAnchor( string unit, string name )
	{
		if ( name is null ) return Transform.Zero;
		if ( !effectAnchors.TryGetValue( unit, out var anchors ) )
		{
			var definition = UnitModel.Xml( Database.Find( "unit", unit ) );
			var path = definition.Element( "object" )?.Attribute( "file" );
			var obj = UnitModel.Xml( Database.Find( "object", path ) );
			var file = new ModelFile( Database.Find( "model", path ) );
			var pose = file.Pose( null, 0 );
			System.Numerics.Matrix4x4.Invert( file.NodePose( pose, obj.Element( "diesel" ).Attribute( "orientation_object" ) ), out var root );
			anchors = file.RigNodes.GroupBy( n => n.Name ).ToDictionary( n => n.Key, n => ModelFile.Convert( pose[n.First().Id] * root ) );
			effectAnchors[unit] = anchors;
		}
		return anchors.TryGetValue( IdString.Hash( name ), out var transform ) ? transform : null;
	}
	internal readonly Dictionary<string, Texture> EffectTextures = [];
	readonly Dictionary<string, (Color32[] Pixels, int Width, int Height)> effectPixels = [];
	readonly Dictionary<string, Sprite> effectSprites = [];
	internal Sprite EffectSprite( string path, Vector3 uv, Vector3 size )
	{
		if ( !effectPixels.TryGetValue( path, out var image ) )
		{
			var texture = EffectTextures.GetValueOrDefault( path );
			if ( texture is null ) return null;
			effectPixels[path] = image = (texture.GetPixels(), texture.Width, texture.Height);
		}
		int x = Math.Clamp( (int)MathF.Round( uv.x * image.Width ), 0, image.Width - 1 );
		int y = Math.Clamp( (int)MathF.Round( uv.y * image.Height ), 0, image.Height - 1 );
		int width = Math.Clamp( (int)MathF.Round( size.x * image.Width ), 1, image.Width - x );
		int height = Math.Clamp( (int)MathF.Round( size.y * image.Height ), 1, image.Height - y );
		string key = $"{path}:{x},{y},{width},{height}";
		if ( effectSprites.TryGetValue( key, out var sprite ) ) return sprite;
		var pixels = new Color32[width * height];
		for ( int row = 0; row < height; row++ ) Array.Copy( image.Pixels, (y + row) * image.Width + x, pixels, row * width, width );
		var cropped = Texture.Create( width, height ).WithData( pixels ).WithMips().Finish();
		return effectSprites[key] = Sprite.FromTexture( cropped );
	}
	internal EffectDefinition GetEffect( string path )
	{
		if ( effects.TryGetValue( path, out var value ) ) return value;
		var bytes = Database.Find( "effect", path ) ?? throw new InvalidDataException( $"Missing PAYDAY effect: {path}" );
		return effects[path] = new EffectDefinition( bytes );
	}
}
