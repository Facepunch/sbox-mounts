using System;
using Sandbox.Animation;

namespace Halo2;

/// <summary>An authored cue fired by AG2 while sampling a mounted Halo 2 clip.</summary>
public sealed class Halo2CueEvent : AnimationEvent
{
	public Halo2AnimationCue Cue { get; set; }
}

public sealed class Halo2Animator : Component, Component.IAnimationEvent
{
	[Property] public SkinnedModelRenderer Renderer { get; set; }
	[Property] public ModelRenderer Weapon { get; set; }
	[Property] public Dictionary<string, SoundEvent> Sounds { get; set; } = new();
	[Property] public Dictionary<string, GameObject> Effects { get; set; } = new();
	public Action<Halo2AnimationCue, Transform> CueFired { get; set; }

	Halo2ModelLoader model;
	Halo2AnimationContext context;
	string weaponClass, weaponType;
	string action;
	Halo2Transition pending, playing;
	bool holdReaction;
	Graph assigned;
	readonly List<(ModelAttachments.Attachment Hand, string[] Targets, string Parameter)> grips = new();

	public bool Configure( string mode, string weaponClass, string weaponType )
	{
		Renderer ??= Components.Get<SkinnedModelRenderer>();
		if ( !Renderer.IsValid() ) return false;
		var loader = Halo2ModelLoader.ForModel( Renderer.Model );
		var bindings = loader?.GetAnimationContext( mode, weaponClass, weaponType );
		if ( bindings is null ) return false;
		var graph = Graph.Load( $"{Renderer.Model.Name}/{mode}/{weaponClass}/{weaponType}.ag" );
		if ( !graph.IsValid() || graph.IsError ) return false;
		ReleaseSlots();
		this.weaponClass = weaponClass;
		this.weaponType = weaponType;
		model = loader;
		context = bindings;
		Renderer.UseAnimGraph = true;
		Renderer.Animation.Graph = assigned = graph;
		action = bindings.Actions.ContainsKey( "idle" ) ? "idle" : bindings.Actions.Keys.FirstOrDefault();
		grips.Clear();
		foreach ( var group in context.Ik.GroupBy( p => p.Marker ) )
		{
			var hand = Renderer.Model.Attachments.Get( group.Key );
			if ( hand?.Bone is not null )
				grips.Add( (hand, group.Select( p => p.AttachTo ).ToArray(), $"ik_{group.Key}") );
		}
		return true;
	}

	public bool SetAction( string name, bool once = false )
	{
		if ( !Bound || !context.Actions.ContainsKey( name ) ) return false;
		var p = Renderer.Animation.Parameters;
		p.TrySet( "locomotion", false );
		p.Set( "action_once", once );
		p.Set( "action", name );
		if ( once && name == action ) p.Trigger( "action_restart" );
		action = name;
		return true;
	}

	public bool SetMovement( float forward, float left, float runWeight )
	{
		if ( !Bound ) return false;
		var p = Renderer.Animation.Parameters;
		if ( !p.TrySet( "locomotion", true ) ) return false;
		p.Set( "move_forward", Math.Clamp( forward, -1f, 1f ) );
		p.Set( "move_left", Math.Clamp( left, -1f, 1f ) );
		p.TrySet( "run_weight", Math.Clamp( runWeight, 0f, 1f ) );
		return true;
	}

	/// <summary>Angles are degrees. Positive yaw turns left and positive pitch looks up.</summary>
	public bool SetAim( string screen, float yaw, float pitch )
	{
		if ( !Bound || (screen is { Length: > 0 } && !context.Overlays.ContainsKey( screen )) ) return false;
		var p = Renderer.Animation.Parameters;
		if ( !p.TrySet( "aim_screen", screen ?? "" ) ) return false;
		p.Set( "aim_yaw", yaw );
		p.Set( "aim_pitch", pitch );
		return true;
	}

	public bool SetLook( float yaw, float pitch, bool enabled = true )
	{
		if ( !Bound ) return false;
		var p = Renderer.Animation.Parameters;
		if ( !p.TrySet( "look_enabled", enabled ) ) return false;
		p.Set( "look_yaw", yaw );
		p.Set( "look_pitch", pitch );
		return true;
	}

	public bool PlayDamage( string kind, int direction, int region )
	{
		if ( !Bound || !context.TryGetDamage( kind, direction, region, out int clip ) ) return false;
		if ( !Play( clip, out string slot ) ) return false;
		if ( slot == "reaction" )
		{
			pending = playing = null;
			holdReaction = kind is "s_kill" or "h_kill";
		}
		return true;
	}

