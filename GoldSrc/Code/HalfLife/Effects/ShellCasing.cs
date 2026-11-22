namespace GoldSrc.HalfLife;

[Hide]
public sealed class ShellCasing : Component, Component.ICollisionListener
{
	bool _shotgun;
	ClassicBounce _classic;

	internal static void Eject( Vector3 origin, Rotation facing, bool shotgun, Vector3 velocity )
	{
		if ( Application.IsDedicatedServer ) return;
		var shell = new GameObject( false, shotgun ? "Shotgun shell" : "Brass casing" );
		shell.NetworkMode = NetworkMode.Never;
		shell.Tags.Add( "debris" );
		shell.Tags.Add( "nocollide_props" );
		shell.WorldPosition = origin;
		shell.WorldRotation = Rotation.FromYaw( facing.Angles().yaw );
		var model = Model.Load( shotgun ? "mount://hl1/valve/models/shotgunshell.mdl.vmdl" : "mount://hl1/valve/models/shell.mdl.vmdl" );
		shell.AddComponent<ModelRenderer>().Model = model;
		shell.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 2.5f;
		var casing = shell.AddComponent<ShellCasing>();
		casing._shotgun = shotgun;
		if ( ClassicBounce.Enabled )
		{
			casing._classic = new( velocity, new Angles( 0, Game.Random.Float( -180, 180 ), Game.Random.Float( -360, 360 ) ), 1, 0.5f );
			shell.Enabled = true;
			return;
		}
		var collider = shell.AddComponent<ModelCollider>();
		collider.Model = model;
		collider.ColliderFlags |= ColliderFlags.IgnoreTraces;
		collider.Elasticity = 0.35f;
		var body = shell.AddComponent<Rigidbody>();
		body.RigidbodyFlags |= RigidbodyFlags.DisableCollisionSounds;
		body.EnhancedCcd = true;
		shell.Enabled = true;
		body.AngularVelocity = facing.Forward * Game.Random.Float( -14, 14 );
		body.Velocity = velocity;
	}

	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( !collision.Other.IsTrigger ) PlayBounce( collision.Contact.Point, System.MathF.Abs( collision.Contact.Speed.z ) );
	}

	protected override void OnFixedUpdate()
	{
		if ( _classic is null ) return;
		var speed = System.MathF.Abs( _classic.Velocity.z );
		var hit = _classic.Step( GameObject, worldOnly: true, shell: true );
		if ( hit.Hit && !_classic.Settled ) PlayBounce( hit.EndPosition, speed );
	}

	void PlayBounce( Vector3 point, float speed )
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ) return;
		if ( speed <= 0 || (speed < 200 && Game.Random.Int( 0, 3 ) != 0) ) return;
		var sample = _shotgun ? "weapons/sshell" : "player/pl_shell";
		var volume = (_shotgun ? 0.5f : 0.8f) * System.MathF.Min( speed / 350, 1 );
		NpcAudio.Play( mount.NpcAudio.Find( sample ), point, volume );
	}
}
