namespace Source1;

[Hide]
public class ActivatorFilter : Component, IMapInput, IMapOutputs
{
	public enum Test { Always, Name, Class, MassGreater, All, Any }

	[Property] public Test Kind { get; set; }
	[Property] public string Pattern { get; set; }
	[Property] public float Mass { get; set; }
	[Property] public bool Negated { get; set; }
	[Property] public bool HasUnsupportedMember { get; set; }
	[Property] public List<ActivatorFilter> Filters { get; set; } = [];
	[Property] public Doo Pass { get; set; }
	[Property] public Doo Fail { get; set; }

	internal const string ClassTag = "source1_class_";
	bool _testing;

	public bool Accepts( GameObject actor ) => Evaluate( actor ) == true;

	bool? Evaluate( GameObject actor )
	{
		if ( _testing ) return null;
		_testing = true;
		try
		{
			return Matches( actor ) ^ Negated;
		}
		finally { _testing = false; }
	}

	protected virtual bool? Matches( GameObject actor )
	{
		switch ( Kind )
		{
			case Test.Always: return true;
			case Test.Name:
				return actor.IsValid() && (Pattern == "!player" ? actor.GetComponent<PlayerController>().IsValid() : actor.Name.WildcardMatch( Pattern ));
			case Test.Class:
				if ( !actor.IsValid() ) return false;
				if ( actor.GetComponent<PlayerController>().IsValid() ) return "player".WildcardMatch( Pattern );
				if ( !Pattern.Contains( '*' ) && !Pattern.Contains( '?' ) ) return actor.Tags.Has( ClassTag + Pattern );
				foreach ( var tag in actor.Tags )
					if ( tag.StartsWith( ClassTag, System.StringComparison.Ordinal ) && tag[ClassTag.Length..].WildcardMatch( Pattern ) ) return true;
				return false;
			case Test.MassGreater:
				return actor.IsValid() && actor.GetComponent<Rigidbody>() is { } body && body.Mass > Mass;
			case Test.All:
			case Test.Any:
				var all = Kind == Test.All;
				bool? result = HasUnsupportedMember ? null : all;
				foreach ( var filter in Filters )
				{
					if ( !filter.IsValid() ) continue;
					var pass = filter.Evaluate( actor );
					if ( pass == !all ) return !all;
					if ( pass is null ) result = null;
				}
				return result;
			default: return null;
		}
	}

	public void TestActivator( GameObject actor ) => RunDoo( Accepts( actor ) ? Pass : Fail, c => c.SetArgument( "user", actor ) );

	Doo.Block IMapInput.BuildInput( string input, string parameter, bool hasUser ) =>
		input.Equals( "TestActivator", System.StringComparison.OrdinalIgnoreCase ) ? this.Call( nameof( TestActivator ), MapInputs.User( hasUser ) ) : null;

	void IMapOutputs.Wire( System.Func<string, bool, Doo> output )
	{
		Pass = output( "OnPass", true );
		Fail = output( "OnFail", true );
	}
}
