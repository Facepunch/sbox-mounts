using System;
using Sandbox.Animation;

namespace Halo2;

/// <summary>An authored cue fired by AG2 while sampling a mounted Halo 2 clip.</summary>
public sealed class Halo2CueEvent : AnimationEvent
{
	public Halo2AnimationCue Cue { get; set; }

	public Halo2CueEvent() { }
	public Halo2CueEvent( int frame, string kind, int index, string tagPath, string marker, int flags )
	{
		Cue = new( frame, kind, index, tagPath, marker, flags );
	}
}

public sealed class Halo2Animator : Component, Component.IAnimationEvent
{
	[Property] public SkinnedModelRenderer Renderer { get; set; }
	[Property] public ModelRenderer Weapon { get; set; }
	[Property] public Dictionary<string, SoundEvent> Sounds { get; set; } = new();
	[Property] public Dictionary<string, GameObject> Effects { get; set; } = new();
	internal Func<string, Transform?> SampleWeaponMarker { get; set; }
	internal bool SampleCuesFromGraph { get; set; } = true;
	public Action<Halo2AnimationCue, Transform> CueFired { get; set; }

	AnimationBindings bindings;
	Halo2AnimationContext context;
	[Sync( SyncFlags.FromHost )] string HolderContext { get; set; }
	string configuredContext, weaponClass, weaponType;
	Model configuredModel;
	string action;
	string weaponOverlay;
	TimeUntil? overlayEnds;
	AnimationBlend gripBlend;
	Halo2Transition pending, playing;
	bool holdReaction;
	Graph assigned;
	sealed record Grip( ModelAttachments.Attachment Hand, string[] Targets, string Parameter, Halo2ModelLoader.WeaponGrip Binding )
	{
		public string Anchor { get; } = $"{Parameter}_anchor";
		public string Offset { get; } = $"{Parameter}_offset";
	}
	readonly List<Grip> grips = new();

	internal void ConfigureWeapon( string category, string type )
	{
		if ( Bound && configuredModel == Renderer.Model && weaponClass == category && weaponType == type ) return;
		Configure( configuredContext?.Split( '/' )[0] ?? "combat", category, type );
	}

	public bool Configure( string mode, string weaponClass, string weaponType )
	{
		Renderer ??= Components.Get<SkinnedModelRenderer>();
		if ( !Renderer.IsValid() ) return false;
		if ( !Renderer.Model.IsValid() || Renderer.Model.IsError ) return false;
		var requested = $"{mode}/{weaponClass}/{weaponType}";
		var path = $"{Renderer.Model.Name}/{requested}";
		var data = AnimationBindings.Load( $"{path}.json" );
		if ( data is null ) return false;
		var graph = Graph.Load( $"{path}.ag" );
		if ( !graph.IsValid() || graph.IsError ) return false;
		ReleaseSlots();
		configuredContext = requested;
		configuredModel = Renderer.Model;
		if ( Networking.IsHost ) HolderContext = configuredContext;
		this.weaponClass = weaponClass;
		this.weaponType = weaponType;
		bindings = data;
		context = data.Context();
		Renderer.UseAnimGraph = true;
		Renderer.Animation.Graph = assigned = graph;
		action = bindings.Actions.ContainsKey( "idle" ) ? "idle" : bindings.Actions.Keys.FirstOrDefault();
		grips.Clear();
		var gripWeapon = data.GripModel is {} gripPath ? Model.Load( gripPath ) : null;
		foreach ( var group in context.Ik.GroupBy( p => p.Marker ) )
		{
			var hand = Renderer.Model.Attachments.Get( group.Key );
			if ( hand?.Bone is not null )
			{
				var targets = group.Select( p => p.AttachTo ).ToArray();
				grips.Add( new( hand, targets, $"ik_{group.Key}", Halo2ModelLoader.BindGrip( Renderer.Model, gripWeapon, group.Key, targets ) ) );
			}
		}
		return true;
	}

	internal Halo2ClipInfo CurrentActionClip => context?.Actions.TryGetValue( action, out var reference ) == true
		? bindings.Clips.GetValueOrDefault( reference ) : null;

	internal Halo2ClipInfo GetOverlayClip( string name ) => context?.Overlays.TryGetValue( name, out var reference ) == true
		? bindings.Clips.GetValueOrDefault( reference ) : null;

	internal void PlayWeaponOverlay( string name )
	{
		if ( !Bound ) return;
		// The Carbine stance binds its primary-fire clip in the second firing slot.
		if ( name == "fire_1" && GetOverlayClip( name ) is null &&
			GetOverlayClip( "fire_2" )?.Name.EndsWith( ":fire_1", StringComparison.Ordinal ) == true )
			name = "fire_2";
		if ( GetOverlayClip( name ) is not {} clip ) return;
		var parameters = Renderer.Animation.Parameters;
		parameters.Set( "overlay", name );
		parameters.Set( "overlay_auto", true );
		parameters.Trigger( "overlay_restart" );
		weaponOverlay = name;
		overlayEnds = clip.Frames / 30f;
	}

