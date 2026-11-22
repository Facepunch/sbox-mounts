namespace GoldSrc.Maps;

[Category( "GoldSrc" ), Icon( "account_tree" )]
public sealed class MultiSource : MapEntity
{
	[Property] public List<MapEntity> Inputs { get; set; } = [];
	[Property] public bool[] InputStates { get; set; } = [];
	[Property] public string GlobalState { get; set; }
	[Property, Hide] public bool Initialized { get; set; }

	private float _initializeAt;

	public bool IsTriggered => Initialized && InputStates.Length == Inputs.Count
		&& InputStates.All( x => x )
		&& (string.IsNullOrEmpty( GlobalState ) || Map.GetGlobal( GlobalState ) == 1);

	protected override void OnStart()
	{
		if ( InputStates.Length != Inputs.Count )
			InputStates = new bool[Inputs.Count];
		_initializeAt = Time.Now + 0.1f;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Initialized && Time.Now >= _initializeAt )
		{
			Inputs = Map.Entities.Where( x => x.IsValid() && !x.GameObject.IsDestroyed && x.Target == TargetName )
				.Concat( Map.Entities.OfType<MultiManager>().Where( x => x.IsValid() && !x.GameObject.IsDestroyed
					&& x.Events.Any( e => e.Target == TargetName ) ) ).Take( 32 ).ToList();
			InputStates = new bool[Inputs.Count];
			Initialized = true;
		}
	}

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		int index = Inputs.IndexOf( Caller );
		if ( index < 0 || index >= InputStates.Length )
		{
			Log.Warning( $"GoldSrc: multisource '{TargetName}' was used by a non-member." );
			return;
		}
		InputStates[index] = !InputStates[index];
		if ( IsTriggered )
			Fire( OnTrigger );
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );
}
