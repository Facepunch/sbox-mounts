using System;

namespace GoldSrc.HalfLife;

[Hide]
[Alias( "GoldSrc.HalfLifeNpcCorpse" )]
public class NpcCorpse : Component
{
	[Property] public string ModelPath { get; set; }
	[Property] public float ModelOriginHeight { get; set; }
	[Property] public string DeathSequence { get; set; }
	[Property] public SkinnedModelRenderer Renderer { get; set; }
	[Property] public ulong BodyGroups { get; set; }
	[Property] public string MaterialGroup { get; set; }
	[Property] public Color Tint { get; set; } = Color.White;
	[Sync( SyncFlags.FromHost )] protected TimeSince Age { get; set; }

	[Property] public bool FadeOnDeath { get; set; }
	TimeSince? _fadeAge;

	readonly StudioSequenceEvents _sequenceEvents = new();
	StudioSequence _sequence, _final;
	bool _finishing, _poseStarted;
	int _nextEvent;
	[Sync( SyncFlags.FromHost )] float FinalStart { get; set; } = -1;
	NpcAudio _audio;
	protected Vector3 Velocity { get; set; }
	BBox _hull;
	bool _anchored;
	TimeUntil _checkSupport;
	[Sync( SyncFlags.FromHost )] protected bool Landed { get; private set; }

	internal static GameObject Spawn( Npc npc, StudioSequence sequence, bool anchored = false, Vector3? velocity = null )
	{
		var source = npc.Renderer;
		var go = new GameObject( false, $"{npc.GameObject.Name} Corpse" );
		go.Tags.Add( "ragdoll" );
		go.Tags.Add( "removable" );
		go.WorldTransform = source.WorldTransform;

		var renderer = go.AddComponent<SkinnedModelRenderer>();
		renderer.Model = source.Model;
		renderer.UseAnimGraph = true;

		var corpse = npc.AddCorpseComponent( go );
		corpse.ModelPath = npc.ModelPath;
		corpse.ModelOriginHeight = npc.ModelOriginHeight;
		corpse.FadeOnDeath = npc.FadeCorpse;
		corpse.DeathSequence = sequence.Name;
		corpse.Renderer = renderer;
		corpse.BodyGroups = source.BodyGroups;
		corpse.MaterialGroup = source.MaterialGroup;
		corpse.Tint = source.Tint;
		corpse.Age = 0;
		if ( npc.Definition.Gibs != GibSet.None )
		{
			CorpseDamage.Attach( go, renderer, npc.MaxHealth * 0.5f, npc.Definition.Gibs, npc.Definition.Blood );
			var hitboxes = go.AddComponent<ModelHitboxes>();
			hitboxes.Renderer = renderer;
			hitboxes.Target = go;
		}
		var radius = npc.Definition.Radius;
		corpse._hull = new BBox( new Vector3( -radius, -radius, 0 ), new Vector3( radius, radius, npc.Definition.Height ) );
		corpse.Velocity = velocity ?? (npc.GetComponent<Rigidbody>() is { MotionEnabled: true } body ? body.Velocity : Sandbox.Npcs.BaseNpc.GetVelocity( npc.GameObject ));
		corpse._anchored = corpse.Landed = anchored;
		corpse.StartPose();
		go.Enabled = true;
		go.NetworkSpawn( true, null );
		return go;
	}

	internal virtual string PoseName => null;
	internal virtual float PoseTime => 0;
	internal virtual string FinalSequence => null;
	internal virtual bool WaitForFloor => false;
	internal virtual float GravityScale => 1;
	internal virtual void SetBody() { }
	internal virtual void HandleEvent( int id ) { }

	protected override void OnStart() => StartPose();

	void StartPose()
	{
		if ( _poseStarted || !Renderer.IsValid() ) return;
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount || mount.GetByPath( ModelPath ) is not ModelLoader loader ) return;

		// Hotloading the mount can give an existing actor a fresh loader for its corpse.
		loader.EnsureNpcAnimations();
		var desired = PoseName ?? DeathSequence;
		_sequence = loader.Sequences.FirstOrDefault( x => string.Equals( x.Name, desired, StringComparison.OrdinalIgnoreCase ) );
		if ( _sequence is null ) return;

		_audio = mount.NpcAudio;
		if ( PoseName is null ) Renderer.BodyGroups = BodyGroups;
		Renderer.MaterialGroup = MaterialGroup;
		Renderer.Tint = Tint;
		SetBody();

