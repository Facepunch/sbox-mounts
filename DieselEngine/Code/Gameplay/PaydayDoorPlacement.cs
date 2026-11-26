namespace Diesel;

/// <summary>
/// Resolves the closed doorway of apartment leaves placed ajar, using surrounding collision.
/// Shared by scene construction and delayed runtime initialization; no importer dependency.
/// </summary>
static class PaydayDoorPlacement
{
	internal static void Resolve( GameObject go, PaydayDoor door )
	{
		float yaw = go.WorldRotation.Yaw();
		float snapped = MathF.Round( yaw / 90 ) * 90;
		// The nearest cardinal direction can be the OPEN pose (e.g. a door opened 101°
		// is only 11° from the neighbouring wall). Prefer a clear leaf span ending at a jamb.
		// Wait until all architecture/brush collision exists before making these queries.
		var model = door.Leaf?.GetComponent<ModelRenderer>()?.Model;
		var hinge = go.WorldTransform.PointToWorld( door.Pivot );
		if ( model is not null )
		{
			var span = (model.Bounds.Center - door.Pivot).WithZ( 0 );
			var axis = MathF.Abs( span.x ) > MathF.Abs( span.y )
				? Vector3.Forward * MathF.Sign( span.x ) : Vector3.Left * MathF.Sign( span.y );
			float width = MathF.Abs( axis.x ) > 0
				? MathF.Max( MathF.Abs( model.Bounds.Mins.x - door.Pivot.x ), MathF.Abs( model.Bounds.Maxs.x - door.Pivot.x ) )
				: MathF.Max( MathF.Abs( model.Bounds.Mins.y - door.Pivot.y ), MathF.Abs( model.Bounds.Maxs.y - door.Pivot.y ) );
			var candidates = new List<float>();
			for ( int i = 0; i < 4; i++ )
			{
				float candidate = snapped + i * 90;
				float opening = Normalize( yaw + door.InitialOpenAngle - candidate );
				if ( MathF.Abs( opening ) > 175 ) continue;
				var along = Rotation.FromYaw( candidate ) * axis;
				var normal = Vector3.Cross( Vector3.Up, along );
				bool fits = true;
				foreach ( float height in new[] { 30f, 60f } )
				{
					var start = hinge.WithZ( go.WorldPosition.z + height );
					var jamb = go.Scene.Trace.Ray( start + along * 8, start + along * (width + 8) )
						.IgnoreGameObjectHierarchy( go ).WithoutTags( PaydayDoor.NavigationTag, "player", "trigger" ).Run();
					var middle = start + along * (width * .5f);
					var wall = go.Scene.Trace.Ray( middle - normal * 12, middle + normal * 12 )
						.IgnoreGameObjectHierarchy( go ).WithoutTags( PaydayDoor.NavigationTag, "player", "trigger" ).Run();
					if ( !jamb.Hit || jamb.StartedSolid || MathF.Abs( jamb.Distance + 8 - width ) > 6 || wall.Hit || wall.StartedSolid )
						fits = false;
				}
				if ( fits ) candidates.Add( candidate );
			}
			if ( candidates.Count == 1 ) snapped = candidates[0];
		}
		float ajar = Normalize( yaw - snapped );
		if ( MathF.Abs( ajar ) < 1 ) return;
		// Rebase about the leaf's local hinge axis, retaining authored pitch/roll.
		go.WorldRotation *= Rotation.FromYaw( -ajar );
		go.WorldPosition = hinge - go.WorldRotation * door.Pivot;
		float total = Normalize( door.InitialOpenAngle + ajar );
		if ( MathF.Abs( total ) > .01f && MathF.Sign( total ) != MathF.Sign( door.FixedOpenAngle ) ) door.FixedOpenAngle = -door.FixedOpenAngle;
		door.InitialOpenAngle = total;
		door.Leaf.LocalTransform = new Transform( Vector3.Zero ).RotateAround( door.Pivot, Rotation.FromYaw( total ) );

		static float Normalize( float angle ) => ((angle + 180) % 360 + 360) % 360 - 180;
	}
}
