namespace Halo2;

internal sealed class AnimationSounds
{
	uint seed = (uint)System.Random.Shared.Next();
	int nextCue;

	internal void Restart() => nextCue = 0;

	internal void Advance( Halo2ClipInfo clip, float frame, FirstPersonAnimation data, Weapon weapon, GameObject view )
	{
		while ( nextCue < clip.Cues.Count && clip.Cues[nextCue].Frame < frame )
		{
			var cue = clip.Cues[nextCue++];
			if ( cue.Kind != "sound" || cue.TagPath is null || !data.Sounds.TryGetValue( cue.TagPath, out var sound ) ) continue;
			bool thirdPerson = weapon.Owner is { ThirdPerson: true };
			if ( thirdPerson && (cue.Flags & 8) != 0 ) continue;
			var emitter = thirdPerson ? weapon.WorldModel : view;
			if ( !emitter.IsValid() ) emitter = weapon.GameObject;
			sound.Play( emitter.WorldPosition, ref seed, emitter );
		}
	}
}
