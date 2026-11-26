using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	sealed class KnifeLayer( string name )
	{
		public readonly string Name = name;
		public readonly string PhaseParameter = name + "_phase";
		public readonly string LeftParameter = name + "_left", WeightParameter = name + "_weight";
		public bool Active, Left, Fading;
		public float Phase, Weight;
		public double Remainder;
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
		PlayKnifeLayer( nextBackhand, LeftHanded );
		nextBackhand = !nextBackhand;
		knifeCooldown = 0.3f;
		Stance = Movement.MoveTarget.HasValue ? Locomotion : Threat;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayKnifeLayer( bool reverse, bool left )
	{
		var layer = reverse ? backhand : forehand;
		layer.Active = true;
		layer.Left = left;
		layer.Fading = false;
		layer.Phase = 0f;
		layer.Remainder = 0;
		layer.Weight = 7f / 120f;
		currentKnifeLayer = layer;
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
		Renderer.Animation.Parameters.Set( "knife_backhand_newest", currentKnifeLayer == backhand );
		Renderer.Animation.Parameters.Set( "attack_height_coord", AttackHeight );
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
			var blend = move.Sample( AttackHeight, layer.Left );
			const double step = 1.0 / 120.0;
			layer.Remainder += Time.Delta;
			while ( layer.Active && layer.Remainder >= step )
			{
				layer.Remainder -= step;
				layer.Phase = Math.Min( 1f, layer.Phase + (float)step * blend.Frequency );
				layer.Weight = Math.Clamp( layer.Weight + (float)step * (layer.Fading ? -4f : layer.Phase >= 1f ? -7f : 7f), 0f, 1f );
				layer.Active = layer.Weight > 0f;
				if ( Networking.IsHost && strike == move && currentKnifeLayer == layer )
					UpdateKnifeEvents( layer, move, blend.Events );
			}

			var parameters = Renderer.Animation.Parameters;
			parameters.Set( layer.PhaseParameter, layer.Phase );
			parameters.Set( layer.LeftParameter, layer.Left );
			parameters.Set( layer.WeightParameter, layer.Weight );
		}
	}

	void UpdateKnifeEvents( KnifeLayer layer, RabbitAttack move, RabbitClip clip )
	{
		AttackPhase = layer.Phase;
		while ( layer.Active && nextPrepare < clip.PreparePhases.Length && layer.Phase > clip.PreparePhases[nextPrepare] )
		{
			nextPrepare++;
			if ( strikeTarget.IsValid() && InStrikeReach( strikeTarget, CharacterCenter( strikeTarget ) - CharacterCenter( GameObject ) ) )
				strikeTarget.GetComponent<RabbitGuard>()?.TryActiveBlock( this, move );
		}
		if ( layer.Active && !impactPassed && clip.Impact is { } hit && layer.Phase > hit.Time / clip.Duration )
		{
			impactPassed = true;
			TryHit( clip, hit.Bone );
		}
		if ( !layer.Active || impactPassed && knifeCooldown <= 0f )
			strike = null;
	}
}
