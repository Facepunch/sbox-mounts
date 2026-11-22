using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Hide]
public abstract class ExpansionPredator : Npc
{
	protected GameObject Prey { get; private set; }
	internal virtual float MeleeDistance => 64;
	internal virtual StudioSequence DistanceAttack => Activity( Ranged );
	internal override IEnumerable<StudioSequence> ExtraSequences => [.. base.ExtraSequences, .. Activities( Melee ), .. Activities( Ranged )];

	internal override void BeginAttack( GameObject target )
	{
		Prey = target;
		var close = WorldPosition.Distance( target.WorldPosition ) <= MeleeDistance;
		var action = close ? WeightedActivity( Melee ) : DistanceAttack;
		if ( action is not null ) PlayAction( action );
	}
}
