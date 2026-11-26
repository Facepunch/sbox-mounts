using System;

namespace NS2;

public abstract partial class AlienLifeform : AlienNpc, Component.IDamageable.IEvents
{
	private protected SparkAnimationGraph.Playback world;
	[Sync( SyncFlags.FromHost )] protected TimeSince AnimationTime { get; set; }
	bool railgunDeath;

	private protected readonly AlienWounds wounds = new();
	protected TimeUntil underFire;
	protected Vector3 LastDamageOrigin { get; private set; }
	TimeUntil dealtDamageRecently;

	void IDamageable.IEvents.OnDamaged( IDamageable.DamageEvent hit )
	{
		if ( !Networking.IsHost || hit.AcceptedDamage <= 0 ) return;
		if ( hit.Instigator == GameObject ) dealtDamageRecently = 3;
	}

	internal bool InCombat => underFire > 0 || dealtDamageRecently > 0;

	protected override void OnDamaging( IDamageable.DamageEvent hit )
	{
		hit.AcceptedDamage *= AlienArmor.UmbraScale( hit );
		AbsorbBabblerShield( hit );
		float armor = ((IDamageable)this).Armour;
		AlienArmor.ApplyPlayerRules( hit, this, armor );
		if ( hit.Cancelled || hit.AcceptedDamage <= 0 ) return;
		hit.AcceptedDamage = BlockDamage( hit.DamageInfo, hit.AcceptedDamage );
		AlienArmor.Absorb( hit, armor );
	}

	protected virtual float BlockDamage( DamageInfo hit, float damage ) => damage;

	protected override void OnDamaged( IDamageable.DamageEvent hit )
	{
		float armorOnlyDamage = AlienArmor.ArmorOnlyDamage( hit );
		var hp = (IDamageable)this;
		if ( armorOnlyDamage > 0 ) armorOnlyDamage = BlockDamage( hit.DamageInfo, Math.Min( armorOnlyDamage, hp.Armour * 2 ) );
		if ( hit.AcceptedDamage <= 0 && armorOnlyDamage <= 0 ) return;
		hp.Armour -= wounds.ApplyDamage( hit, hp.Armour, MaxHealth, hp.MaxArmour, armorOnlyDamage );
		LastDamageOrigin = hit.DamageInfo.Origin;
		underFire = 3;
	}

	protected abstract int BaseHealth { get; }
	[Property, Range( 0, 3 ), Sync( SyncFlags.FromHost )] public int RegenerationShells { get; set; }
	[Sync( SyncFlags.FromHost )] TimeUntil nextHeal { get; set; }

	protected void Regenerate( bool underFire )
	{
		if ( nextHeal > 0 ) return;
		nextHeal = 2;
		if ( underFire ) return;

		int shells = Math.Clamp( RegenerationShells, 0, 3 );
		float amount = shells == 0 ? Math.Clamp( BaseHealth * 0.02f, 1, 20 )
			: Math.Clamp( BaseHealth * 0.06f, 6, 80 ) * shells / 3;
		AlienHealing.Restore( this, amount, 0.5f, feedback: shells > 0 ? HealFeedback.Self : HealFeedback.Hidden );
	}

	protected override void OnDeath( IDamageable.DeathEvent e )
	{
		while ( brood.Count > 0 ) BreakBabbler( brood[0] );
		railgunDeath = e.DamageInfo.Tags.Has( "railgun" );
		if ( railgunDeath ) ShowRailgunDeath( GetType().Name.ToLowerInvariant(), WorldPosition );
		base.OnDeath( e );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ShowRailgunDeath( string lifeform, Vector3 position ) =>
		CinematicLoader.Play( $"cinematics/alien/{lifeform}/explode.cinematic", position, Rotation.Identity );

	protected override GameObject CreateRagdoll()
	{
		if ( world is null ) return railgunDeath ? null : NpcCorpse.Attach( base.CreateRagdoll(), 3f );
		return NpcCorpse.AnimatePlayer( Renderer, world, AnimationTime, railgunDeath );
	}
}
