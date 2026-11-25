using System;

namespace Overgrowth;

// A character draws all of its camera-facing flame strips in one translucent pass.
internal sealed class CharacterFlames : SceneCustomObject
{
	readonly FireRibbon[] ribbons;
	readonly Vector3?[] lastAnchors;
	readonly List<Vertex> vertices;
	readonly Material material = Material.FromShader( "shaders/overgrowth_fire.shader" );
	float gameTime, flameEffect;
	internal ScenePointLight Light { get; }
	internal int BoneCount => ribbons.Length;

	internal CharacterFlames( SceneWorld world, int bones, GameMount mount ) : base( world )
	{
		var light = XmlAsset.Read( mount.FileSystem, "Data/Objects/default_light.xml", "ActorObjects" ).Element( "DynamicLightObject" );
		Light = new ScenePointLight( world, Vector3.Zero, (light.Number( "s0" ) ?? 1f) * 40f, Color.Black );
		ribbons = Enumerable.Range( 0, bones ).Select( _ => new FireRibbon() ).ToArray();
		lastAnchors = new Vector3?[bones];
		vertices = new( bones * 4 * 6 );
		Flags.IsOpaque = false;
		Flags.IsTranslucent = true;
	}

	static Vector3 ToSource( Vector3 v ) => new Vector3( v.y, v.z, v.x ) / 40f;
	static Vector3 ToWorld( Vector3 v ) => new Vector3( v.z, v.x, v.y ) * 40f;

	internal bool Advance( CharacterLoader loader, SkinnedModelRenderer renderer, RabbitGuard fighter, bool burning )
	{
		float dt = Time.Delta;
		gameTime = Time.Now;
		var physics = renderer.GetComponent<ModelPhysics>();
		var root = physics is { Active: true } ? CharacterLoader.RagdollCenter( physics ) ?? renderer.WorldPosition
			: fighter?.CharacterCenter( fighter.GameObject ) ?? renderer.WorldPosition;
		var bounds = new BBox( root, root );
		var center = ToSource( root );
		bool active = false;
		for ( int i = 0; i < ribbons.Length; i++ )
		{
			if ( !loader.FireAnchor( i, renderer, physics, fighter, out var world, out var bodyVelocity ) ) continue;
			var anchor = ToSource( world );
			var velocity = bodyVelocity.HasValue ? ToSource( bodyVelocity.Value )
				: dt > 0f && lastAnchors[i] is { } previous ? (anchor - previous) / dt : Vector3.Zero;
			lastAnchors[i] = anchor;
			var ribbon = ribbons[i];
			ribbon.Advance( anchor, velocity, center, burning, gameTime, dt );
			active |= ribbon.Count > 0;
			for ( int j = 0; j < ribbon.Count; j++ ) bounds = bounds.AddPoint( ToWorld( ribbon.Points[j].Position ) );
		}
		Bounds = bounds.Grow( 10f );
		flameEffect = Math.Clamp( flameEffect + (burning ? dt : -dt) * 10f, 0f, 1f );
		var first = ribbons[0];
		if ( first.Count < 2 ) Light.LightColor = Color.Black;
		else
		{
			int top = Math.Min( 3, first.Count - 1 );
			var a = first.Points[top];
			var b = first.Points[top - 1];
			float fraction = first.SpawnFraction;
			Light.Position = ToWorld( Vector3.Lerp( a.Position, b.Position, fraction ) );
			float heat = Math.Max( 0f, flameEffect * 2f + MathX.Lerp( a.Heat, b.Heat, fraction ) );
			float strength = 0.2f * heat * (top == 1 ? 1f - fraction : 1f);
			Light.LightColor = new Color( 2f * strength, strength, 0f );
		}
		return active || flameEffect > 0f;
	}

	public override void RenderSceneObject()
	{
		vertices.Clear();
		var forward = Graphics.CameraRotation.Forward;
		foreach ( var r in ribbons )
		{
			if ( r.Count < 2 ) continue;
			Vector3 previousRight = default;
			Vertex lastLeft = default, lastRight = default;
			for ( int i = 0; i < r.Count; i++ )
			{
				var p = r.Points[i];
				var nextRight = i + 1 < r.Count ? forward.Cross( ToWorld( r.Points[i + 1].Position - p.Position ) ).Normal : previousRight;
				var offset = (previousRight + nextRight).Normal * (40f * p.Width);
				var center = ToWorld( p.Position );
				var timing = new Vector4( p.Born + r.Seed, gameTime + r.Seed, 0f, 0f );
				var left = new Vertex( center + offset ) { TexCoord0 = new( p.Heat, 0f, 0f, 0f ), TexCoord1 = timing };
				var right = new Vertex( center - offset ) { TexCoord0 = new( p.Heat, 1f, 0f, 0f ), TexCoord1 = timing };
				if ( i > 0 )
				{
					vertices.Add( lastLeft ); vertices.Add( lastRight ); vertices.Add( left );
					vertices.Add( left ); vertices.Add( lastRight ); vertices.Add( right );
				}
				previousRight = nextRight;
				lastLeft = left;
				lastRight = right;
			}
		}
		if ( vertices.Count > 0 ) Graphics.Draw( vertices, vertices.Count, material );
	}
}
