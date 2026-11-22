namespace GoldSrc.HalfLife;

[Title( "Scientist" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeScientist" )]
[NpcDefinition( "monster_scientist", "scientist", MonsterClass.HumanPassive, 20, 16, 72, 105, Gibs = GibSet.Human, Blood = BloodColor.Red )]
public partial class Scientist : ScientistBase
{
	internal override string[] ExtraActions => ["walk_scared", "run2", "panic", "crouch"];
	[Sync( SyncFlags.FromHost )] TimeUntil AfraidUntil { get; set; }

	internal override IEnumerable<StudioSequence> ExtraSequences => [.. base.ExtraSequences, .. Activities( CrouchIdle ), .. Activities( RunScared ), _drawNeedle, _inject, _putAway];

	protected override void Think()
	{
		if ( TurnToPatient() ) return;
		UpdateTreatment();
		if ( RespondToDanger() ) CancelAnswer();
		else base.Think();
	}

	internal override void FinishAction( string name )
	{
		if ( name == "crouch" && _startled )
		{
			_startled = false;
			if ( Game.Random.Float() < 0.1f ) Scream();
		}
		FinishTreatmentAction( name );
	}

	internal override void OnModelLoaded( ModelLoader loader )
	{
		base.OnModelLoaded( loader );
		EyeHeight = 50;
		SetUpTreatment( loader );
	}

	protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
	{
		base.OnDamaged( damage );
		if ( Health < 1 || damage.Instigator?.GetComponentInParent<PlayerController>() is not { } player ) return;
		if ( Leader.IsValid() ) StopFollowing( false );
		AfraidUntil = 15;
		SetRelationship( player.GameObject, Sandbox.Npcs.Disposition.Fearful );
	}
}
