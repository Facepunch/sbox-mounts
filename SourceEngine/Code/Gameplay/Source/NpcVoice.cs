using System.Globalization;
using System;
using Sandbox.Npcs;

namespace Source1;

public enum VoiceLine { Alert, AlertNpc, Pain, Death, Kill, Reload, Cover, Grenade, Idle, Greet, Fear, Follow, Unfollow, Announce, Assault, Kick, BatonOn, BatonOff }

/// <summary>
/// What a mounted NPC says in a situation. HL2's soldiers and metrocops speak sentence groups (!NAME) as their m_Sentences.Speak
/// calls do, and player allies speak response concepts (TLK_*) chosen by the talker rules, as CAI_PlayerAlly does. Other classes
/// emit the sound scripts their NPC code names ({0} is the class's prefix, such as NPC_Strider, numbered variants included);
/// Counter-Strike's bots speak their chatter, or call on the radio where it has none. A line the mount lacks stays silent.
/// </summary>
public static class NpcVoice
{
	/// <summary>The bot chatter phrases (BotChatter.db) a line is said with; a mount without them speaks its sound scripts above.</summary>
	static readonly Dictionary<VoiceLine, string[]> BotChatter = new()
	{
		[VoiceLine.Alert] = ["EnemySpotted"],
		[VoiceLine.AlertNpc] = ["EnemySpotted"],
		[VoiceLine.Pain] = ["Help"],
		[VoiceLine.Death] = ["Pain"],
		[VoiceLine.Kill] = ["KilledMyEnemy"],
		[VoiceLine.Reload] = ["CoverMe"],
		[VoiceLine.Cover] = ["PinnedDown"],
		[VoiceLine.Idle] = ["Clear", "RequestReport", "ReportingIn"],
	};

	static readonly Dictionary<VoiceLine, string[]> PlayerAlly = new()
	{
		[VoiceLine.Alert] = ["TLK_STARTCOMBAT"],
		[VoiceLine.AlertNpc] = ["TLK_STARTCOMBAT"],
		[VoiceLine.Pain] = ["TLK_WOUND"],
		[VoiceLine.Kill] = ["TLK_ENEMY_DEAD"],
		[VoiceLine.Reload] = ["TLK_HIDEANDRELOAD"],
		[VoiceLine.Cover] = ["TLK_DANGER"],
		[VoiceLine.Idle] = ["TLK_IDLE", "TLK_QUESTION", "TLK_STARE"],
		[VoiceLine.Greet] = ["TLK_HELLO"],
		[VoiceLine.Fear] = ["TLK_DANGER"],
		[VoiceLine.Follow] = ["TLK_STARTFOLLOW"],
		[VoiceLine.Unfollow] = ["TLK_STOPFOLLOW"],
	};

	/// <summary>The lines of the HL2 NPC classes whose code speaks sentences or response concepts.</summary>
	static readonly Dictionary<string, Dictionary<VoiceLine, string[]>> Classes = new()
	{
		["npc_combine_s"] = new()
		{
			[VoiceLine.Alert] = ["!COMBINE_ALERT"],
			[VoiceLine.AlertNpc] = ["!COMBINE_MONST"],
			[VoiceLine.Pain] = ["!COMBINE_PAIN"],
			[VoiceLine.Death] = ["!COMBINE_DIE"],
			[VoiceLine.Kill] = ["!COMBINE_KILL_MONST"],
			[VoiceLine.Cover] = ["!COMBINE_COVER"],
			[VoiceLine.Grenade] = ["!COMBINE_THROW_GRENADE"],
			[VoiceLine.Announce] = ["!COMBINE_ANNOUNCE"],
			[VoiceLine.Assault] = ["!COMBINE_ASSAULT"],
			[VoiceLine.Kick] = ["!COMBINE_KICK"],
			[VoiceLine.Idle] = ["!COMBINE_IDLE", "!COMBINE_CHECK", "!COMBINE_QUEST"],
		},
		["npc_metropolice"] = new()
		{
			[VoiceLine.Alert] = ["!METROPOLICE_MONST_PLAYER"],
			[VoiceLine.AlertNpc] = ["!METROPOLICE_MONST"],
			[VoiceLine.Pain] = ["!METROPOLICE_PAIN"],
			[VoiceLine.Death] = ["!METROPOLICE_DIE"],
			[VoiceLine.Kill] = ["!METROPOLICE_KILL_MONST"],
			[VoiceLine.Reload] = ["!METROPOLICE_COVER_LOW_AMMO"],
			[VoiceLine.Cover] = ["!METROPOLICE_COVER_HEAVY_DAMAGE"],
			[VoiceLine.BatonOn] = ["!METROPOLICE_ACTIVATE_BATON"],
			[VoiceLine.BatonOff] = ["!METROPOLICE_DEACTIVATE_BATON"],
			[VoiceLine.Idle] = ["!METROPOLICE_IDLE"],
		},
		["npc_citizen"] = new( PlayerAlly ) { [VoiceLine.Death] = ["NPC_Citizen.Die"] },
		["npc_alyx"] = PlayerAlly,
		["npc_barney"] = PlayerAlly,
		["npc_monk"] = PlayerAlly,
		["npc_vortigaunt"] = PlayerAlly,
	};

