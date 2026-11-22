namespace GoldSrc.Maps;

[Title( "GoldSrc Multi Manager" )]
[Icon( "schedule" )]
public sealed class MultiManager : MapEntity
{
	public sealed class TimedOutput
	{
		public string Target { get; set; }
		public float Delay { get; set; }
		public Doo Output { get; set; }
	}

	[Property] public List<TimedOutput> Events { get; set; } = [];
	[Property] public bool Threaded { get; set; }
	[Property, Hide] public bool IsClone { get; set; }
	[Property, Hide] public bool Running { get; set; }
	[Property, Hide] public float StartTime { get; set; }
	[Property, Hide] public int EventIndex { get; set; }
	[Property, Hide] public GameObject Activator { get; set; }

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		if ( Threaded && !IsClone )
		{
			var go = new GameObject( true, GameObject.Name ) { Parent = GameObject.Parent };
			go.WorldTransform = WorldTransform;
			var clone = go.AddComponent<MultiManager>();
			clone.Map = Map;
			clone.TargetName = TargetName;
			clone.Target = Target;
			clone.Events = Events;
			clone.IsClone = true;
			Map.Entities.Add( clone );
			clone.Trigger( activator );
			return;
		}
		if ( Running ) return;
		Running = true;
		Activator = activator;
		StartTime = Time.Now;
		EventIndex = 0;
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	protected override void OnFixedUpdate()
	{
		if ( !CanTrigger || !Running ) return;
		while ( EventIndex < Events.Count && Events[EventIndex].Delay <= Time.Now - StartTime )
		{
			var output = Events[EventIndex++].Output;
			Fire( output, Activator.IsValid() ? Activator : null );
			if ( !CanTrigger ) return;
		}
		if ( EventIndex < Events.Count ) return;
		Running = false;
		Activator = null;
		if ( IsClone ) Kill();
	}

	protected override void OnDestroy()
	{
		if ( Map.IsValid() ) Map.Entities.Remove( this );
		base.OnDestroy();
	}
}
