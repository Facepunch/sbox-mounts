namespace Diesel;

static class MapEnvironment
{
	public static void Add( GameObject root, EnvironmentSettings environment )
	{
		// Diesel links its sun to animated underlay nodes, not directly to sky_rot.
		// Keep the preview angle until those tracks can be evaluated.
		var sun = new GameObject( true, "Sunlight (preview direction)" ) { Parent = root, WorldRotation = Rotation.From( 55, 30, 0 ) };
		var color = environment?.Sun ?? new Vector3( 0.7f, 0.7f, 0.7f );
		sun.AddComponent<DirectionalLight>().LightColor = new Color( color.x, color.y, color.z );
		// Valid hemispherical sky lighting is evaluated per material normal. Adding
		// an ambient component as well would double-light every imported surface.
		if ( environment?.HasSky != true )
			new GameObject( true, "Preview ambient (missing environment)" ) { Parent = root }
				.AddComponent<AmbientLight>().Color = Color.White * 0.3f;
	}
}
