namespace Diesel;

static class MapEnvironment
{
	public static void Add( GameObject root, EnvironmentSettings environment )
	{
		// The authored direction comes from the underlay's d_sun node (see SunDirection). A light
		// shines along its forward axis, i.e. from the sun towards the world. Keep the old preview
		// angle only when the environment or underlay couldn't be evaluated, or the sun is below the horizon.
		var towardsSun = environment?.SunDirection;
		bool authored = towardsSun is { } d && d.z > 0.01f;
		var sun = new GameObject( true, authored ? "Sunlight" : "Sunlight (preview direction)" )
		{
			Parent = root,
			WorldRotation = authored ? Rotation.LookAt( -towardsSun.Value ) : Rotation.From( 55, 30, 0 ),
		};
		var color = environment?.Sun ?? new Vector3( 0.7f, 0.7f, 0.7f );
		sun.AddComponent<DirectionalLight>().LightColor = new Color( color.x, color.y, color.z );
		// Valid hemispherical sky lighting is evaluated per material normal. Adding
		// an ambient component as well would double-light every imported surface.
		if ( environment?.HasSky != true )
			new GameObject( true, "Preview ambient (missing environment)" ) { Parent = root }
				.AddComponent<AmbientLight>().Color = Color.White * 0.3f;
	}
}