	/// <summary>The HL2 NPC class each model is spawned as, for the class's lines and for rules that name an enemy class.</summary>
	static readonly Dictionary<string, string> Models = new( StringComparer.OrdinalIgnoreCase )
	{
		["models/combine_soldier.mdl"] = "npc_combine_s",
		["models/combine_soldier_prisonguard.mdl"] = "npc_combine_s",
		["models/combine_super_soldier.mdl"] = "npc_combine_s",
		["models/police.mdl"] = "npc_metropolice",
		["models/odessa.mdl"] = "npc_citizen",
		["models/alyx.mdl"] = "npc_alyx",
		["models/barney.mdl"] = "npc_barney",
		["models/monk.mdl"] = "npc_monk",
		["models/vortigaunt.mdl"] = "npc_vortigaunt",
		["models/headcrabclassic.mdl"] = "npc_headcrab",
		["models/headcrab.mdl"] = "npc_headcrab_fast",
		["models/headcrabblack.mdl"] = "npc_headcrab_black",
		["models/zombie/classic.mdl"] = "npc_zombie",
		["models/zombie/classic_torso.mdl"] = "npc_zombie_torso",
		["models/zombie/fast.mdl"] = "npc_fastzombie",
		["models/zombie/poison.mdl"] = "npc_poisonzombie",
		["models/zombie/zombie_soldier.mdl"] = "npc_zombine",
		["models/combine_scanner.mdl"] = "npc_cscanner",
		["models/combine_strider.mdl"] = "npc_strider",
		["models/manhack.mdl"] = "npc_manhack",
	};

	/// <summary>
	/// Speech priority, and how long the same line is held back for everyone within earshot so a squad does not repeat it.
	/// Pain is always said but yields to every call-out, as Source speaks it at SENTENCE_PRIORITY_INVALID.
	/// </summary>
	static readonly Dictionary<VoiceLine, (int Priority, float Cooldown)> Rules = new()
	{
		[VoiceLine.Pain] = (1000, 1.5f),
		[VoiceLine.Follow] = (2200, 1),
		[VoiceLine.Unfollow] = (2200, 1),
		[VoiceLine.Fear] = (2000, 4),
		[VoiceLine.Announce] = (1700, 4),
		[VoiceLine.Assault] = (1500, 4),
		[VoiceLine.Grenade] = (1700, 4),
		[VoiceLine.Alert] = (1600, 8),
		[VoiceLine.AlertNpc] = (1600, 8),
		[VoiceLine.Kill] = (1400, 6),
		[VoiceLine.BatonOn] = (1300, 4),
		[VoiceLine.BatonOff] = (1300, 4),
		[VoiceLine.Cover] = (1200, 8),
		[VoiceLine.Reload] = (1200, 6),
		[VoiceLine.Greet] = (1100, 10),
		[VoiceLine.Kick] = (1100, 0.5f),
		[VoiceLine.Idle] = (500, 20),
	};

	/// <summary>The sound script each class's CAI_SentenceBase::Init reads its sentence volume, pitch and sound level from.</summary>
	static readonly Dictionary<string, string> SentenceParameters = new()
	{
		["npc_combine_s"] = "NPC_Combine.SentenceParameters",
		["npc_metropolice"] = "NPC_MetroPolice.SentenceParameters",
	};

	/// <summary>A line ready to speak: waves (mount paths) played back to back, and the path of the sound script whose sound level they carry at.</summary>
	sealed record Utterance( string Name, string[] Waves, float[] Pitch, float[] Volume, string Level );

	static readonly Dictionary<(string, string, VoiceLine), string[]> Resolved = new();