	internal void StopWeaponOverlay( string name )
	{
		if ( weaponOverlay == name ) ReleaseWeaponOverlay();
	}

	internal void SampleWeaponOverlay( string name, float cycle )
	{
		if ( !Bound ) return;
		var parameters = Renderer.Animation.Parameters;
		parameters.Set( "overlay", name );
		parameters.Set( "overlay_auto", false );
		parameters.Set( "overlay_time", cycle );
		weaponOverlay = name;
		overlayEnds = null;
	}

	void ReleaseWeaponOverlay()
	{
		if ( weaponOverlay is null ) return;
		if ( Bound && string.Equals( Renderer.Animation.Parameters.GetString( "overlay" ), weaponOverlay, StringComparison.OrdinalIgnoreCase ) )
			Renderer.Animation.Parameters.Set( "overlay", "" );
		weaponOverlay = null;
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
		if ( link is null || AnimationBindings.Load( $"{Renderer.Model.Name}/{nextMode}/{weaponClass}/{weaponType}.json" ) is not { } target
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
		var clip = Clip.Load( Halo2ModelLoader.ClipPath( Renderer.Model.Name, reference ) );
		var graph = Graph.Load( $"{Renderer.Model.Name}/oneshots/{reference}.ag" );
		if ( clip is null || graph is null ) return false;
		slot = clip.IsAdditive ? "reaction_additive" : "reaction";
		var slots = Renderer.Animation.ExternalGraphs;
		if ( !slots.Contains( slot ) ) return false;
		if ( slots.IsConnected( slot ) ) slots.Disconnect( slot );
		slots.Connect( slot, graph );
		return true;
	}

	internal bool Bound => Renderer.IsValid() && assigned is not null && Renderer.Animation.Graph == assigned && Renderer.Animation.IsValid;

	// Retail admits these secondary actions before checking the two animation records.
	static bool AllowsWeaponGrip( string overlay ) => overlay is null or "" or "fire_1" or "fire_2"
		or "chamber_1" or "chamber_2" or "charged_1" or "charged_2" or "melee" or "leap_melee"
		or "melee_back" or "melee_dash" or "melee_lunge" or "melee_1sthit" or "melee_2ndhit"
		or "melee_3rdhit" or "melee_flurry" or "melee_tackle" or "melee_airborne" or "melee_strike_1"
		or "melee_strike_2" or "melee_strike_3" or "melee_strike_4" or "melee_off_hand"
		or "melee_1sthit2idle" or "melee_2ndhit2idle" or "melee_dash_airborne" or "melee_lunge_airborne";

	protected override void OnFixedUpdate()
	{
		if ( !Bound || grips.Count == 0 ) return;
		var parameters = Renderer.Animation.Parameters;
		string overlay = parameters.GetString( "overlay" );
		bool active = AllowsWeaponGrip( overlay )
			&& (string.IsNullOrEmpty( overlay ) || ((GetOverlayClip( overlay )?.PlaybackFlags ?? 0) & 8) == 0);
		float duration = active ? 0.267f : 0.06675f;
		gripBlend.Advance();
		gripBlend.Select( active, (int)MathF.Round( Sandbox.ProjectSettings.Physics.FixedUpdateFrequency * duration ) );
		parameters.TrySet( "weapon_grip_weight", Weapon.IsValid() ? gripBlend.Weight : -1f );
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost && HolderContext is {} requested && Renderer.IsValid() && Renderer.Model is { IsError: false } model
			&& (configuredContext != requested || configuredModel != model) )
		{
			if ( requested.Split( '/' ) is [var mode, var category, var weapon] ) Configure( mode, category, weapon );
		}
		if ( !Bound ) return;
		if ( weaponOverlay is not null && overlayEnds is {} end && end <= 0 ) ReleaseWeaponOverlay();
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
		foreach ( var binding in grips )
		{
			Transform? grip = null;
			if ( Weapon.IsValid() && Weapon.Model.IsValid() )
			{
				foreach ( var marker in binding.Targets )
				{
					grip = SampleWeaponMarker?.Invoke( marker ) ?? (Weapon is SkinnedModelRenderer skinned ? skinned.GetAttachment( marker, false )
						: Weapon.Model.Attachments.GetTransform( marker ));
					if ( grip.HasValue ) break;
				}
			}
			if ( grip is { } localMarker )
			{
				Transform anchor = default;
				var gripBinding = binding.Binding;
				bool anchored = gripBinding is not null && Weapon.GameObject.Parent is {} parent
					&& parent == Renderer.GetAttachmentObject( gripBinding.Anchor )
					&& Renderer.TryGetBoneTransform( gripBinding.Anchor.Bone, out anchor );
				// Evaluate the weapon and the arm from the same holder pose while the player moves.
				var world = anchored ? anchor.ToWorld( gripBinding.Anchor.LocalTransform.ToWorld( Weapon.LocalTransform.ToWorld( localMarker ) ) )
					: Weapon.WorldTransform.ToWorld( localMarker );
				var hand = binding.Hand.LocalTransform;
				var rotation = world.Rotation * hand.Rotation.Inverse;
				var position = world.Position - rotation * (hand.Position * Renderer.WorldScale);
				var target = new Transform( LimitGripReach( binding.Hand.Bone, position ), rotation );
				var parameters = Renderer.Animation.Parameters;
				if ( anchored )
				{
					var relative = anchor.ToLocal( target );
					if ( relative.Rotation.AlmostEqual( gripBinding.Rotation ) && parameters.TrySet( binding.Offset, relative.Position ) )
					{
						parameters.TrySetTargetBone( binding.Anchor, gripBinding.Anchor.Bone.Name );
						parameters.TryUnsetTarget( binding.Parameter );
						continue;
					}
				}
				parameters.TrySet( binding.Parameter, target );
			}
			else Renderer.Animation.Parameters.TryUnsetTarget( binding.Parameter );
			if ( binding.Binding is not null ) Renderer.Animation.Parameters.TryUnsetTarget( binding.Anchor );
		}
	}