	public bool TransitionTo( string nextMode, string state )
	{
		if ( !Bound ) return false;
		var link = context.Transitions.FirstOrDefault( t => t.SourceState == action && t.Mode == nextMode && t.State == state );
		if ( link is null || model.GetAnimationContext( nextMode, weaponClass, weaponType ) is not { } target
			|| !target.Actions.ContainsKey( state ) ) return false;
		if ( link.Keyframe != 0 )
		{
			pending = link;
			return true;
		}
		return StartTransition( link );
	}

	bool StartTransition( Halo2Transition link )
	{
		if ( !Play( link.Clip, out _ ) ) return false;
		playing = link;
		pending = null;
		holdReaction = false;
		return true;
	}

	bool Play( int reference, out string slot )
	{
		slot = null;
		var clip = model.LoadAnimationClip( reference );
		var graph = model.OneShot( reference );
		if ( clip is null || graph is null ) return false;
		slot = clip.IsAdditive ? "reaction_additive" : "reaction";
		var slots = Renderer.Animation.ExternalGraphs;
		if ( !slots.Contains( slot ) ) return false;
		if ( slots.IsConnected( slot ) ) slots.Disconnect( slot );
		slots.Connect( slot, graph );
		return true;
	}

	bool Bound => Renderer.IsValid() && assigned is not null && Renderer.Animation.Graph == assigned && Renderer.Animation.IsValid;

	protected override void OnUpdate()
	{
		if ( !Bound ) return;
		var slots = Renderer.Animation.ExternalGraphs;
		foreach ( var slot in ReactionSlots )
		{
			if ( !slots.TryGetPlaybackInfo( slot, out var info ) || !info.IsActive || info.NormalizedTime < 1 ) continue;
			if ( slot == "reaction" && holdReaction ) continue;
			slots.Disconnect( slot );
			if ( slot == "reaction" && playing is { } transition )
			{
				if ( Configure( transition.Mode, weaponClass, weaponType ) ) SetAction( transition.State );
				break;
			}
		}
		foreach ( var (hand, targets, parameter) in grips )
		{
			Transform? grip = null;
			if ( Weapon.IsValid() && Weapon.Model.IsValid() )
			{
				foreach ( var marker in targets )
				{
					grip = Weapon is SkinnedModelRenderer skinned ? skinned.GetAttachment( marker )
						: Weapon.Model.Attachments.GetTransform( marker ) is { } local ? Weapon.WorldTransform.ToWorld( local ) : null;
					if ( grip.HasValue ) break;
				}
			}
			if ( grip is { } world )
			{
				var local = hand.LocalTransform;
				var rotation = world.Rotation * local.Rotation.Inverse;
				var scale = Renderer.WorldScale;
				var target = new Transform( world.Position - rotation * (local.Position * scale), rotation );
				Renderer.Animation.Parameters.TrySet( parameter, target );
			}
			else Renderer.Animation.Parameters.TryUnsetTarget( parameter );
		}
	}

	static readonly string[] ReactionSlots = ["reaction", "reaction_additive"];

	void ReleaseSlots()
	{
		if ( Bound )
		{
			var slots = Renderer.Animation.ExternalGraphs;
			foreach ( var slot in ReactionSlots )
				if ( slots.IsConnected( slot ) ) slots.Disconnect( slot );
			foreach ( var grip in grips ) Renderer.Animation.Parameters.TryUnsetTarget( grip.Parameter );
		}
		pending = playing = null;
		holdReaction = false;
	}

	protected override void OnDisabled() => ReleaseSlots();

	void Component.IAnimationEvent.OnAnimationEvent( AnimationEvent data )
	{
		if ( data is not Halo2CueEvent { Cue: { } cue } || data.Renderer != Renderer || !Bound ) return;
		var position = cue.Marker is { Length: > 0 } marker ? Renderer.GetAttachment( marker ) ?? Renderer.WorldTransform : Renderer.WorldTransform;
		if ( cue.Kind == "sound" && cue.TagPath is not null && Sounds.TryGetValue( cue.TagPath, out var sound ) && sound.IsValid() )
			Sound.Play( sound, position.Position );
		if ( cue.Kind == "effect" && cue.TagPath is not null && Effects.TryGetValue( cue.TagPath, out var effect ) && effect.IsValid() )
			effect.Clone( position );
		CueFired?.Invoke( cue, position );
		if ( pending is { } link && cue.Kind == $"transition_{(char)('a' + link.Keyframe - 1)}" ) StartTransition( link );
	}
}
