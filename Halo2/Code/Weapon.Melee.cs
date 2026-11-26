using System;
using System.Runtime.CompilerServices;

namespace Halo2;

public sealed record MeleeDamageProfile( float Minimum, float Maximum, float Force, Dictionary<string, float> BodyScales, bool Explosive );

public sealed record MeleeContactSounds( bool AtContact, Dictionary<string, int[]> Materials, CuePlayback[] Cues );

public abstract partial class Weapon
{
	[Property] public Dictionary<string, float> MeleeBodyScales { get; set; }
	[Property] public bool MeleeExplosive { get; set; }
	[Property] public float MinimumMeleeDamage { get; set; } = 20;
	[Property] public float MeleeForwardSpeed { get; set; } = 2.25f;
	[Property] public float MeleeDamage { get; set; }
	[Property] public float MeleeForce { get; set; }
	[Property] public Vector2 MeleeHalfExtents { get; set; }
	[Property] public float MeleeRange { get; set; }

	sealed class MeleeRecovery
	{
		public TimeUntil End;
		public TimeUntil NextStrike;
		public string Action;
		public uint Interruptions;
	}

	// Halo advances the melee action on the unit, including while its inventory changes.
	static readonly ConditionalWeakTable<GameObject, MeleeRecovery> meleeRecovery = new();
	GameObject MeleeOwner => Inventory?.GameObject ?? GameObject;
	protected bool MeleeActive => Recovery.End > 0;
	protected string MeleeAction => Recovery.Action;
	MeleeRecovery Recovery => meleeRecovery.GetValue( MeleeOwner, static _ => new MeleeRecovery() );

	protected abstract string FirstPersonRig { get; }
	protected virtual FirstPersonAnimation FirstPersonAnimations() => Model.Load( FirstPersonRig )?.GetData<FirstPersonAnimation>();
	protected abstract void PlayMeleeView( string action );
	[Property] public MeleeContactSounds ContactSounds { get; set; }
	uint contactSeed = (uint)System.Random.Shared.Next();

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayContactSounds( string surfacePath, Vector3 position )
	{
		if ( Application.IsDedicatedServer || ContactSounds is null || string.IsNullOrEmpty( surfacePath ) ) return;
		var indices = ContactMaterial.Lookup( ContactSounds.Materials, GameResource.Load<Surface>( surfacePath ), false );
		if ( indices is null ) return;
		foreach ( var index in indices )
			ContactSounds.Cues[index].Play( position, ref contactSeed, null );
	}
	protected bool IsHolstered => Inventory is not null && !IsInHand;
	protected bool CanUseHeldWeapon => !PuttingAway && !IsHolstered;

	public override bool CanReload() => !MeleeActive && CanUseHeldWeapon && base.CanReload();
	public override bool CanPrimaryAttack() => !MeleeActive && CanUseHeldWeapon && base.CanPrimaryAttack();

	protected override bool WantsSecondaryAttack() => !Offhand.IsValid() && !IsOffhand && NextSecondaryFire <= 0 && base.WantsSecondaryAttack();

	public override bool CanSecondaryAttack() => NextSecondaryFire <= 0 && Recovery.NextStrike <= 0 && CanUseHeldWeapon;

	public override void SecondaryAttack() => BeginMelee( false );

