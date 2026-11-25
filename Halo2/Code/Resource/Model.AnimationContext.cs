using System;
using Sandbox.Animation;

namespace Halo2;

/// <summary>References use the graph slot in the low word (-1 for this model) and the clip index in the high word.</summary>
public sealed record Halo2AnimationContext(
	IReadOnlyDictionary<string, int> Actions, IReadOnlyDictionary<string, int> Overlays,
	IReadOnlyDictionary<(string Damage, int Direction, int Region), int> Damage,
	IReadOnlyList<Halo2Transition> Transitions, IReadOnlyList<(string Marker, string AttachTo)> Ik )
{
	static readonly int[] NextDirection = [1, 3, 0, 2];
	static readonly int[] NextRegion = [2, 0, 1, 1, 3, 0, 5, 1, 7, 0, 9];

	public bool TryGetDamage( string kind, int direction, int region, out int reference )
	{
		reference = -1;
		if ( (uint)direction >= NextDirection.Length || (uint)region >= NextRegion.Length ) return false;
		for ( int d = 0; d < NextDirection.Length; d++, direction = NextDirection[direction] )
		{
			if ( !Damage.Keys.Any( k => k.Damage == kind && k.Direction == direction ) ) continue;
			int checkedRegions = 0;
			while ( (checkedRegions & (1 << region)) == 0 )
			{
				if ( Damage.TryGetValue( (kind, direction, region), out reference ) ) return true;
				checkedRegions |= 1 << region;
				region = NextRegion[region];
			}
			break;
		}
		reference = -1;
		return false;
	}
}

public sealed record Halo2Transition( string From, string To, string SourceState, string Mode, string State, int Keyframe, int Clip );
public sealed record Halo2AnimationCue( int Frame, string Kind, int Index, string TagPath, string Marker, int Flags );
public sealed record Halo2ClipInfo( string Name, int Frames, int LoopFrame, float Weight, int PreviousVariant,
	int NextVariant, int PlaybackFlags, IReadOnlyList<Halo2AnimationCue> Cues );

[Sandbox.ModelEditor.GameData( "first_person_animation" )]
public sealed class FirstPersonAnimation
{
	public Dictionary<string, Halo2ClipInfo> Actions { get; set; }
	public Halo2ClipInfo Movement { get; set; }
	public int AmmunitionFrames { get; set; }
	public Dictionary<string, CuePlayback> Sounds { get; set; }
}

public partial class Halo2ModelLoader
{
	public static Halo2ModelLoader ForModel( Model model ) => model is null ? null :
		Sandbox.Mounting.Directory.Get( Halo2Mount.MountIdent )?.GetByPath( model.Name ) as Halo2ModelLoader;

	readonly Dictionary<(string, string, string), Halo2AnimationContext> contexts = new();
	readonly Dictionary<int, Halo2ClipInfo> clipInfo = new();

	public IEnumerable<(string Mode, string WeaponClass, string WeaponType)> AnimationContexts()
	{
		if ( graph is null ) yield break;
		foreach ( int mode in Rows( GraphHeader + 52, 20 ) )
		foreach ( int category in Rows( mode + 4, 20 ) )
		foreach ( int weapon in Rows( category + 4, 52 ) )
			yield return (Label( mode ), Label( category ), Label( weapon ));
	}

