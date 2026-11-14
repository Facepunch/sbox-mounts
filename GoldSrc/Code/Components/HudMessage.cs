using Sandbox;

namespace GoldSrc;

/// <summary>
/// env_message. Puts a titles.txt entry on screen, plays its sound, then fires its target like
/// any other trigger. CMessage::Use ignores the use type it was called with.
/// </summary>
[Title( "GoldSrc Message" )]
[Icon( "subtitles" )]
public sealed class HudMessage : MapEntity
{
	[Property] public TitleMessage Message { get; set; }

	/// <summary>The "messagesound" key.</summary>
	[Property] public SoundEvent Sound { get; set; }

	/// <summary>SF_MESSAGE_ONCE - CMessage removes itself once it has been used.</summary>
	[Property] public bool FireOnce { get; set; }

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || _fired && FireOnce ) return;
		_fired = true;
		Scene.GetSystem<MessageDisplay>()?.Show( Message );

		if ( Sound is not null )
			global::Sandbox.Sound.Play( Sound, WorldPosition );

		Fire( OnTrigger, activator );

		if ( FireOnce )
			GameObject.Destroy();
	}

	private bool _fired;

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );
}
