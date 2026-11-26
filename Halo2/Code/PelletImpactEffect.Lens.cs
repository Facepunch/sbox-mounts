using Sandbox.Rendering;
using System.Threading.Tasks;

namespace Halo2;

public sealed partial class PelletImpactEffect
{
	static float CoronaRotation( Vector3 direction, Rotation view, float scale )
	{
		// The lens request stores each world direction component in an unsigned byte.
		static float Unpack( float component ) => System.MathF.Round( (component + 1) * 127.5f ) * (2f / 255) - 1;
		direction = new( Unpack( direction.x ), Unpack( direction.y ), Unpack( direction.z ) );
		var axis = Vector3.Cross( Vector3.Cross( direction, view.Right ), direction );
		float sine = Vector3.Dot( axis, view.Forward );
		return sine == 0 ? 0 : System.MathF.Atan2( sine, -Vector3.Dot( direction, view.Forward ) ) * (scale / System.MathF.PI);
	}

	static async Task PlayLens( GameObject marker, ImpactLens lens, float duration )
	{
		var scene = marker.Scene;
		var camera = scene.Camera;
		if ( !camera.IsValid() ) return;
		var material = Material.FromShader( "shaders/halo2_lens.shader" );
		var query = new ComputeShader( "shaders/halo2_lens_visibility" );
		var textures = lens.Reflections.Select( r => Texture.Load( r.TexturePath ) ).ToArray();
		using var history = new GpuBuffer<Vector4>( 1 );
		history.SetData( new[] { Vector4.Zero } );
		var commands = new CommandList( "Lens reflections" );
		camera.AddCommandList( commands, Stage.AfterViewmodel );
		var clock = TaskSource.Create( scene.EnabledToken );
		double start = Time.NowDouble;
		void Update()
		{
			commands.Reset();
			if ( !marker.IsValid() || !camera.IsValid() || camera != scene.Camera ) return;
			float elapsed = (float)(Time.NowDouble - start);
			if ( elapsed >= duration ) return;
			var position = marker.WorldPosition;
			float depth = Vector3.Dot( position - camera.WorldPosition, camera.WorldRotation.Forward );
			if ( depth <= camera.ZNear ) return;
			var center = camera.PointToScreenPixels( position, out _ );
			float pixelsPerUnit = (camera.PointToScreenPixels( position + camera.WorldRotation.Up, out _ ) - center).Length;
			commands.Attributes.Set( "LensProbe", new Vector4( center.x, center.y, lens.OcclusionRadius * pixelsPerUnit, depth ) );
			commands.ResourceBarrierTransition( history, ResourceState.UnorderedAccess );
			commands.Attributes.Set( "LensHistory", history );
			commands.DispatchCompute( query, 8, 8, 1 );
			commands.UavBarrier( history );
			commands.ResourceBarrierTransition( history, ResourceState.PixelShaderResource );
			commands.Attributes.Set( "LensVisibility", history );
			float control = 1 - elapsed / duration;
			for ( int i = 0; i < textures.Length; i++ )
			{
				var r = lens.Reflections[i];
				float radius = Sample( r.Radius, control ) * pixelsPerUnit;
				// Only the first reflection (the corona) inherits the lens rotation.
				float angle = -r.RotationOffset;
				if ( i == 0 ) angle -= CoronaRotation( marker.WorldRotation.Forward, camera.WorldRotation, lens.RotationScale );
				commands.Attributes.Set( "LensRotation", new Vector4( center.x, center.y, System.MathF.Cos( angle ), System.MathF.Sin( angle ) ) );
				commands.Attributes.Set( "LensBitmap", textures[i] );
				commands.Attributes.Set( "LensSecondaryAlpha", 1 - r.Modulation );
				commands.Attributes.Set( "LensTint", new Vector4( r.Tint.r, r.Tint.g, r.Tint.b, Sample( r.Brightness, control ) ) );
				commands.DrawQuad( new Rect( center.x - radius, center.y - radius, radius * 2, radius * 2 ), material, Color.White );
			}
		}

		try
		{
			// The animation system publishes attachment transforms at FinishUpdate priority zero.
			using var hook = scene.AddHook( GameObjectSystem.Stage.FinishUpdate, 1, Update, "Lens reflections", "Update marker" );
			while ( marker.IsValid() && camera.IsValid() && camera == scene.Camera && Time.NowDouble - start < duration )
				await clock.Frame();
		}
		finally
		{
			if ( camera.IsValid() ) camera.RemoveCommandList( commands );
		}
	}
}
