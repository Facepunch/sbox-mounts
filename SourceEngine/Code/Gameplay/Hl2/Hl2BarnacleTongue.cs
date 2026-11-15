using System;

namespace Source1;

/// <summary>Extends the mounted barnacle's own tongue rig to the floor or its captured victim.</summary>
[Title( "HL2 Barnacle Tongue" )]
[Category( "Half-Life 2" )]
public sealed class Hl2BarnacleTongue : Component
{
	public Hl2BarnacleNpc Npc { get; set; }
	Model _model;
	int[] _bones;
	float[] _fractions;
	Vector3[] _axes;
	float _length;
	IDisposable _hook;

	protected override void OnEnabled() => _hook = Scene.AddHook( GameObjectSystem.Stage.FinishUpdate, 100, UpdateTongue,
		nameof( Hl2BarnacleTongue ), "Place mounted barnacle tongue" );

	protected override void OnDisabled()
	{
		_hook?.Dispose();
		_hook = null;
	}

	void UpdateTongue()
	{
		var renderer = Npc.IsValid() ? Npc.Renderer : null;
		if ( !renderer.IsValid() || !renderer.Model.IsValid() || !renderer.SceneModel.IsValid() )
			return;

		if ( _model != renderer.Model )
		{
			_model = renderer.Model;
			var chain = _model.Bones.AllBones.Where( b => b.Name.Contains( ".tongue", StringComparison.OrdinalIgnoreCase ) ).ToArray();
			_bones = chain.Select( b => b.Index ).ToArray();
			_fractions = new float[chain.Length];
			_axes = new Vector3[chain.Length];
			if ( chain.Length < 2 )
				return;

			for ( var i = 1; i < chain.Length; i++ )
				_fractions[i] = _fractions[i - 1] + chain[i].LocalTransform.Position.Distance( chain[i - 1].LocalTransform.Position );
			_length = _fractions[^1];
			if ( _length <= 0 )
				return;

			for ( var i = 0; i < chain.Length; i++ )
			{
				_fractions[i] /= _length;
				var next = chain[Math.Min( i + 1, chain.Length - 1 )].LocalTransform.Position;
				var previous = chain[Math.Max( i - 1, 0 )].LocalTransform.Position;
				var axis = chain[i].LocalTransform.Rotation.Inverse * (next - previous).Normal;
				_axes[i] = new Vector3( Math.Abs( axis.x ), Math.Abs( axis.y ), Math.Abs( axis.z ) );
			}
		}

		if ( _bones.Length < 2 || _length <= 0 )
			return;

		var first = renderer.SceneModel.GetBoneWorldTransform( _bones[0] );
		var last = renderer.SceneModel.GetBoneWorldTransform( _bones[^1] );
		var start = first.Position;
		var end = Npc.CapturedTarget.IsValid() ? SourceNpcs.CapturePoint( Npc.CapturedTarget )
			: Scene.Trace.Ray( start, start + Vector3.Down * Npc.AttackRange ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run().EndPosition;
		var direction = (end - start).Normal;
		var rotation = Rotation.FromToRotation( (last.Position - start).Normal, direction );
		var scale = start.Distance( end ) / _length;
		var bounds = _model.RenderBounds.Transform( renderer.WorldTransform );
		for ( var i = 0; i < _bones.Length; i++ )
		{
			var pose = renderer.SceneModel.GetBoneWorldTransform( _bones[i] );
			pose.Position = Vector3.Lerp( start, end, _fractions[i] );
			pose.Rotation = rotation * pose.Rotation;
			pose.Scale = Vector3.One + _axes[i] * (scale - 1);
			renderer.SceneModel.SetBoneWorldTransform( _bones[i], pose );
			bounds = bounds.AddPoint( pose.Position );
		}
		renderer.SceneModel.Bounds = bounds.Grow( 2 );
	}
}
