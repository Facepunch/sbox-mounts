using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	sealed class KnifeLayer( string name )
	{
		public readonly string Name = name;
		public readonly string PhaseParameter = name + "_phase", HeightParameter = name + "_height";
		public readonly string LeftParameter = name + "_left", WeightParameter = name + "_weight";
		public bool Active, Left, Fading;
		public float Height, Phase, Weight;
	}

	readonly KnifeLayer forehand = new( "r_knifeslash" ), backhand = new( "r_knifebackslash" );
	bool nextBackhand;
	TimeUntil knifeCooldown;
	KnifeLayer currentKnifeLayer;

	void BeginKnifeAttack( GameObject target )
	{
		strikeTarget = target;
		var layer = nextBackhand ? backhand : forehand;
		strike = boundAnimations.KnifeSwings[layer.Name];
		if ( MirroredStance ) MirroredStance = LeftHanded;
		flippedAttack = LeftHanded;
		AttackHeight = GetAttackHeight( target ) + Random.Shared.Float( 0f, 0.2f );
		AttackPhase = 0f;
		nextPrepare = 0;
		impactPassed = false;
		Speak( "attack" );
		PlayKnifeLayer( nextBackhand, LeftHanded, AttackHeight );
		nextBackhand = !nextBackhand;
		knifeCooldown = 0.3f;
		Stance = Movement.MoveTarget.HasValue ? Locomotion : Threat;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayKnifeLayer( bool reverse, bool left, float height )
	{
		var layer = reverse ? backhand : forehand;
		layer.Active = true;
		layer.Left = left;
		layer.Fading = false;
		layer.Height = height;
		layer.Phase = 0f;
		layer.Weight = 7f / 120f;
		currentKnifeLayer = layer;
		Renderer.Animation.Parameters.Set( "knife_backhand_newest", reverse );
	}

	void InterruptWeaponLayers()
	{
		if ( HasThrowLayer ) fadingThrow = true;
		if ( currentKnifeLayer is { Active: true, Fading: false } ) FadeKnifeLayer();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void FadeKnifeLayer()
	{
		if ( currentKnifeLayer is { Active: true } layer ) layer.Fading = true;
	}

	void UpdateKnifeLayers()
	{
		if ( !forehand.Active && !backhand.Active ) return;
		Advance( forehand );
		Advance( backhand );

		void Advance( KnifeLayer layer )
		{
			if ( !layer.Active ) return;
			if ( !boundAnimations.KnifeSwings.TryGetValue( layer.Name, out var move ) )
			{
				layer.Active = false;
				return;
			}
			var blend = move.Sample( layer.Height, layer.Left );
			layer.Phase = Math.Min( 1f, layer.Phase + Time.Delta * blend.Frequency );
			layer.Weight = Math.Clamp( layer.Weight + Time.Delta * (layer.Fading ? -4f : layer.Phase >= 1f ? -7f : 7f), 0f, 1f );
			var parameters = Renderer.Animation.Parameters;
			parameters.Set( layer.PhaseParameter, layer.Phase );
			parameters.Set( layer.HeightParameter, layer.Height );
			parameters.Set( layer.LeftParameter, layer.Left );
			parameters.Set( layer.WeightParameter, layer.Weight );
			layer.Active = layer.Weight > 0f;

			if ( !Networking.IsHost || strike != move || currentKnifeLayer != layer ) return;
			AttackPhase = layer.Phase;
			var clip = blend.Events;
			while ( layer.Active && nextPrepare < clip.PreparePhases.Length && layer.Phase >= clip.PreparePhases[nextPrepare] )
			{
				nextPrepare++;
				if ( strikeTarget.IsValid() && InStrikeReach( strikeTarget, CharacterCenter( strikeTarget ) - CharacterCenter( GameObject ) ) )
					strikeTarget.GetComponent<RabbitGuard>()?.TryActiveBlock( this, move );
			}
			if ( layer.Active && !impactPassed && clip.Impact is { } hit && layer.Phase >= hit.Time / clip.Duration )
			{
				impactPassed = true;
				TryHit( clip, hit.Bone );
			}
			if ( !layer.Active || impactPassed && knifeCooldown <= 0f )
			{
				strike = null;
				EndGroupAttack();
			}
		}
	}
}