	protected void BeginMelee( bool primary )
	{
		if ( Networking.IsHost ) StartMelee( primary );
		else RequestMelee( primary );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestMelee( bool primary )
	{
		if ( IsActive && CanSecondaryAttack() && (!primary || CanPrimaryAttack()) ) StartMelee( primary );
	}

	protected virtual string PrimaryMeleeAction() => null;
	protected virtual MeleeDamageProfile LungeProfile => null;

	static readonly string[] strikeActions = ["melee_strike_1", "melee_strike_2", "melee_strike_3", "melee_strike_4"];

	void StartMelee( bool primary )
	{
		var actions = FirstPersonAnimations()?.Actions;
		if ( actions is null ) return;
		var action = primary ? PrimaryMeleeAction() : null;
		if ( action is null )
		{
			Span<int> available = stackalloc int[strikeActions.Length];
			int count = 0;
			for ( int i = 0; i < strikeActions.Length; i++ )
				if ( actions.ContainsKey( strikeActions[i] ) ) available[count++] = i;

			if ( count == 0 ) return;
			var roll = BlamRandom.Next( ref BlamRandom.GameplayState( Scene ) );
			action = strikeActions[available[(int)(roll * (uint)count >> 16)]];
		}
		PerformMeleeAction( action, actions );
	}

	protected void PerformMeleeAction( string action, IReadOnlyDictionary<string, Halo2ClipInfo> actions )
	{
		if ( actions?.TryGetValue( action, out var clip ) != true ) return;
		bool lunge = action is "melee_lunge" or "melee_lunge_airborne";
		CancelReload();
		float duration = CooldownSeconds( clip.Frames );
		int recovery = clip.Cues.FirstOrDefault( cue => cue.Kind == "secondary_keyframe" )?.Frame ?? clip.Frames;
		PlayMeleeAnimation( MeleeOwner, action, duration, CooldownSeconds( recovery ) );
		if ( action is not ("melee_dash" or "melee_dash_airborne") && clip.Cues.FirstOrDefault( cue => cue.Kind == "primary_keyframe" ) is {} impact )
			_ = ResolveStrike( Inventory, Attacker, StrikeFraction(), lunge, Time.NowDouble + impact.Frame / 30.0 );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayMeleeAnimation( GameObject wielder, string action, float duration, float recovery )
	{
		if ( !wielder.IsValid() ) return;
		var state = meleeRecovery.GetValue( wielder, static _ => new MeleeRecovery() );
		state.Action = action;
		state.End = duration;
		state.NextStrike = recovery;
		if ( wielder != MeleeOwner ) return;
		SetNextSecondaryFire( 0 );
		SetZoom( 0 );
		PlayMeleeView( action );
		PlayHolderOverlay( action );
	}

	protected void InterruptMelee()
	{
		var state = Recovery;
		if ( state.Action is not {} action ) return;
		state.Action = null;
		state.End = state.NextStrike = 0;
		state.Interruptions++;
		HolderAnimator?.StopWeaponOverlay( action );
		PlayMeleeView( "idle" );
	}

	float StrikeFraction()
	{
		var holder = Holder;
		var player = holder as PlayerController;
		var horizontalAim = player.IsValid() ? Rotation.FromYaw( player.EyeAngles.yaw ).Forward : AimRay.Forward.WithZ( 0 ).Normal;
		var velocity = Sandbox.Npcs.BaseNpc.GetVelocity( holder?.GameObject );
		float forward = Vector3.Dot( velocity, horizontalAim ) / 120;
		// The action stores a byte at swing start. A moving strike uses 127 of the 255 damage steps.
		return MeleeForwardSpeed > 0 && forward / MeleeForwardSpeed > 0.9f ? 127f / 255f : 0;
	}

	async System.Threading.Tasks.Task ResolveStrike( BaseInventoryComponent inventory, GameObject attacker, float fraction, bool lunge, double impactAt )
	{
		// The unit owns the swing. Its equipped weapon supplies the contact geometry on the impact tick.
		var clock = TaskSource.Create( (inventory?.GameObject ?? GameObject).EnabledToken );
		var state = Recovery;
		uint interruptions = state.Interruptions;
		while ( Time.NowDouble < impactAt ) await clock.Frame();
		if ( state.Interruptions != interruptions ) return;
		var weapon = inventory.IsValid() ? inventory.ActiveItem as Weapon : null;
		if ( inventory is null && IsValid && !IsHeld ) weapon = this;
		if ( weapon.IsValid() ) weapon.MeleeContact( attacker, fraction, lunge ? weapon.LungeProfile : null );
	}

	void MeleeContact( GameObject attacker, float fraction, MeleeDamageProfile profile )
	{
		var aim = Holder is PlayerController player ? player.EyeTransform.ForwardRay : AimRay;
		var hit = TraceMelee( aim, out var lastContact );
		if ( !hit.Hit ) return;
		if ( ContactSounds is {} sounds )
		{
			// Melee-only material effects use the final cast point plus 0.01 Halo world units.
			var position = sounds.AtContact ? lastContact.HitPosition + lastContact.Normal * 1.2f : MeleeOwner.WorldPosition;
			PlayContactSounds( hit.Surface?.ResourcePath, position );
		}
		// The contact rays select the recipient. The damage request carries the attacker's aim.
		hit.Direction = aim.Forward;
		float damage = (1 - fraction) * (profile?.Minimum ?? MinimumMeleeDamage) + fraction * (profile?.Maximum ?? MeleeDamage);
		ApplyDelayedHit( hit, damage * BodyMaterial.DamageScale( hit, profile?.BodyScales ?? MeleeBodyScales, profile?.Explosive ?? MeleeExplosive ), profile?.Force ?? MeleeForce, attacker, new TagSet { "melee" }, false );
	}

	protected void ApplyDelayedHit( SceneTraceResult hit, float damage, float force, GameObject attacker, TagSet tags, bool hitboxTags = true )
	{
		if ( IsValid && (!Networking.IsHost || attacker == Attacker) )
		{
			ShootBullet( hit, damage, force, tags, hitboxTags );
			return;
		}

		var source = IsValid ? GameObject : null;
		force = GetHitForce( hit.GameObject, force );
		ShootBullet( hit, damage, float.IsFinite( force ) ? System.MathF.Max( 0, force ) : 0,
			attacker.IsValid() ? attacker : source, source, tags, hitboxTags );
	}

	internal static Vector3 Perpendicular( Vector3 forward )
	{
		var abs = forward.Abs();
		// Halo constructs the two perpendicular axes from the smallest component.
		return (abs.x <= abs.y && abs.x <= abs.z ? new Vector3( 0, forward.z, -forward.y )
			: abs.y <= abs.z ? new Vector3( -forward.z, 0, forward.x ) : new Vector3( forward.y, -forward.x, 0 )).Normal;
	}

	SceneTraceResult TraceMelee( Ray aim, out SceneTraceResult lastContact )
	{
		lastContact = default;
		var forward = aim.Forward;
		var axis = Perpendicular( forward );
		var stepY = Vector3.Cross( forward, axis ) * (MeleeHalfExtents.y * 0.5f);
		var stepX = axis * (MeleeHalfExtents.x * 0.5f);
		var center = aim.Position + forward * MeleeRange;
		var trace = BulletTrace( aim, MeleeRange, 0 );
		SceneTraceResult best = default;
		int priority = -1;
		for ( int x = -2; x <= 2; x++ )
		for ( int y = -2; y <= 2; y++ )
		{
			var hit = trace.FromTo( aim.Position, center + stepX * x + stepY * y ).Run();
			if ( !hit.Hit || !hit.GameObject.IsValid() ) continue;

			// The last contact positions the effect, even when an earlier target wins.
			lastContact = hit;

			// The first object beats scenery. A biped beats other objects and the closest biped wins.
			var obj = hit.GameObject;
			bool character = obj.GetComponentInParent<PlayerController>().IsValid() || obj.GetComponentInParent<Sandbox.Npcs.BaseNpc>().IsValid();
			int rank = character ? 2 : 0;
			if ( !character && (obj.GetComponentInParent<Rigidbody>().IsValid() || obj.GetComponentInParent<Component.IDamageable>() is not null) ) rank = 1;
			if ( rank < priority ) continue;
			if ( rank == 1 && priority == 1 ) continue;
			if ( rank == 2 && priority == 2 && hit.Fraction >= best.Fraction ) continue;
			best = hit;
			priority = rank;
		}
		return best;
	}
}
