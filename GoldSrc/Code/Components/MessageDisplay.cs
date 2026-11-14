using Sandbox;
using Sandbox.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoldSrc;

/// <summary>
/// GoldSrc's on-screen text for the Half-Life opening titles and credits.
/// env_message pushes titles.txt entries in and they're drawn until they have run their
/// fade in, hold and fade out.
/// </summary>
public sealed class MessageDisplay : GameObjectSystem<MessageDisplay>
{
	public MessageDisplay( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 0, Draw, "GoldSrc.MessageDisplay" );
	}

	private const int MaxMessages = 16;

	/// <summary>
	/// s&amp;box's caption styling - the same font and weight the built-in subtitle overlay uses, so
	/// a mounted game's text reads like the rest of the platform. Messages sit bigger than the
	/// caption's own size because they're titles rather than dialogue.
	/// </summary>
	private const string FontName = "Inter";
	private const int FontWeight = 600;
	private const float CaptionFontSize = 24f;

	/// <summary>
	/// How hard the text is separated from what's behind it, sized against the caption.
	/// </summary>
	private const float OutlineSize = 3f;
	private const float ShadowSize = 6f;
	private const float ShadowOffset = 2f;
	private const float EffectAlpha = 0.9f;

	/// <summary>Font size, and the screen height it's sized against.</summary>
	private const float ReferenceHeight = 1080f;
	private const float ReferenceFontSize = 32f;

	/// <summary>Line advance as a multiple of the font size.</summary>
	private const float LineSpacing = 1.2f;

	/// <summary>titles.txt $effect values.</summary>
	private const int Flicker = 1;
	private const int WriteOut = 2;

	/// <summary>Effect 1's odds of flaring up on any given frame, from MessageScanStart.</summary>
	private const float FlickerChance = 0.1f;

	/// <summary>The position value that means "centre this axis".</summary>
	private const float Centred = -1f;

	/// <summary>
	/// The caption style, without effects - those pad the texture text is drawn into, so they'd
	/// show up as extra width when measuring. Drawing adds them.
	/// </summary>
	private static TextRendering.Scope TextStyle( float fontSize ) =>
		new( null, Color.White, fontSize, FontName, FontWeight );

	private sealed class Shown
	{
		public TitleMessage Message;
		public float StartTime;
		public Layout Layout;
	}

	private readonly List<Shown> _shown = [];

	/// <summary>
	/// Put a message on screen. Ignores one whose text is already up, and
	/// drops any other message sharing its position, so only one occupies a spot at a time.
	/// </summary>
	public void Show( TitleMessage message )
	{
		if ( string.IsNullOrEmpty( message.Text ) )
			return;

		if ( _shown.Any( x => x.Message.Text == message.Text ) )
			return;

		_shown.RemoveAll( x => x.Message.Position.AlmostEqual( message.Position ) );

		if ( _shown.Count >= MaxMessages )
			return;

		_shown.Add( new Shown { Message = message, StartTime = Time.Now } );
	}

	private void Draw()
	{
		_shown.RemoveAll( x => Time.Now - x.StartTime > x.Message.Duration );

		if ( _shown.Count == 0 )
			return;

		var camera = Scene.Camera;
		if ( !camera.IsValid() )
			return;

		var screen = Screen.Size;
		var fontSize = screen.y / ReferenceHeight * ReferenceFontSize;
		var hud = camera.Hud;

		foreach ( var shown in _shown )
		{
			if ( shown.Layout?.FontSize != fontSize )
				shown.Layout = new Layout( shown.Message.Text, fontSize );

			DrawMessage( hud, shown, Time.Now - shown.StartTime, screen );
		}
	}

	/// <summary>
	/// Effect 2 writes itself out a character at a time, so it's the
	/// only one that has to colour characters individually - the rest draw a line at a time.
	/// </summary>
	private static void DrawMessage( HudPainter hud, Shown shown, float time, Vector2 screen )
	{
		var message = shown.Message;
		var layout = shown.Layout;

		var fade = FadeBlend( message, time );
		var flare = message.Effect == Flicker && Game.Random.Float() < FlickerChance;
		var charTime = 0f;

		var scope = TextStyle( layout.FontSize );
		var y = Align( message.Position.y, layout.Height, layout.Height, screen.y );

		foreach ( var line in layout.Lines )
		{
			var x = Align( message.Position.x, line.Width, layout.Width, screen.x );

			if ( message.Effect != WriteOut )
			{
				Draw( hud, scope, line.Text, new Vector2( x, y ), CharColor( message, time, ref charTime, fade, flare ) );
			}
			else
			{
				for ( var i = 0; i < line.Chars.Length; i++ )
				{
					var color = CharColor( message, time, ref charTime, fade, flare );
					Draw( hud, scope, line.Chars[i], new Vector2( x + line.Offsets[i], y ), color );
				}
			}

			y += layout.LineHeight;
		}
	}

	/// <summary>
	/// Messages land over skyboxes, lit walls and anything else the map puts behind them, so the
	/// text is separated from it with a tight outline and a soft shadow. Both fade with the text
	/// rather than outliving it.
	/// </summary>
	private static void Draw( HudPainter hud, TextRendering.Scope scope, string text, Vector2 position, Color color )
	{
		if ( color.a <= 0f )
			return;

		var scale = scope.FontSize / CaptionFontSize;
		var shade = Color.Black.WithAlpha( EffectAlpha * color.a );

		scope.Text = text;
		scope.TextColor = color;

		scope.ShadowUnder = new TextRendering.Shadow
		{
			Enabled = true,
			Color = shade,
			Size = ShadowSize * scale,
			Offset = new Vector2( 0f, ShadowOffset * scale )
		};

		scope.OutlineUnder = new TextRendering.Outline
		{
			Enabled = true,
			Color = shade,
			Size = OutlineSize * scale
		};

		hud.DrawText( scope, position );
	}

	/// <summary>
	/// A message is placed by its own size, but a block
	/// aligned to the right or bottom edge lines up on the widest line rather than each line.
	/// </summary>
	private static float Align( float fraction, float size, float blockSize, float screen )
	{
		var position = fraction switch
		{
			Centred => (screen - size) * 0.5f,
			< 0f => (1f + fraction) * screen - blockSize,
			_ => fraction * screen
		};

		return Math.Clamp( position, 0f, Math.Max( screen - size, 0f ) );
	}

	/// <summary>
	/// How far the message has faded, 0 while it's fully up and 1
	/// when it's gone. Effect 2 has no fade in of its own - it writes out instead.
	/// </summary>
	private static float FadeBlend( in TitleMessage message, float time )
	{
		if ( message.Effect != WriteOut && message.FadeIn > 0f && time < message.FadeIn )
			return (message.FadeIn - time) / message.FadeIn;

		if ( time <= message.FadeTime )
			return 0f;

		return message.FadeOut > 0f ? (time - message.FadeTime) / message.FadeOut : 1f;
	}

	/// <summary>
	/// Calculate the character color with the fade expressed as alpha.
	/// </summary>
	private static Color CharColor( in TitleMessage message, float time, ref float charTime, float fade, bool flare )
	{
		if ( message.Effect != WriteOut )
			return (flare ? message.Color + message.Color2 : message.Color).WithAlpha( 1f - fade );

		// Each character waits a further fadein before it appears, so the text writes itself out.
		charTime += message.FadeIn;

		if ( charTime > time )
			return Color.Transparent;

		if ( fade > 0f )
			return message.Color.WithAlpha( 1f - fade );

		var written = time - charTime;

		return written > message.FxTime
			? message.Color
			: Color.Lerp( message.Color2, message.Color, written / message.FxTime );
	}

	/// <summary>
	/// Where every character of a message sits, measured once per font size rather than per frame.
	/// </summary>
	private sealed class Layout
	{
		public readonly float FontSize;
		public readonly Line[] Lines;

		/// <summary>The widest line.</summary>
		public readonly float Width;

		public float LineHeight => FontSize * LineSpacing;
		public float Height => Lines.Length * LineHeight;

		public Layout( string text, float fontSize )
		{
			FontSize = fontSize;

			var scope = TextStyle( fontSize );

			Lines = text.Replace( "\r", "" ).Split( '\n' ).Select( line => new Line( line, scope ) ).ToArray();
			Width = Lines.Max( x => x.Width );
		}
	}

	private sealed class Line
	{
		public readonly string Text;

		/// <summary>The characters, kept as strings because that is what gets drawn.</summary>
		public readonly string[] Chars;

		/// <summary>Where each character starts, with the line's full width on the end.</summary>
		public readonly float[] Offsets;

		public float Width => Offsets[^1];

		public Line( string text, TextRendering.Scope scope )
		{
			Text = text;
			Chars = new string[text.Length];
			Offsets = new float[text.Length + 1];

			// A character is placed by the width of everything before it rather than by adding up
			// characters measured alone, so the line keeps the kerning it'd have drawn in one go.
			for ( var i = 0; i < text.Length; i++ )
			{
				Chars[i] = text[i].ToString();

				scope.Text = text[..(i + 1)];
				Offsets[i + 1] = Graphics.MeasureText( new Rect( 0f ), scope, TextFlag.LeftTop ).Width;
			}
		}
	}
}