	/// <summary>The sound scripts the NPC's mount has for a line.</summary>
	static string[] Lines( string mount, string prefix, VoiceLine line )
	{
		if ( Resolved.TryGetValue( (mount, prefix, line), out var names ) )
			return names;

		var patterns = (Sandbox.Mounting.Directory.Get( mount ) as SourceGameMount)?.VoicePatterns( line ) ?? [];
		var events = Sounds.EventNames( mount );
		names = patterns.Where( x => !x.Contains( "{0}" ) || !string.IsNullOrEmpty( prefix ) )
			.Select( x => string.Format( x, prefix ).ToLowerInvariant() )
			.SelectMany( x => events.Where( e => e.StartsWith( x, StringComparison.Ordinal ) && e[x.Length..].TrimStart( '_' ).All( char.IsDigit ) ) )
			.Distinct().ToArray();
		if ( events.Count > 0 )
			Resolved[(mount, prefix, line)] = names;
		return names;
	}

	/// <summary>Says a line through the NPC's speech layer, on the host. Death is not spoken: see <see cref="SayDeath"/>.</summary>
	public static bool Say( BaseNpc npc, string prefix, VoiceLine line, GameObject lookAt = null )
	{
		if ( !Networking.IsHost || !npc.IsValid() || !npc.Speech.IsValid() || npc.Health < 1 )
			return false;

		// AnnounceEnemyType calls out players and other enemies differently.
		if ( line == VoiceLine.Alert && lookAt.IsValid() && !Npcs.IsPlayer( lookAt ) )
			line = VoiceLine.AlertNpc;

		if ( !Rules.TryGetValue( line, out var rule ) || rule.Priority < 1000 && !npc.Speech.CanSpeak )
			return false;

		if ( Pick( npc, prefix, line, lookAt ) is not { } utterance )
			return false;

		var tag = line.ToString().ToLowerInvariant();
		if ( !SentencePlayer.TrySay( npc, utterance.Name, utterance.Waves, utterance.Pitch, utterance.Volume, utterance.Level, lookAt, tag, rule.Priority ) )
			return false;

		npc.Scene.GetSystem<NpcSystem>().SetTagCooldown( npc.WorldPosition, tag, rule.Cooldown, priority: rule.Priority );
		return true;
	}

	/// <summary>Cuts the dying NPC's line and speaks its death line where it fell, on the host; it finishes after the NPC is gone.</summary>
	public static void SayDeath( BaseNpc npc, string prefix )
	{
		if ( !Networking.IsHost || !npc.IsValid() || Pick( npc, prefix, VoiceLine.Death, null ) is not { } utterance )
			return;

		var position = npc.WorldPosition + SentencePlayer.Mouth( npc );
		SentencePlayer.SayDeath( npc.GameObject, position, utterance.Waves, utterance.Pitch, utterance.Volume, utterance.Level );
	}

	/// <summary>The line the NPC's class, or else its sound-script prefix, has for the situation.</summary>
	static Utterance Pick( BaseNpc npc, string prefix, VoiceLine line, GameObject lookAt )
	{
		var mount = MountContent.Of( npc.GameObject );
		var model = ModelOf( npc.GameObject );
		var npcClass = ClassOf( model );
		foreach ( var name in Classes.GetValueOrDefault( npcClass ?? "" )?.GetValueOrDefault( line ) ?? [] )
		{
			var utterance = name.StartsWith( '!' ) ? Sentence( npc, mount, name[1..], Sounds.Sound( SentenceParameters.GetValueOrDefault( npcClass ?? "" ), mount ) )
				: name.StartsWith( "TLK_", StringComparison.Ordinal ) ? Concept( npc, mount, model, npcClass, name, lookAt )
				: SoundScript( mount, name, model );
			if ( utterance is not null )
				return utterance;
		}

		if ( Chatter( npc, mount, line ) is { } chatter )
			return chatter;

		var names = Lines( mount, prefix, line );
		return names.Length > 0 ? SoundScript( mount, Game.Random.FromArray( names ), model ) : null;
	}

