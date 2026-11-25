using System.Runtime.CompilerServices;

namespace NS2;

[Hide]
internal sealed class SupportPain : Component
{
	[Property] SoundEvent Main { get; set; }
	[Property] SoundEvent Impact { get; set; }
	[Property] int Limit { get; set; }
	[Property] bool StealOldest { get; set; }
	[Property] float FadeOut { get; set; }
	[Property] float Reverb { get; set; }
	[Property] float Pan { get; set; }
	[Property] Curve? DistanceReverb { get; set; }
	[Property] Curve? DistanceOcclusion { get; set; }

	TimeUntil observed, nextCheck;
	readonly record struct Voice( SoundHandle Main, SoundHandle Impact )
	{
		internal bool IsPlaying => Main?.IsPlaying == true || Impact?.IsPlaying == true;
	}
	static readonly ConditionalWeakTable<SoundEvent, List<Voice>> ActiveVoices = new();

	internal void Configure( GameMount mount, string id )
	{
		var definition = mount.FindSoundEvent( $"sound/NS2.fev/{id}" );
		if ( definition is not { Layers.Length: > 0 } ) return;
		Main = GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{id}.sound" );
		if ( definition.Layers.Length > 1 )
			Impact = GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{id}_impact.sound" );
		Limit = definition.Limit;
		StealOldest = definition.Steal == 1;
		FadeOut = definition.FadeOut;
		Reverb = definition.Reverb;
		Pan = definition.Pan;
		foreach ( var env in definition.Layers[0].Envelopes )
		{
			if ( env.ParameterName != "(distance)" ) continue;
			var curve = new Curve( env.Points.Select( p => new Curve.Frame( p.x, p.y ) { Mode = Curve.HandleMode.Linear } ) )
			{
				TimeRange = new Vector2( env.Min, env.Max )
			};
			if ( env.Effect == SoundProject.Effect.Reverb ) DistanceReverb = curve;
			if ( env.Effect == SoundProject.Effect.Occlusion ) DistanceOcclusion = curve;
		}
	}

	internal void StartListening( TimeUntil end )
	{
		observed = end;
		nextCheck = 0.3f;
	}

	// FlinchMixin batches hits on each client. The replicated deadline changes on every hit.
	internal void UpdateSound( TimeUntil end )
	{
		if ( Application.IsDedicatedServer || nextCheck > 0 ) return;
		nextCheck = 0.3f;
		if ( end == observed ) return;
		observed = end;
		if ( Main is null ) return;
		var voices = ActiveVoices.GetOrCreateValue( Main );
		voices.RemoveAll( v => !v.IsPlaying );
		if ( Limit > 0 && voices.Count >= Limit )
		{
			if ( !StealOldest ) return;
			voices[0].Main?.Stop( FadeOut );
			voices[0].Impact?.Stop( FadeOut );
			voices.RemoveAt( 0 );
		}

		var main = Sound.Play( Main, WorldPosition );
		if ( main.IsValid() )
		{
			float meters = WorldPosition.Distance( Sound.Listener.Position ) / MapLoader.Scale;
			main.Reverb = DistanceReverb?.Evaluate( meters ) ?? Reverb;
			main.SpacialBlend = Pan;
			if ( DistanceOcclusion is { } occlusion ) main.OcclusionEnabled = occlusion.Evaluate( meters ) > 0;
		}
		var impact = Impact is null ? null : Sound.Play( Impact, WorldPosition );
		if ( impact.IsValid() )
		{
			impact.Reverb = Reverb;
			impact.SpacialBlend = Pan;
		}
		voices.Add( new( main, impact ) );
	}
}
