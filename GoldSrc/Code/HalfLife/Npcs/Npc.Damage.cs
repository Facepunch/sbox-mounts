using Sandbox.Npcs;

namespace GoldSrc.HalfLife;

public partial class Npc
{
	protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
	{
		// BaseNpc clamps Health to zero. GoldSrc uses the excess damage to choose a death.
		_lastHit = damage.DamageInfo;
		_remainingHealth = _healthBeforeHit - damage.AcceptedDamage;
		_healthBeforeHit = Health;
		ShowWound( damage );
		if ( damage.AcceptedDamage > 0 ) GetComponent<Maps.Monster>()?.FireDamageTarget();
		if ( !_loaded || Health <= 0 )
			return;
		if ( damage.Instigator.IsValid() && GetDisposition( damage.Instigator ) == Disposition.Neutral )
			SetRelationship( damage.Instigator, Definition.Attack == NpcAttack.None ? Disposition.Fearful : Disposition.Hostile );
		if ( SelectFlinch( damage ) is { } flinch )
		{
			Movement?.Stop();
			PlayAction( flinch );
		}
		PainSound();
	}

	const int FlinchHead = 70, FlinchStomach = 72, FlinchLeftArm = 73, FlinchRightArm = 74, FlinchLeftLeg = 75, FlinchRightLeg = 76;

	IEnumerable<StudioSequence> FlinchSequences => _sequences.Values.Where( s => s.Description.Activity is Flinch or >= FlinchHead and <= FlinchRightLeg );

	internal virtual StudioSequence SelectFlinch( Component.IDamageable.DamageEvent damage ) => _action is null ? SmallFlinch( damage.DamageInfo ) : null;

	internal StudioSequence SmallFlinch( DamageInfo info )
	{
		if ( HitStudioModel( info ) )
		{
			var tags = HitGroups( info );
			var activity = IsHeadHit( tags ) ? FlinchHead
				: tags.Has( "hitgroup_3" ) ? FlinchStomach
				: tags.Has( "hitgroup_4" ) ? FlinchLeftArm
				: tags.Has( "hitgroup_5" ) ? FlinchRightArm
				: tags.Has( "hitgroup_6" ) ? FlinchLeftLeg
				: tags.Has( "hitgroup_7" ) ? FlinchRightLeg : Flinch;
			if ( activity != Flinch && WeightedActivity( activity ) is { } regional ) return regional;
		}
		return WeightedActivity( Flinch );
	}
}
