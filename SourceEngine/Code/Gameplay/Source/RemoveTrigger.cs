namespace Source1;

[Hide]
public sealed class RemoveTrigger : PlayerTrigger
{
	[Property] public Doo OnRemove { get; set; }

	protected override void ActivateActor( GameObject actor )
	{
		if ( actor.GetComponent<PlayerController>() is not null || actor.Tags.Has( "world" ) ) return;

		Fire( OnRemove, actor );
		actor.Destroy();
	}
}
