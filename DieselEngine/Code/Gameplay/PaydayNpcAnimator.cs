using Sandbox.Npcs;

namespace Diesel;

[Title( "PAYDAY Character Animation" ), Group( "PAYDAY" )]
public sealed class PaydayNpcAnimator : Component
{
	[Property] public string CharacterId { get; set; } = "civilian";
	[Property] public SkinnedModelRenderer Renderer { get; set; }
	public string CurrentClip { get; private set; } = "idle";
	PaydayCharacter assets;
	float elapsed;
	Dictionary<uint, Transform> previous;
	float blend;

	protected override void OnUpdate()
	{
		var npc = GetComponent<BaseNpc>();
		if ( !npc.IsValid() ) return;
		float speed = npc.Animation?.MoveVelocity.Length ?? 0;
		string clip = npc is PaydayDisablerNpc { AttackKind: PaydayDisablerNpc.AttackKinds.Cloaker, AttackPhase: PaydayDisablerNpc.AttackPhases.Windup } ? "special"
			: npc is BaseCombatNpc { Weapon.IsReloading: true } ? "reload"
			: npc is PaydayCivilian { Surrendered: true, Leader: null } ? "surrender"
			: speed > 140 ? "run" : speed > 10 ? "walk" : "idle";
		if ( clip != CurrentClip ) { CurrentClip = clip; elapsed = 0; blend = 0; }
		elapsed += Time.Delta;
		SamplePose( CurrentClip, elapsed );
	}

	public void SamplePose( string clip, float time )
	{
		if ( !Renderer.IsValid() ) return;
		if ( assets is null && Sandbox.Mounting.Directory.Get( "payday" ) is PaydayMount mount )
			assets = mount.Characters.GetValueOrDefault( CharacterId );
		if ( assets is null ) return;
		var animation = assets.Clips[clip];
		var pose = assets.Sample( clip, clip == "special" ? Math.Min( time, animation.Duration ) : time % Math.Max( animation.Duration, .001f ) );
		blend = Math.Min( 1, blend + Time.Delta * 8 );
		foreach ( var bone in assets.Bones )
		{
			var tx = pose[bone.Id];
			if ( previous?.TryGetValue( bone.Id, out var old ) == true && blend < 1 )
				tx = new Transform( Vector3.Lerp( old.Position, tx.Position, blend ), Rotation.Slerp( old.Rotation, tx.Rotation, blend ) );
			pose[bone.Id] = tx;
			var target = Renderer.Model.Bones.GetBone( bone.Name );
			if ( target is not null ) Renderer.SetBoneTransform( target, tx );
		}
		previous = pose;
	}
}