	/// <summary>
	/// A wave of one of the line's chatter phrases, picked on the host. Waves tied to a place are left out (the sandbox has no
	/// places); waves tied to an enemy count are said of as many enemies as the NPC sees, as CSBot reports them.
	/// </summary>
	static Utterance Chatter( BaseNpc npc, string mount, VoiceLine line )
	{
		var phrases = Speech.Chatter( mount );
		if ( phrases.Count == 0 || !BotChatter.TryGetValue( line, out var names ) )
			return null;

		var seen = npc.Senses.IsValid() ? npc.Senses.VisibleHostiles.Count : 0;
		var count = seen > 3 ? "Many" : Math.Max( seen, 1 ).ToString( CultureInfo.InvariantCulture );
		bool Fits( ChatterLine x ) => (x.Count is null || x.Count.Equals( count, StringComparison.OrdinalIgnoreCase ))
			&& (x.Place is null || x.Place.Equals( "ANY", StringComparison.OrdinalIgnoreCase ) || x.Place.Equals( "UNDEFINED", StringComparison.OrdinalIgnoreCase ));

		var choices = names.Select( x => (Name: x, Lines: phrases.GetValueOrDefault( x )?.Lines.Where( Fits ).ToArray() ?? []) )
			.Where( x => x.Lines.Length > 0 ).ToArray();
		if ( choices.Length == 0 )
			return null;

		var (name, lines) = Game.Random.FromArray( choices );
		return new Utterance( name, [$"mount://{mount}/{Game.Random.FromArray( lines ).Wave}.vsnd"], [1], [1], null );
	}

	static bool IsFemale( string mount, string model ) => (Sandbox.Mounting.Directory.Get( mount ) as SourceGameMount)?.IsFemaleActor( model ) == true;

	/// <summary>One of the sound script's waves, picked on the host so every client speaks the same one.</summary>
	static Utterance SoundScript( string mount, string name, string model )
	{
		var sound = Sounds.Sound( name, mount, IsFemale( mount, model ) );
		if ( sound is null )
			return null;

		string wave;
		if ( Application.IsHeadless )
		{
			var loader = Sandbox.Mounting.Directory.Get( mount )?.GetByPath( sound.ResourcePath ) as SurfaceSoundLoader;
			if ( loader?.Waves is not { Count: > 0 } waves ) return null;
			wave = $"mount://{mount}/{waves[Game.Random.Int( 0, waves.Count - 1 )]}.vsnd";
		}
		else
		{
			var files = sound.Sounds.Where( x => x.IsValid() ).ToArray();
			if ( files.Length == 0 ) return null;
			wave = Game.Random.FromArray( files ).ResourcePath;
		}
		return new Utterance( name, [wave], [sound.Pitch.GetValue()], [sound.Volume.GetValue()], sound.ResourcePath );
	}

	/// <summary>
	/// One alternative of a sentence group; a virtual word picks one of its waves, the same one for the same speaker. The words
	/// take the speaker's sentence volume and sound level, and a voice pitch fixed per speaker as Init picks it once per NPC.
	/// </summary>
	static Utterance Sentence( BaseNpc npc, string mount, string group, SoundEvent voice )
	{
		if ( !Speech.Sentences( mount ).TryGetValue( group, out var alternatives ) || alternatives.Count == 0 )
			return null;

		var words = alternatives[Game.Random.Int( 0, alternatives.Count - 1 )];
		var speaker = npc.GameObject.Id.GetHashCode() & int.MaxValue;
		var waves = words.Select( x => $"mount://{mount}/{(x.Speaker ? x.Waves[speaker % x.Waves.Length] : Game.Random.FromArray( x.Waves ))}.vsnd" ).ToArray();
		var pitch = voice is null ? 1 : voice.Pitch.Min + (voice.Pitch.Max - voice.Pitch.Min) * (speaker % 101) / 100f;
		var volume = voice?.Volume.GetValue() ?? 1;
		return new Utterance( group, waves, words.Select( x => x.Pitch * pitch ).ToArray(), words.Select( x => x.Volume * volume ).ToArray(), voice?.ResourcePath );
	}

	/// <summary>
	/// A response to a concept, as the talker picks it: of the rules whose criteria all hold for the speaker, one that tests the
	/// most, then a line by weight. Criteria the sandbox has no state for fail, which leaves out map and story rules.
	/// </summary>
	static Utterance Concept( BaseNpc npc, string mount, string model, string npcClass, string concept, GameObject lookAt )
	{
		if ( !Speech.Rules( mount ).TryGetValue( concept, out var rules ) )
			return null;

		var facts = Facts( npc, npcClass, lookAt );
		var gender = IsFemale( mount, model ) ? "female" : "male";
		var candidates = rules.Where( x => x.Criteria.All( c => facts.TryGetValue( c.Key, out var value ) && Matches( c.Value, value ) ) );
		foreach ( var rule in candidates.OrderByDescending( x => x.Criteria.Count ).ThenBy( _ => Game.Random.Float() ) )
		{
			var responses = rule.Responses.Select( x => (Response: x, Utterance: Response( npc, mount, gender, x )) ).Where( x => x.Utterance is not null ).ToArray();
			if ( responses.Length == 0 )
				continue;

			var roll = Game.Random.Float( 0, responses.Sum( x => x.Response.Weight ) );
			foreach ( var (response, utterance) in responses )
			{
				roll -= response.Weight;
				if ( roll <= 0 )
					return utterance;
			}
			return responses[^1].Utterance;
		}

		return null;
	}

