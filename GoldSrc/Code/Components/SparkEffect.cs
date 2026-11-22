namespace GoldSrc.Maps;

[Title( "GoldSrc Sparks" ), Icon( "bolt" )]
public sealed class SparkEffect : MapEntity
{
	[Property] public SoundEvent[] Sounds { get; set; } = [];
	[Property] public bool Toggleable { get; set; }
	[Property] public bool StartOn { get; set; }
	[Property] public float MaxDelay { get; set; } = 1.5f;

	public bool IsOn { get; private set; }
	private TimeUntil _nextSpark;
	private SoundHandle _handle;

	protected override void OnStart()
	{
		IsOn = !Toggleable || StartOn;
		_nextSpark = 0.1f + Game.Random.Float( 0, 1.5f );
	}

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || !Toggleable ) return;
		IsOn = !IsOn;
		if ( IsOn ) Schedule();
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	private void Schedule() => _nextSpark = 0.1f + Game.Random.Float( 0, MaxDelay <= 0 ? 1.5f : MaxDelay );

	protected override void OnUpdate()
	{
		if ( !IsOn || _nextSpark > 0 ) return;
		Schedule();
		Emit();
	}

	public void Emit()
	{
		if ( !CanTrigger ) return;
		EmitVisuals();
		var volume = Game.Random.Float( 0.25f, 0.75f ) * 0.4f;
		var index = (int)(Game.Random.Float( 0, 1 ) * 6);
		if ( index >= Sounds.Length || Sounds[index] is null ) return;
		_handle?.Stop();
		_handle = Sound.Play( Sounds[index], WorldPosition );
		if ( _handle.IsValid() ) _handle.Volume = volume;
	}

	public void EmitVisuals()
	{
		if ( !CanTrigger ) return;
		SparkBurst.Spawn( Map.IsValid() ? Map.GameObject : GameObject.Parent, WorldPosition );
	}

	protected override void OnDisabled() => _handle?.Stop();
}
