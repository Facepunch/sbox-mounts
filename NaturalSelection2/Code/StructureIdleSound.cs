namespace NS2;

[Hide]
internal sealed class StructureIdleSound : SparkSoundEmitter
{
	AlienStructure structure;

	protected override void OnStart()
	{
		structure = GetComponent<AlienStructure>();
		base.OnStart();
	}

	protected override void UpdatePlayback()
	{
		bool active = structure.IsValid() && structure.Health > 0 && structure.IsBuilt;
		if ( active == Playing ) return;
		if ( active ) StartEvent();
		else StopEvent();
	}
}