	static Utterance Response( BaseNpc npc, string mount, string gender, Response response )
	{
		if ( !string.IsNullOrEmpty( response.Sentence ) )
			return Sentence( npc, mount, response.Sentence, null );

		if ( !string.IsNullOrEmpty( response.Sound ) )
			return SoundScript( mount, response.Sound, ModelOf( npc.GameObject ) );

		if ( string.IsNullOrEmpty( response.Wave ) )
			return null;

		var wave = $"mount://{mount}/{response.Wave.Replace( "$gender", gender )}.vsnd";
		return Sandbox.Mounting.Directory.Get( mount )?.GetByPath( wave ) is not null ? new Utterance( response.Wave, [wave], [1], [1], null ) : null;
	}

	/// <summary>The criteria the sandbox can answer, named as CAI_BaseNPC and the player ally fill them in.</summary>
	static Dictionary<string, string> Facts( BaseNpc npc, string npcClass, GameObject lookAt )
	{
		var combat = npc.Senses.IsValid() && npc.Senses.FindBestTarget().IsValid();
		PlayerController player = null;
		var nearestDistance = 0f;
		foreach ( var candidate in npc.Scene.GetAllComponents<PlayerController>() )
		{
			var distance = candidate.WorldPosition.Distance( npc.WorldPosition );
			if ( player is null || distance.CompareTo( nearestDistance ) < 0 )
			{
				player = candidate;
				nearestDistance = distance;
			}
		}
		var facts = new Dictionary<string, string>
		{
			["classname"] = npcClass,
			["health"] = npc.Health.ToString( CultureInfo.InvariantCulture ),
			["npcstate"] = combat ? "3" : "1",
			["readiness"] = combat ? "agitated" : "relaxed",
			["timesincecombat"] = combat ? "0" : "999",
			["gordon_precriminal"] = "0",
			["hurt_by_fire"] = "0",
			["seeplayer"] = "1",
			["seenbyplayer"] = "1",
		};

		if ( player.IsValid() )
		{
			if ( Npcs.HealthOf( player.GameObject ) is { } health )
				facts["playerhealth"] = health.Health.ToString( CultureInfo.InvariantCulture );
			facts["distancetoplayer"] = player.WorldPosition.Distance( npc.WorldPosition ).ToString( CultureInfo.InvariantCulture );
		}

		if ( ClassOf( ModelOf( lookAt ) ) is { } enemy )
			facts["enemy"] = enemy;

		return facts;
	}

	/// <summary>A criterion value: a string, a number, or comparisons joined by commas such as "&gt;0,&lt;=.5", "!=1" or "!panic".</summary>
	static bool Matches( string criterion, string value )
	{
		foreach ( var part in criterion.Split( ',' ) )
		{
			var length = 0;
			while ( length < part.Length && part[length] is '<' or '>' or '=' or '!' )
				length++;
			var op = part[..length];
			var operand = part[op.Length..].Trim();
			var numeric = float.TryParse( operand, NumberStyles.Float, CultureInfo.InvariantCulture, out var b )
				& float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a );
			var order = numeric ? a.CompareTo( b ) : string.Compare( value, operand, StringComparison.OrdinalIgnoreCase );
			var holds = op switch
			{
				">" => order > 0,
				">=" => order >= 0,
				"<" => order < 0,
				"<=" => order <= 0,
				"!" or "!=" => order != 0,
				_ => order == 0,
			};
			if ( !holds )
				return false;
		}

		return true;
	}

	/// <summary>The original NPC class a character is spawned as, read from its body model, or null.</summary>
	internal static string ClassOf( GameObject go ) => ClassOf( ModelOf( go ) );

	/// <summary>Humans are citizens; the HL2 catalog spawns the rest by model.</summary>
	static string ClassOf( string model ) => model is null ? null
		: Models.GetValueOrDefault( model ) ?? (model.StartsWith( "models/humans/", StringComparison.OrdinalIgnoreCase ) ? "npc_citizen" : null);

	/// <summary>The body model, not a held weapon's.</summary>
	static string ModelOf( GameObject go ) => !go.IsValid() ? null
		: (go.GetComponent<BaseNpc>()?.Renderer?.GetComponent<MountedModel>( true ) ?? go.GetComponentInChildren<MountedModel>( true ))?.ModelPath;
}
