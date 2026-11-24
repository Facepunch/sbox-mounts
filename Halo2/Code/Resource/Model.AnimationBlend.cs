using System;
using Sandbox.Animation;
using Node = Sandbox.Animation.GraphBuilder.Node;

namespace Halo2;

public partial class Halo2ModelLoader
{
	string MarkerBone( string marker )
	{
		foreach ( int group in Rows( mapFile.VaToOffset( (int)tag.DataVirtualAddress ) + 0x58, 12 ) )
		{
			if ( Label( group ) != marker || mapFile.ReadInt32( group + 4 ) <= 0 ) continue;
			int first = mapFile.VaToOffset( mapFile.ReadInt32( group + 8 ) );
			int node = mapFile.ReadSpan( first + 2, 1 )[0];
			if ( node < skeleton.Bones.Count ) return skeleton.Bones[node].Name;
		}
		return null;
	}

	sealed class MovementInputs
	{
		static readonly (string Name, Vector2 Point)[] Directions = [
			("front", new( 1, 0 )), ("back", new( -1, 0 )), ("left", new( 0, 1 )), ("right", new( 0, -1 )) ];
		readonly Node forward, left, run, enabled;
		readonly List<(int Idle, (int Reference, Vector2 Point)[] Directions)> sets = new();

		internal MovementInputs( GraphBuilder b, Halo2AnimationContext context )
		{
			if ( !context.Actions.TryGetValue( "idle", out int idle ) ) return;
			foreach ( string gait in new[] { "walk", "move" } )
			{
				var points = Directions.Where( d => context.Actions.ContainsKey( $"{gait}_{d.Name}" ) )
					.Select( d => (Reference: context.Actions[$"{gait}_{d.Name}"], d.Point) ).ToArray();
				if ( points.Any( d => d.Point.x != 0 ) && points.Any( d => d.Point.y != 0 ) ) sets.Add( (idle, points) );
			}
			if ( sets.Count == 0 ) return;
			enabled = b.BoolParameter( "locomotion" );
			forward = b.FloatParameter( "move_forward", -1, 1 );
			left = b.FloatParameter( "move_left", -1, 1 );
			if ( sets.Count > 1 ) run = b.FloatParameter( "run_weight", 0, 1 );
		}

		internal Node Apply( GraphBuilder b, Node original, Func<int, Node> pose )
		{
			if ( enabled is null ) return original;
			var gaits = sets.Select( set =>
			{
				var space = b.BlendSpace2D( forward, left ).Add( Vector2.Zero, pose( set.Idle ) );
				foreach ( var (reference, point) in set.Directions ) space.Add( point, pose( reference ) );
				return (Node)space;
			} ).ToArray();
			var machine = b.StateMachine();
			var action = machine.AddState( original );
			var moving = machine.AddState( gaits.Length == 1 ? gaits[0] : b.Blend( gaits[0], gaits[1], run ) );
			machine.SetEntryCondition( moving, enabled );
			machine.AddTransition( action, moving, enabled, new GraphBuilder.TransitionSettings() );
			machine.AddTransition( moving, action, b.Not( enabled ), new GraphBuilder.TransitionSettings() );
			return machine.Build();
		}
	}

	sealed class ScreenInputs
	{
		readonly record struct Screen( string Label, int Clip, float Right, float Left, float Down, float Up,
			int RightFrames, int LeftFrames, int DownFrames, int UpFrames );
		readonly List<Screen> screens = new();
		readonly Node aim, yaw, pitch, look, lookYaw, lookPitch;

