using Sandbox;

namespace GoldSrc;

/// <summary>
/// One entry from the game's titles.txt - the text an env_message shows, plus the display
/// parameters that were in effect where it was declared. Mirrors client_textmessage_t.
/// </summary>
public struct TitleMessage
{
	/// <summary>The text. Newlines split it into lines, drawn one below the other.</summary>
	[Property, TextArea] public string Text { get; set; }

	/// <summary>
	/// Screen position, 0 to 1. -1 centres that axis, and any other negative value aligns to the
	/// right or bottom edge.
	/// </summary>
	[Property] public Vector2 Position { get; set; }

	/// <summary>
	/// 0 fades in and out, 1 is the same with a flicker, 2 writes the text out character by
	/// character.
	/// </summary>
	[Property] public int Effect { get; set; }

	/// <summary>The colour the text settles at.</summary>
	[Property] public Color Color { get; set; }

	/// <summary>The colour a character is written in before it settles, for effect 2.</summary>
	[Property] public Color Color2 { get; set; }

	/// <summary>Seconds to fade in. Effect 2 spends this on each character in turn.</summary>
	[Property] public float FadeIn { get; set; }

	/// <summary>Seconds to fade out once the hold is over.</summary>
	[Property] public float FadeOut { get; set; }

	/// <summary>Seconds a character takes to settle from <see cref="Color2"/> to <see cref="Color"/>.</summary>
	[Property] public float FxTime { get; set; }

	/// <summary>Seconds the text stays up at full brightness.</summary>
	[Property] public float HoldTime { get; set; }

	/// <summary>When the fade out finishes, in seconds since the message was shown.</summary>
	public readonly float Duration => FadeOut + HoldTime + (Effect == 2 ? FadeIn * (Text?.Length ?? 0) : FadeIn);

	/// <summary>When the fade out begins, in seconds since the message was shown.</summary>
	internal readonly float FadeTime => Duration - FadeOut;
}
