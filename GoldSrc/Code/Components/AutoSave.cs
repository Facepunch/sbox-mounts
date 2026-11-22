namespace GoldSrc.Maps;

public sealed class AutoSave : MapEntity, Component.ITriggerListener
{
	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( !CanTrigger || !Networking.IsHost ) return;
		var player = other.GetComponentInParent<PlayerController>();
		if ( !player.IsValid() || !IsMasterTriggered( player.GameObject ) ) return;
		if ( Map.SaveCheckpoint( this ) ) Kill();
	}

	protected override void OnFixedUpdate()
	{
		// A multisource can unlock while the player is already inside the volume.
		if ( string.IsNullOrEmpty( Master ) || !Networking.IsHost || !CanTrigger ) return;
		foreach ( var other in GetComponent<Collider>().Touching )
		{
			((ITriggerListener)this).OnTriggerEnter( other );
			if ( !CanTrigger ) break;
		}
	}
}
