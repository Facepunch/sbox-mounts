using System;
using Sandbox.Animation;

namespace Source1;

/// <summary>A ground rule connects an animated ankle to a contact patch on the supporting surface.</summary>
internal sealed class GroundFoot( string bone )
{
	internal static readonly string[] Channels = ["weight", "height", "radius", "floor", "latch"];
	internal static string Set( string bone ) => $"ground:{bone}";
	internal static string Channel( string bone, string channel ) => $"ground:{bone}:{channel}";
	internal static string Pad( string bone ) => $"ground_pad:{bone}";
	internal static string Ankle( string bone ) => $"ground_ankle:{bone}";

	readonly string _set = Set( bone );
	readonly string[] _channels = Channels.Select( c => Channel( bone, c ) ).ToArray();
	internal string UnlatchChannel { get; } = SourceAnimations.UnlatchChannel( bone );
	readonly string _target = $"ground_target:{bone}";
	readonly string _weight = $"ground_weight:{bone}";
	int _pad, _ankle, _hip, _knee, _foot;
	GameObject _support;
	Transform _localContact;
	TimeUntil _release;
	float _errorWeight;
	bool _latched;

	public (GraphBuilder.Node Target, GraphBuilder.Node Weight) Parameters( GraphBuilder builder, Model model )
	{
		_foot = model.Bones.GetBone( bone ).Index;
		var knee = model.Bones.GetBone( bone ).Parent;
		_knee = knee.Index;
		_hip = knee.Parent.Index;
		_pad = model.Bones.GetBone( Pad( bone ) ).Index;
		_ankle = model.Bones.GetBone( Ankle( bone ) ).Index;
		return (builder.TargetParameter( _target ), builder.FloatParameter( _weight, 0, 1 ));
	}

	public GraphBuilder.Node Build( GraphBuilder builder, GraphBuilder.Node input, (GraphBuilder.Node Target, GraphBuilder.Node Weight) parameters )
	{
		return builder.TwoBoneIK( input, bone, parameters.Target, new()
		{
			Weight = parameters.Weight,
			WorldSpace = true
		} );
	}

	public float? Update( SkinnedModelRenderer renderer, Sandbox.Npcs.BaseNpc npc, Transform origin, float unlatch )
	{
		var parameters = renderer.Animation.Parameters;
		float Read( int i ) => renderer.Animation.Debug.TryGetFloatChannel( _set, _channels[i], out var v ) ? v : 0;
		float? Release()
		{
			parameters.TrySet( _weight, 0f );
			_latched = false;
			_support = null;
			_errorWeight = 0;
			return null;
		}

		var weight = Read( 0 );
		if ( npc.IsAirborne || weight < 0.001f ) return Release();
		var height = Read( 1 ) * origin.Scale.z;
		if ( height <= 0 ) return Release();
		var radius = MathF.Max( 1, Read( 2 ) ) * origin.Scale.x;
		var pad = renderer.SceneModel.GetBoneWorldTransform( _pad );
		var ankle = pad.ToLocal( renderer.SceneModel.GetBoneWorldTransform( _ankle ) );
		var floor = origin.Position.z + Read( 3 ) * origin.Scale.z;
		var latch = MathF.Min( Read( 4 ), 1 - unlatch );
		var center = pad.Position.WithZ( floor );
		var heading = pad.Rotation;
		if ( _latched && _support.IsValid() && latch > 0 )
		{
			var anchor = _support.WorldTransform.ToWorld( _localContact );
			var hip = renderer.SceneModel.GetBoneWorldTransform( _hip ).Position;
			var knee = renderer.SceneModel.GetBoneWorldTransform( _knee ).Position;
			var end = renderer.SceneModel.GetBoneWorldTransform( _foot ).Position;
			var reach = anchor.ToWorld( ankle ).Position.Distance( hip );
			var upper = hip.Distance( knee );
			var lower = end.Distance( knee );
			// Release a plant that would stretch the leg or twist it while the body turns.
			if ( reach * .95f > upper + lower || reach < MathF.Abs( upper - lower )
				|| MathF.Abs( (anchor.Rotation.Inverse * heading).w ) * 2 - 1 < .707f )
			{
				_release = .25f;
			}
			_errorWeight = Math.Clamp( _errorWeight + Time.Delta * (_release > 0 ? 4 : -4), 0, 1 );
			latch *= 1 - _errorWeight * _errorWeight * (3 - 2 * _errorWeight);
			center = Vector3.Lerp( center, anchor.Position.WithZ( floor ), latch );
			heading = Rotation.Slerp( heading, anchor.Rotation, latch );
		}

		var bounds = new BBox( new( -radius, -radius, 0 ), new Vector3( radius, radius, radius * 2 ) );
		var span = Vector3.Up * height;
		var hit = npc.Scene.Trace.Box( bounds, center + span, center - span ).IgnoreGameObjectHierarchy( npc.GameObject )
			.WithoutTags( "npc", "player", "playercontroller", "trigger" ).Run();
		if ( !hit.Hit || hit.StartedSolid || hit.Normal.z <= 0 ) return Release();

		var contact = hit.EndPosition;
		var normal = hit.Normal;
		var slope = MathF.Sqrt( MathF.Max( 0, 1 - normal.z * normal.z ) ) / normal.z;
		contact.z -= radius * MathF.Min( 1, slope );
		if ( _release <= 0 && latch >= .999f && (!_latched || hit.GameObject != _support) && hit.GameObject.IsValid() )
		{
			_errorWeight = 0;
			_support = hit.GameObject;
			_localContact = _support.WorldTransform.ToLocal( new Transform( contact, heading ) );
			_latched = true;
		}
		else if ( latch <= .001f || _support != hit.GameObject ) _latched = false;

		var forward = Vector3.Cross( heading.Left, normal );
		var surface = new Transform( contact, Rotation.LookAt( forward, normal ), pad.Scale );
		parameters.TrySet( _target, surface.ToWorld( ankle ) );
		parameters.TrySet( _weight, weight );
		return contact.z - floor;
	}
}