	Vector3 LimitGripReach( BoneCollection.Bone hand, Vector3 target )
	{
		if ( hand.Parent?.Parent is not {} shoulder || !Renderer.TryGetBoneTransform( shoulder, out var root )
			|| !Renderer.TryGetBoneTransform( hand.Parent, out var elbow ) || !Renderer.TryGetBoneTransform( hand, out var wrist ) )
			return target;

		// Halo's two-bone solver caps wrist travel at 98% of the posed chain length.
		float reach = ((elbow.Position - root.Position).Length + (wrist.Position - elbow.Position).Length) * 0.98f;
		var delta = target - root.Position;
		return reach > 0 && delta.LengthSquared > reach * reach ? root.Position + delta.Normal * reach : target;
	}

	static readonly string[] ReactionSlots = ["reaction", "reaction_additive"];

	void ReleaseSlots()
	{
		ReleaseWeaponOverlay();
		if ( Bound )
		{
			var slots = Renderer.Animation.ExternalGraphs;
			foreach ( var slot in ReactionSlots )
				if ( slots.IsConnected( slot ) ) slots.Disconnect( slot );
			foreach ( var grip in grips )
			{
				Renderer.Animation.Parameters.TryUnsetTarget( grip.Parameter );
				if ( grip.Binding is not null ) Renderer.Animation.Parameters.TryUnsetTarget( grip.Anchor );
			}
			if ( grips.Count > 0 ) Renderer.Animation.Parameters.TrySet( "weapon_grip_weight", -1f );
		}
		pending = playing = null;
		holdReaction = false;
		gripBlend = default;
	}

	protected override void OnDisabled() => ReleaseSlots();

	void Component.IAnimationEvent.OnAnimationEvent( AnimationEvent data )
	{
		if ( data is not Halo2CueEvent { Cue: { } cue } || data.Renderer != Renderer || !Bound ) return;
		if ( SampleCuesFromGraph ) DispatchCue( cue );
	}

	internal void DispatchCue( Halo2AnimationCue cue, GameObject soundOwner = null, bool playSound = true )
	{
		var position = cue.Marker is { Length: > 0 } marker ? Renderer.GetAttachment( marker ) ?? Renderer.WorldTransform : Renderer.WorldTransform;
		if ( playSound && cue.Kind == "sound" && cue.TagPath is not null )
		{
			if ( Sounds.TryGetValue( cue.TagPath, out var sound ) )
			{
				if ( soundOwner.IsValid() ) soundOwner.PlaySound( sound );
				else if ( sound.IsValid() ) Sound.Play( sound, position.Position );
			}
			else if ( bindings.Sounds.TryGetValue( cue.TagPath, out var playback ) )
				playback.Play( Scene, position.Position, soundOwner );
		}
		if ( cue.Kind == "effect" && cue.TagPath is not null && Effects.TryGetValue( cue.TagPath, out var effect ) && effect.IsValid() )
			effect.Clone( position );
		CueFired?.Invoke( cue, position );
		if ( pending is { } link && cue.Kind == $"transition_{(char)('a' + link.Keyframe - 1)}" ) StartTransition( link );
	}
}