		internal ScreenInputs( Halo2ModelLoader model, Halo2.Blam.MapFile map, GraphBuilder b, KeyValuePair<string,int>[] overlays )
		{
			foreach ( var (label, reference) in overlays )
			{
				int entry = model.Entry( reference );
				int index = (sbyte)map.ReadSpan( entry + 18, 1 )[0];
				int block = model.sources[(short)reference].Header + 36;
				if ( (uint)index >= map.ReadInt32( block ) ) continue;
				int p = map.VaToOffset( map.ReadInt32( block + 4 ) ) + index * 28;
				int Count( int offset ) => (short)map.ReadInt32( p + offset );
				float Degrees( int offset ) => model.Float( p + offset ).RadianToDegree();
				var s = new Screen( label, reference, Degrees( 4 ), Degrees( 8 ), Degrees( 16 ), Degrees( 20 ),
					Count( 12 ), Count( 14 ), Count( 24 ), Count( 26 ) );
				if ( (s.RightFrames + s.LeftFrames + 1) * (s.DownFrames + s.UpFrames + 1) != model.GetClipInfo( reference ).Frames ) continue;
				screens.Add( s );
			}
			if ( screens.Any( s => s.Label != "look" ) )
			{
				aim = b.IdParameter( "aim_screen", screens.Where( s => s.Label != "look" ).Select( s => s.Label ).ToArray() );
				yaw = b.FloatParameter( "aim_yaw", -360, 360 );
				pitch = b.FloatParameter( "aim_pitch", -180, 180 );
			}
			if ( screens.Any( s => s.Label == "look" ) )
			{
				look = b.BoolParameter( "look_enabled" );
				lookYaw = b.FloatParameter( "look_yaw", -360, 360 );
				lookPitch = b.FloatParameter( "look_pitch", -180, 180 );
			}
		}

		internal Node Apply( GraphBuilder b, Node output, Halo2ModelLoader model, Node action, KeyValuePair<string,int>[] actions )
		{
			if ( screens.Count == 0 ) return output;
			var layers = b.Layers( output );
			foreach ( var s in screens )
			{
				bool head = s.Label == "look";
				var enabled = head ? look : b.IdCompare( aim, s.Label );
				int flag = head ? 32 : 16;
				var excluded = actions.Where( a => (model.GetClipInfo( a.Value ).PlaybackFlags & flag) != 0 ).Select( a => a.Key ).ToArray();
				if ( excluded.Length > 0 ) enabled = b.And( enabled, b.Not( b.IdCompare( action, excluded ) ) );
				var clip = model.GetClip( s.Clip );
				var x = head ? lookYaw : yaw;
				var y = head ? lookPitch : pitch;
				int width = s.LeftFrames + s.RightFrames + 1;
				var rows = new List<(float Angle, Node Pose)>();
				for ( int row = -s.DownFrames; row <= s.UpFrames; row++ )
				{
					var cols = new List<(float Angle, Node Pose)>();
					for ( int col = -s.RightFrames; col <= s.LeftFrames; col++ )
					{
						float t = ((row + s.DownFrames) * width + col + s.RightFrames) / (float)Math.Max( 1, clip.FrameCount - 1 );
						cols.Add( (col * (col < 0 ? s.Right : s.Left), b.TimeControlledClip( clip, b.Constant( t ) )) );
					}
					rows.Add( (row * (row < 0 ? s.Down : s.Up), Axis( x, cols )) );
				}
				var weight = b.Switch( enabled, 1f, 0f );
				if ( s.Label == "pitch_and_turn" && excluded.Length > 0 )
					weight = b.Ease( weight );
				layers.AddLayer( Axis( y, rows ), new() { Weight = weight,
					BlendMode = clip.IsAdditive ? GraphBuilder.LayerBlendMode.Additive : GraphBuilder.LayerBlendMode.Overlay,
					BoneMask = model.clipMasks.TryGetValue( s.Clip, out var mask ) ? b.BoneMask( mask ) : null } );
			}
			return layers;

			Node Axis( Node input, List<(float Angle, Node Pose)> points )
			{
				if ( points.Count == 1 ) return points[0].Pose;
				var axis = b.BlendSpace( input, false );
				foreach ( var point in points ) axis.Add( point.Angle, point.Pose );
				return axis;
			}
		}
	}
}