	public Halo2AnimationContext GetAnimationContext( string modeName, string weaponClass, string weaponType )
	{
		if ( graph is null ) return null;
		if ( skeleton is null ) BuildSkeleton( GraphHeader );
		if ( skeleton is null ) return null;
		var key = (modeName, weaponClass, weaponType);
		if ( contexts.TryGetValue( key, out var cached ) ) return cached;
		var actions = new Dictionary<string, int>();
		var overlays = new Dictionary<string, int>();
		var damage = new Dictionary<(string, int, int), int>();
		var transitions = new Dictionary<(string, string), Halo2Transition>();
		var ik = new List<(string, string)>();
		bool found = false;

		foreach ( int mode in Matches( GraphHeader + 52, 20, modeName ) )
		foreach ( int category in Matches( mode + 4, 20, weaponClass ) )
		foreach ( int weapon in Matches( category + 4, 52, weaponType ) )
		{
			// A weapon can use its class's "any" row when the rig has no weapon-specific stance.
			found |= Label( mode ) == modeName && Label( category ) == weaponClass;
			ReadBindings( weapon + 4, actions );
			ReadBindings( weapon + 12, overlays );
			foreach ( int d in Rows( weapon + 20, 12 ) )
			foreach ( var (direction, row) in Rows( d + 4, 8 ).Index() )
			foreach ( var (region, binding) in Rows( row, 4 ).Index() )
			{
				int reference = mapFile.ReadInt32( binding );
				if ( HasClip( reference ) ) damage.TryAdd( (Label( d ), direction, region), reference );
			}
			foreach ( int from in Rows( weapon + 28, 20 ) )
			foreach ( int to in Rows( from + 12, 20 ) )
			{
				int reference = mapFile.ReadInt32( to + 16 );
				if ( HasClip( reference ) )
					transitions.TryAdd( (Label( from ), Label( to )), new( Label( from ), Label( to ), Label( from + 4 ), Label( to + 4 ),
						Label( to + 8 ), mapFile.ReadSpan( to + 12, 1 )[0], reference ) );
			}
			foreach ( int row in Rows( category + 12, 8 ).Concat( Rows( mode + 12, 8 ) ) )
			{
				var pair = (Label( row ), Label( row + 4 ));
				if ( !ik.Contains( pair ) ) ik.Add( pair );
			}
		}
		if ( !found ) return null;
		return contexts[key] = new( actions, overlays, damage, transitions.Values.ToArray(), ik );

		IEnumerable<int> Matches( int block, int stride, string requested ) =>
			Rows( block, stride ).Where( p => Label( p ) == requested || Label( p ) == "any" )
				.OrderByDescending( p => Label( p ) == requested );

		void ReadBindings( int block, Dictionary<string, int> destination )
		{
			foreach ( int row in Rows( block, 8 ) )
			{
				int reference = mapFile.ReadInt32( row + 4 );
				if ( HasClip( reference ) && Label( row ) is { Length: > 0 } label )
					destination.TryAdd( label, reference );
			}
		}
	}

	public Clip LoadAnimationClip( int reference )
	{
		if ( graph is null ) return null;
		if ( skeleton is null ) BuildSkeleton( GraphHeader );
		return HasClip( reference ) ? GetClip( reference ) : null;
	}

	static readonly string[] FrameEvents = ["primary_keyframe", "secondary_keyframe", "left_foot", "right_foot",
		"allow_interruption", "transition_a", "transition_b", "transition_c", "transition_d", "both_feet_shuffle", "body_impact"];

	public Halo2ClipInfo GetClipInfo( int reference )
	{
		if ( clipInfo.TryGetValue( reference, out var info ) ) return info;
		if ( graph is null ) return null;
		if ( skeleton is null ) BuildSkeleton( GraphHeader );
		if ( !HasClip( reference ) ) return null;
		int entry = Entry( reference ), header = sources[(short)reference].Header;
		var cues = new List<Halo2AnimationCue>();
		foreach ( int row in Rows( entry + 64, 4 ) )
		{
			int packed = mapFile.ReadInt32( row ), kind = (short)packed;
			cues.Add( new( packed >> 16, (uint)kind < FrameEvents.Length ? FrameEvents[kind] : "frame_event", kind, null, null, 0 ) );
		}
		ReadCues( "sound", entry + 72, 8, header + 20 );
		ReadCues( "effect", entry + 80, 4, header + 28 );
		return clipInfo[reference] = new( Label( entry ), (ushort)mapFile.ReadInt32( entry + 20 ),
			(short)mapFile.ReadInt32( entry + 32 ), Float( entry + 28 ), Sibling( 34 ), Sibling( 36 ),
			(ushort)mapFile.ReadInt32( entry + 24 ), cues.OrderBy( c => c.Frame ).ToArray() );

		int Sibling( int offset )
		{
			int index = (short)mapFile.ReadInt32( entry + offset );
			return index < 0 ? -1 : (index << 16) | (ushort)reference;
		}

		void ReadCues( string kind, int block, int stride, int assets )
		{
			int count = mapFile.ReadInt32( assets ), table = mapFile.VaToOffset( mapFile.ReadInt32( assets + 4 ) );
			foreach ( int row in Rows( block, stride ) )
			{
				int packed = mapFile.ReadInt32( row ), index = (short)packed;
				if ( (uint)index >= count ) continue;
				int asset = table + 12 * index;
				var tag = mapFile.GetTag( mapFile.ReadInt32( asset + 4 ) & 0xffff );
				cues.Add( new( packed >> 16, kind, index, tag?.TagPath, stride == 8 ? Label( row + 4 ) : null,
					(ushort)mapFile.ReadInt32( asset + 8 ) ) );
			}
		}
	}
}
