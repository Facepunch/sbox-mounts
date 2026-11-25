using System.Text.Json;
using Sandbox.Mounting;

namespace Halo2;

internal sealed record DamageBinding( string Kind, int Direction, int Region, int Clip );
internal sealed record GripTarget( string Marker, string AttachTo );

// The mount and the networked game may load in separate assemblies. A mounted text
// resource lets each animator read the context using types from its own assembly.
internal sealed record AnimationBindings( IReadOnlyDictionary<string, int> Actions, IReadOnlyDictionary<string, int> Overlays,
	DamageBinding[] Damage, IReadOnlyList<Halo2Transition> Transitions, GripTarget[] Ik, string GripModel,
	Dictionary<int, Halo2ClipInfo> Clips, Dictionary<string, CuePlayback> Sounds )
{
	public Halo2AnimationContext Context() => new( Actions, Overlays,
		Damage.ToDictionary( d => (d.Kind, d.Direction, d.Region), d => d.Clip ), Transitions,
		Ik.Select( i => (i.Marker, i.AttachTo) ).ToArray() );

	public static AnimationBindings Load( string path )
	{
		var load = Sandbox.Mounting.Directory.Get( Halo2Mount.MountIdent )?.GetByPath( path )?.GetOrCreate();
		return load is { IsCompletedSuccessfully: true } && load.Result is string json
			? JsonSerializer.Deserialize<AnimationBindings>( json ) : null;
	}
}

public partial class Halo2ModelLoader
{
	sealed class BindingsLoader( string[] names ) : ResourceLoader<Halo2Mount>
	{
		protected override object Load() => ((Halo2ModelLoader)Parent).ReadBindingsJson( names );
	}

	string ReadBindingsJson( string[] names )
	{
		var context = GetAnimationContext( names[0], names[1], names[2] );
		if ( context is null ) return null;
		var clips = new Dictionary<int, Halo2ClipInfo>();
		foreach ( int first in context.Actions.Values.Concat( context.Overlays.Values ).Concat( context.Damage.Values )
			.Concat( context.Transitions.Select( t => t.Clip ) ) )
		{
			int id = first;
			while ( !clips.ContainsKey( id ) && GetClipInfo( id ) is {} info )
			{
				clips.Add( id, info );
				id = info.NextVariant;
			}
		}
		var sounds = new Dictionary<string, CuePlayback>();
		foreach ( var path in clips.Values.SelectMany( c => c.Cues ).Where( c => c.Kind == "sound" && c.TagPath != null )
			.Select( c => c.TagPath ).Distinct() )
			if ( Host.GetByPath( Host.AssetPath( mapFile, path, "sound" ) ) is Halo2SoundLoader loader ) sounds[path] = loader.Cue;
		var damage = context.Damage.Select( d => new DamageBinding( d.Key.Damage, d.Key.Direction, d.Key.Region, d.Value ) ).ToArray();
		var ik = context.Ik.Select( i => new GripTarget( i.Marker, i.AttachTo ) ).ToArray();
		var grip = ik.Length > 0 ? GripWeaponModel( names[1], names[2] )?.Name : null;
		return JsonSerializer.Serialize( new AnimationBindings( context.Actions, context.Overlays, damage, context.Transitions, ik, grip, clips, sounds ) );
	}
}
