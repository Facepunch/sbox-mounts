using System;

namespace Source1;

partial class MapLoader
{
	void BuildWaterPhysics( List<BspPhysicsFluid> fluids, GameObject world, int modelIndex )
	{
		int index = 0;
		foreach ( var group in fluids.GroupBy( x => (x.Plane, x.SurfaceName, x.IsSlime) ) )
		{
			var settings = group.Key;
			var normal = new Vector3( settings.Plane.x, settings.Plane.y, settings.Plane.z );
			if ( (normal - Vector3.Up).LengthSquared > 0.000001f )
			{
				Log.Warning( $"{FullPath}: Sandbox water requires a horizontal surface; fluid plane {settings.Plane} was not imported." );
				continue;
			}
			var origin = normal * settings.Plane.w;
			var surface = Host.GetSurface( settings.SurfaceName, FullPath );
			var body = new PhysicsBodyBuilder { Surface = surface };
			foreach ( var fluid in group )
			{
				foreach ( var hull in fluid.Hulls )
				{
					var vertices = hull.Vertices.Select( vertex => vertex - origin ).ToArray();
					body.AddHull( vertices );
				}
			}

			var physics = PhysicsGroupDescription.Create( $"source1/{Host.Ident}/{FullPath}/water_{modelIndex}_{index++}.vphys", [body], [surface] );
			_worldPhysics.Add( physics );
			var child = new GameObject( false, "water physics" ) { Parent = world, IsStatic = true, LocalPosition = origin };
			child.Tags.Add( "water" );
			if ( settings.IsSlime ) child.Tags.Add( "slime" );
			var collider = child.AddComponent<PhysicsCollider>();
			collider.Physics = physics;
			collider.IsTrigger = true;
			collider.Static = true;
			collider.Surface = surface;
			var bounds = BBox.FromPoints( group.SelectMany( fluid => fluid.Hulls ).SelectMany( hull => hull.Vertices ).Select( vertex => vertex - origin ) );
			bounds.Maxs = bounds.Maxs.WithZ( 0 );
			var box = child.AddComponent<BoxCollider>();
			box.Enabled = false;
			box.Center = bounds.Center;
			box.Scale = bounds.Size;
			box.IsTrigger = true;
			box.Static = true;
			box.Surface = surface;
			child.Enabled = true;
		}
	}
}
