namespace Diesel;

public sealed partial class PaydayMount
{
	readonly Dictionary<string, PaydayAnimationInfo> animationInfo = new( StringComparer.Ordinal );
	readonly Dictionary<string, PaydayUnitAnimationInfo> unitAnimationInfo = new( StringComparer.Ordinal );

	/// <summary>Inspect a source path from AnimationPaths. Cues retain source names and times in seconds.</summary>
	public PaydayAnimationInfo GetAnimationInfo( string source )
	{
		if ( string.IsNullOrWhiteSpace( source ) ) throw new ArgumentException( "An animation source path is required.", nameof( source ) );
		if ( animationInfo.TryGetValue( source, out var cached ) ) return cached;
		var file = new AnimationFile( Database.Find( "animation", source )
			?? throw new InvalidDataException( $"Missing PAYDAY animation: {source}" ) );
		return animationInfo[source] = new( source, file.Duration, file.Tracks.Count,
			Array.AsReadOnly( file.Cues.Select( c => new PaydayAnimationCue( c.Time, c.Name ) ).ToArray() ) );
	}

	/// <summary>Inspect embedded controllers and named animation groups using a source unit path.</summary>
	public PaydayUnitAnimationInfo GetUnitAnimationInfo( string unit )
	{
		if ( string.IsNullOrWhiteSpace( unit ) ) throw new ArgumentException( "A unit source path is required.", nameof( unit ) );
		if ( unitAnimationInfo.TryGetValue( unit, out var cached ) ) return cached;
		var definition = UnitModel.Xml( Database.Find( "unit", unit )
			?? throw new InvalidDataException( $"Missing PAYDAY unit: {unit}" ) );
		var path = definition.Element( "object" )?.Attribute( "file" );
		if ( string.IsNullOrEmpty( path ) ) throw new InvalidDataException( $"PAYDAY unit {unit} has no object." );
		var obj = UnitModel.Xml( Database.Find( "object", path )
			?? throw new InvalidDataException( $"Missing PAYDAY object: {path}" ) );
		var file = new ModelFile( Database.Find( "model", path )
			?? throw new InvalidDataException( $"Missing PAYDAY model: {path}" ) );
		var tracks = file.Controllers( file.RigNodes.Select( n => n.Name ).ToHashSet() );
		var groups = new List<PaydayAnimationGroup>();
		foreach ( var group in obj.Element( "animations" )?.Elements( "animation_group" ) ?? [] )
		{
			var objects = group.Elements( "object" ).Select( o => o.Attribute( "name" ) ).Where( n => !string.IsNullOrEmpty( n ) ).ToArray();
			var selected = file.ControllerTracks( objects.Select( IdString.Hash ).ToHashSet(), includeDescendants: false );
			var range = Range( selected );
			groups.Add( new( group.Attribute( "name" ), Array.AsReadOnly( objects ), selected.Count, range.Start, range.End,
				group.Attribute( "enabled" ), group.Attribute( "speed" ), group.Attribute( "time" ) ) );
		}
		var all = Range( tracks );
		return unitAnimationInfo[unit] = new( unit, tracks.Count, all.Start, all.End, groups.AsReadOnly() );

		static (float Start, float End) Range( Dictionary<ulong, AnimationFile.Track> tracks )
		{
			var times = tracks.Values.SelectMany( t => t.PositionTimes.Concat( t.RotationTimes ) ).ToArray();
			return times.Length == 0 ? (0, 0) : (times.Min(), times.Max());
		}
	}
}