		if ( FinalSequence is { } final )
			_final = loader.Sequences.FirstOrDefault( x => string.Equals( x.Name, final, StringComparison.OrdinalIgnoreCase ) && x != _sequence );
		var time = Math.Clamp( (float)Age, 0, _sequence.Duration );
		Renderer.Animation.Graph = loader.AnimationGraphs.GetGraph( _sequence, _final );
		Renderer.Animation.Parameters.Set( StudioAnimationGraphs.StateParameter, _sequence.Name, persist: true );
		Renderer.Animation.Parameters.Set( _sequence.TimeParameter, PoseName is null ? time / _sequence.Duration : PoseTime, persist: true );
		_poseStarted = true;

		if ( PoseName is not null ) _sequence = null;
		else _sequenceEvents.Reset( _sequence, time, time <= 0.1f );
	}

	protected override void OnUpdate()
	{
		if ( _fadeAge is {} fade )
		{
			float alpha = Math.Max( 0, 1 - (int)((float)fade * 10) * (7f / 255) );
			Renderer.Attributes.Set( "StudioOpacity", alpha );
			if ( Networking.IsHost && (float)fade >= 3.9f ) GameObject.Destroy();
			return;
		}
		if ( _sequence is null || !Renderer.IsValid() || !Renderer.Animation.IsValid )
			return;

		if ( _final is not null && !_finishing )
		{
			if ( Networking.IsHost && FinalStart < 0 && Age >= _sequence.Duration && (!WaitForFloor || Landed) ) FinalStart = Age;
			if ( FinalStart >= 0 )
			{
				_finishing = true;
				_sequence = _final;
				_nextEvent = 0;
				Renderer.Animation.Parameters.Set( StudioAnimationGraphs.StateParameter, _sequence.Name );
				_sequenceEvents.Reset( _sequence, 0, true );
			}
		}
		var time = Math.Clamp( (float)Age - (_finishing ? FinalStart : 0), 0, _sequence.Duration );
		Renderer.Animation.Parameters.Set( _sequence.TimeParameter, time / _sequence.Duration );
		_sequenceEvents.Update( _audio, Renderer, time, false, grounded: Landed );
		if ( Networking.IsHost )
		{
			while ( _nextEvent < _sequence.Events.Length && _sequence.Events[_nextEvent].Frame <= time * _sequence.Description.Fps )
				HandleEvent( _sequence.Events[_nextEvent++].Index );
		}
		if ( time >= _sequence.Duration && (_final is null || _finishing) )
		{
			if ( FadeOnDeath )
			{
				Gib.EnableFade( Renderer );
				if ( GetComponent<ModelHitboxes>() is {} hitboxes ) hitboxes.Enabled = false;
				_fadeAge = 0;
			}
			else if ( Networking.IsHost && GetComponent<CorpseDamage>().IsValid() )
				Scene.GetSystem<Sandbox.Npcs.NpcSystem>().EmitStimulus( "carcass", WorldPosition, GameObject, 384, 30 );
			_sequence = null;
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy || _anchored )
			return;

		var offset = Vector3.Up * ModelOriginHeight;
		var feet = WorldPosition - offset;
		if ( Landed )
		{
			if ( _checkSupport > 0 ) return;
			_checkSupport = 0.25f;
			var support = TraceHull( feet, feet + Vector3.Down );
			if ( support.Hit && support.Normal.z > 0.7f ) return;
			Landed = false;
		}

		Velocity += Scene.PhysicsWorld.Gravity * (GravityScale * Time.Delta);
		var next = feet + Velocity * Time.Delta;
		var hit = TraceHull( feet, next );
		if ( !hit.Hit )
		{
			WorldPosition = next + offset;
			return;
		}

		Landed = hit.Normal.z > 0.7f;
		if ( Landed )
		{
			WorldPosition = hit.EndPosition + offset;
			Velocity = Vector3.Zero;
			return;
		}

		WorldPosition = hit.EndPosition + offset + hit.Normal * 0.03125f;
		Velocity -= hit.Normal * MathF.Min( 0, Velocity.Dot( hit.Normal ) );
	}

	SceneTraceResult TraceHull( Vector3 from, Vector3 to ) => Scene.Trace.Box( _hull, from, to )
		.IgnoreGameObjectHierarchy( GameObject )
		.WithoutTags( "npc", "player", "playercontroller", "ragdoll", "trigger", "nocollide_props", "debris" )
		.Run();
}
