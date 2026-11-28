namespace Source1;

[Hide]
public sealed class PhysicsBrush : Component, IMapInput, IMapOutputs, Component.ICollisionListener
{
	[Property] public float ForceToEnableMotion { get; set; }
	[Property] public bool IgnorePlayerBumps { get; set; }
	[Property] public Rigidbody Body { get; set; }
	[Property] public Doo OnMotionEnabled { get; set; }

	void IMapOutputs.Wire( System.Func<string, bool, Doo> output ) => OnMotionEnabled = output( "OnMotionEnabled", true );

	Doo.Block IMapInput.BuildInput( string input, string parameter, bool hasUser ) => input.ToLowerInvariant() switch
	{
		"enablemotion" => this.Call( nameof( EnableMotion ) ),
		"disablemotion" => this.Call( nameof( DisableMotion ) ),
		"wake" => this.Call( nameof( Wake ) ),
		"sleep" => this.Call( nameof( Sleep ) ),
		_ => null
	};

	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( !Networking.IsHost || ForceToEnableMotion <= 0 || collision.Other.Body is not { } other ) return;
		if ( IgnorePlayerBumps && collision.Other.GameObject.GetComponentInParent<PlayerController>().IsValid() ) return;
		if ( collision.Contact.Speed.Length * other.Mass >= ForceToEnableMotion ) EnableMotion();
	}

	public void EnableMotion()
	{
		ForceToEnableMotion = 0;
		Body.CollisionEventsEnabled = false;
		Body.MotionEnabled = true;
		Wake();
		RunDoo( OnMotionEnabled, c => c.SetArgument( "user", GameObject ) );
	}

	public void DisableMotion() => Body.MotionEnabled = false;
	public void Wake() => Body.Sleeping = false;
	public void Sleep() => Body.Sleeping = true;
}
