namespace NS2;

internal sealed class SparkDecal : Component
{
	Decal decal;
	TimeSince age;
	float lifetime;

	protected override void OnUpdate()
	{
		if ( !decal.IsValid() || age >= lifetime ) GameObject.Destroy();
	}

	internal static bool HasBlood( string surface ) => surface is "flesh" or "robot" or "organic" or "membrane";

	internal static void ProjectBlood( Vector3 position, Vector3 direction, GameObject victim, string surface )
	{
		if ( !Graphics.IsAvailable ) return;
		var ray = Game.ActiveScene.Trace.Ray( position, position + direction * (3.5f * MapLoader.Scale) ).WithoutTags( "trigger" ).UseHitboxes();
		if ( victim.IsValid() ) ray = ray.IgnoreGameObjectHierarchy( victim );
		var wall = ray.Run();
		if ( !wall.Hit || wall.StartedSolid ) return;
		if ( !victim.IsValid() )
			surface = wall.Surface?.ResourceName is { } name && name.StartsWith( "ns2_" ) ? name[4..] : null;
		if ( !HasBlood( surface ) ) return;
		var (material, size) = Blood( surface );
		Spawn( "Blood impact", material, wall.EndPosition, Rotation.LookAt( -wall.Normal, direction ), 0, size, 12 );
	}

	internal static (string Material, float Size) Blood( string surface ) => surface switch
	{
		"flesh" => ($"marine_blood_0{Game.Random.Next( 1, 5 )}", 2f),
		"robot" => ($"robot_blood_0{Game.Random.Next( 1, 5 )}", 2f),
		"organic" => ($"alien_blood_0{Game.Random.Next( 1, 5 )}", 0.75f),
		"membrane" => ("alien_blood_02", 0.75f),
		_ => default
	};

	internal static void Spawn( string name, string material, Vector3 position, Rotation rotation, float roll, float size, float lifetime )
	{
		if ( !Graphics.IsAvailable ) return;
		var definition = GameResource.Load<DecalDefinition>( $"mount://ns2/ns2/cinematics/vfx_materials/decals/{material}.material.vmat/projected.decal" );
		if ( definition?.ColorTexture is null ) return;

		var effect = new GameObject( false, name )
		{
			NetworkMode = NetworkMode.Never,
			WorldPosition = position,
			WorldRotation = rotation
		};
		var decal = effect.AddComponent<Decal>();
		decal.Decals.Add( definition );
		decal.Size = size * MapLoader.Scale;
		decal.Rotation = roll;
		decal.LifeTime = lifetime;
		decal.Transient = true;
		var timed = effect.AddComponent<SparkDecal>();
		timed.decal = decal;
		timed.age = 0;
		timed.lifetime = lifetime;

		if ( size > 0.3f )
		{
			float radius = size * 0.5f * MapLoader.Scale;
			foreach ( var previous in effect.Scene.GetAllComponents<SparkDecal>() )
			{
				if ( !previous.decal.IsValid() || previous.WorldPosition.DistanceSquared( position ) >= radius * radius ) continue;
				previous.lifetime = previous.age + 1;
				previous.decal.LifeTime = previous.lifetime;
			}
		}
		effect.Enabled = true;
	}
}
