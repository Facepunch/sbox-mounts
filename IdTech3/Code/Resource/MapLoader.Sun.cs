using IdTech3.Formats;

namespace IdTech3;

partial class MapLoader
{
	/// <summary>
	/// The map's sun: q3map_sun on a shader (Quake 3, RTCW) or sundirection / suncolor / sunlight on worldspawn
	/// (Call of Duty).
	/// </summary>
	sealed class Sun
	{
		/// <summary>Unit direction from the world towards the sun.</summary>
		public Vector3 ToSun;
		/// <summary>Colour with its largest channel at 1.</summary>
		public Vector3 Color;
		public float Intensity;
	}

	Sun _sun;

	/// <summary>True when world surfaces darken under the sun's realtime shadows: the sun is baked into the probes.</summary>
	bool SunShadows => _sun is not null && _bsp.LightGrid?.Cells is not null;

	Sun FindSun()
	{
		if ( _bsp.Kind == BspKind.CallOfDuty )
		{
			var ws = _bsp.WorldSpawn;
			if ( !ws.Has( "sundirection" ) ) return null;

			// pitch and yaw of the direction towards the sun (negative pitch looks up)
			var angles = ws.Vector( "sundirection" );
			float pitch = angles.x * MathF.PI / 180, yaw = angles.y * MathF.PI / 180;
			var toSun = new Vector3( MathF.Cos( pitch ) * MathF.Cos( yaw ), MathF.Cos( pitch ) * MathF.Sin( yaw ), -MathF.Sin( pitch ) );
			return new Sun
			{
				ToSun = toSun.Normal,
				Color = NormalizeColor(  ws.Vector( "suncolor", new Vector3( 1, 1, 1 ) )  ),
				Intensity = Math.Clamp( ws.Float( "sunlight", 1 ), 0.25f, 4f ),
			};
		}

		// q3map_sun on the sky shader, or any shader that defines one
		ShaderDef sky = null;
		foreach ( var s in _bsp.Shaders )
		{
			var def = _game.Shaders.Find( s.Name );
			if ( def is not null && def.IsSky ) { sky = def; break; }
		}
		var shader = sky?.HasSun == true ? sky : _bsp.Shaders.Select( s => _game.Shaders.Find( s.Name ) ).FirstOrDefault( d => d?.HasSun == true );
		if ( shader is null ) return null;

		float elevation = shader.SunElevation * MathF.PI / 180, azimuth = shader.SunYaw * MathF.PI / 180;
		return new Sun
		{
			ToSun = new Vector3( MathF.Cos( elevation ) * MathF.Cos( azimuth ), MathF.Cos( elevation ) * MathF.Sin( azimuth ), MathF.Sin( elevation ) ).Normal,
			Color = NormalizeColor(  shader.SunColor  ),
			Intensity = Math.Clamp( shader.SunIntensity / 100f, 0.25f, 4f ),
		};
	}

	static Vector3 NormalizeColor( Vector3 c )
	{
		float max = MathF.Max( c.x, MathF.Max( c.y, c.z ) );
		return max > 0 ? c / max : Vector3.One;
	}

	/// <summary>
	/// The sun as a directional light. When the light probes carry it (<paramref name="baked"/>), it is baked light
	/// index 0 with mixed shadows: probe-lit objects take its light from the probes' per-probe strength and its
	/// realtime shadows from the world's shadow blockers and dynamic casters.
	/// </summary>
	void BuildSun( bool baked )
	{
		if ( _sun is null ) return;

		var go = new GameObject( false, "sun" );
		go.WorldRotation = Rotation.LookAt( -_sun.ToSun );
		var light = go.AddComponent<DirectionalLight>();
		light.Shadows = true;

		if ( baked )
		{
			// the probes' strengths carry the intensity
			light.LightColor = new Color( _sun.Color.x, _sun.Color.y, _sun.Color.z );
			light.BakeLightIndex = 0;
			light.MixedShadows = true;
			// the world's shadow blockers are static; without them the sun reaches indoors
			light.IncludeStaticShadowCasters = true;
		}
		else
		{
			light.LightColor = new Color( _sun.Color.x, _sun.Color.y, _sun.Color.z ) * _sun.Intensity;

			// a dynamic sun shadows the world too, which the lightmaps already have: no extra darkening
			foreach ( var material in _materials.Values )
				material?.Set( "g_flId3SunShadows", 0f );
		}

		go.Enabled = true;
	}
}
