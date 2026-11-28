using Sandbox.Npcs;

namespace GoldSrc.Maps;

[Title( "GoldSrc Monster Jump" )]
public sealed class MonsterJump : MapEntity, Component.ITriggerListener
{
	[Property] public Collider Volume { get; set; }
	[Property] public Vector3 Velocity { get; set; }

	public override void Trigger( GameObject activator = null )
	{
		if ( Volume.IsValid() ) Volume.Enabled = !Volume.Enabled;
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( !Networking.IsHost || !CanTrigger || !Volume.Enabled || other.GetComponentInParent<BaseNpc>() is not { } npc ) return;
		npc.WorldPosition += Vector3.Up;
		npc.Launch( Velocity );
		Volume.Enabled = false;
	}
}
