namespace GoldSrc.HalfLife;

[Title( "Sitting Scientist" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeSittingScientist" )]
[NpcDefinition( "monster_sitting_scientist", "scientist", MonsterClass.HumanPassive, 50, 14, 36, 0, Movement = NpcMovement.Stationary, IdleSequence = "sitlookleft", Gibs = GibSet.Human, Blood = BloodColor.Red )]
public sealed class SittingScientist : ScientistBase
{
	static readonly string[] Poses = ["sitlookleft", "sitlookright", "sitscared", "sitting2", "sitting3"];
	internal override string[] ExtraActions => Poses;
	bool _started;
	[Sync( SyncFlags.FromHost )] float HeadTurn { get; set; }
	protected override float HeadYaw => HeadTurn;
	internal override float AnswerDelay( float questionDuration ) => Game.Random.Float( 3, 4 );

	internal override void OnModelLoaded( ModelLoader loader )
	{
		base.OnModelLoaded( loader );
		Animation.TurnsBody = false;
	}

	[Property, Hide] public bool PlacedOnMapHull { get; set; }

	internal override void PlaceAtSpawn()
	{
		if ( PlacedOnMapHull ) return;
		var radius = Definition.Radius;
		var hull = new BBox( new Vector3( -radius, -radius, 0 ), new Vector3( radius, radius, Definition.Height ) );
		var floor = Scene.Trace.Box( hull, WorldPosition, WorldPosition + Vector3.Down * 256 )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "npc", "player", "trigger" ).Run();
		if ( floor.Hit && !floor.StartedSolid ) WorldPosition = floor.EndPosition;
	}

	protected override void Think()
	{
		if ( !Renderer.IsValid() || !Renderer.Model.IsValid() ) return;
		if ( Find( Poses[0] ) is null ) return;
		if ( !_started )
		{
			_started = true;
			PlayAction( Find( Poses[Game.Random.Int( 0, Poses.Length - 1 )] ) );
			return;
		}

		GameObject player = !HasSpoken && CanStartConversation ? Senses?.FindNearestVisible( "player" ) : null;
		if ( player.IsValid() && player.WorldPosition.DistanceSquared( WorldPosition ) <= 500 * 500 && InViewCone( player.WorldPosition - WorldPosition ) )
		{
			Say( "PHELLO", player, Game.Random.Float( 3, 3.5f ) );
			LookAtFriend( player );
			return;
		}

		if ( AdvanceAction() ) return;
		HeadTurn = 0;
		if ( AnswerQuestion() )
		{
			PlayAction( Find( "sitscared" ) );
			return;
		}

		int choice = Game.Random.Int( 0, 99 );
		if ( choice < 60 )
		{
			if ( CanStartConversation && (choice < 30 || Game.Random.Int( 0, 99 ) < 5) )
			{
				var friend = NearestColleague();
				var listener = HasSpoken ? friend?.GameObject : player;
				if ( IdleSpeak( friend ) && choice < 30 && listener.IsValid() )
				{
					LookAtFriend( listener );
					return;
				}
			}
			HeadTurn = Game.Random.Int( 0, 8 ) * 10 - 40;
		}
		Animation.ClearLookTarget();
		PlayAction( Find( choice < 60 ? "sitting3" : choice < 80 ? "sitting2" : "sitscared" ) );
	}

	bool IdleSpeak( Personnel friend )
	{
		if ( friend.IsValid() && Game.Random.Int( 0, 1 ) == 1 )
		{
			EmitSentence( "PQUEST" );
			friend.ListenToQuestion( this, 3 );
		}
		else if ( Game.Random.Int( 0, 1 ) == 1 ) EmitSentence( "PIDLE" );
		else return false;
		PauseConversation( Game.Random.Float( 4.8f, 5.2f ) );
		return true;
	}

	void LookAtFriend( GameObject friend )
	{
		HeadTurn = 0;
		Animation.SetLookTarget( friend );
		bool left = WorldRotation.Left.Dot( friend.WorldPosition - WorldPosition ) > 0;
		PlayAction( Find( left ? "sitlookleft" : "sitlookright" ) );
	}
}
