namespace Diesel;

public sealed partial class PaydayEffect
{
	readonly Dictionary<XmlNode, float> emissionCredit = [];
	readonly HashSet<GameObject> children = [];
	HashSet<string> ancestors = [];
	Vector3 inheritedVelocity;

	void UpdateRespawns()
	{
		foreach ( var atom in started )
		{
			float atomAge = elapsed - N( atom, "start_time" );
			if ( N( atom, "lifetime", -1 ) >= 0 && atomAge >= N( atom, "lifetime" ) ) continue;
			foreach ( var op in atom.Element( "simulatorstack" )?.Elements( "teleporter" ) ?? [] )
			{
				bool frequency = op.Attribute( "trigger_teleport" ) == "frequency";
				if ( op.Attribute( "trigger_teleport" ) is not (null or "time" or "frequency") ) continue;
				if ( op.Attribute( "lifetime_input" ) is not (null or "constant") && !frequency ) continue;
				if ( op.Attribute( "positioner" ) is not ("box" or "circle") ) continue;
				int cap = Math.Clamp( (int)N( op, "teleport_cap", 512 ), 0, 512 );
				if ( frequency )
				{
					float rate = op.Attribute( "frequency_input" ) == "keys"
						? C( op, "frequency_keys", atomAge, default ).x : N( op, "frequency" );
					float credit = Math.Min( cap, emissionCredit.GetValueOrDefault( op ) + Math.Max( 0, rate ) * Time.Delta );
					cap = (int)credit; emissionCredit[op] = credit;
				}
				float threshold = frequency ? N( op, "min_age" ) : N( op, "particle_lifetime", 1 );
				if ( !frequency && threshold <= 0 ) continue;
				foreach ( var p in particles.Where( p => p.Atom == atom && p.Age >= threshold ).Take( cap ) )
				{
					Respawn( p, op );
					if ( frequency ) emissionCredit[op] -= 1;
					if ( op.Attribute( "teleport_event" ) == "true" ) SpawnChildren( p, "teleport", op );
				}
			}
		}
	}
	void Respawn( Particle p, XmlNode op )
	{
		Vector3 local, normal;
		if ( op.Attribute( "positioner" ) == "circle" )
		{
			var circle = op.Element( "circle" );
			float angle = Game.Random.Float( 0, MathF.PI * 2 );
			float radius = N( circle, "radius" ) * (circle?.Attribute( "on_edge" ) == "true" ? 1 : MathF.Sqrt( Game.Random.Float() ));
			local = new Vector3( MathF.Cos( angle ) * radius, MathF.Sin( angle ) * radius, 0 );
			normal = Vector3.Up;
		}
		else
		{
			var box = op.Element( "box" );
			local = RV( box );
			normal = Vector3.Lerp( V( box, "min_normal", Vector3.Up ), V( box, "max_normal", Vector3.Up ),
				new Vector3( Game.Random.Float(), Game.Random.Float(), Game.Random.Float() ) ).Normal;
		}
		float variation = N( op, "normal_variation" );
		normal = (normal + Vector3.Random * variation).Normal;
		p.Position = WorldTransform.PointToWorld( Coordinates.Position( local ) );
		p.Velocity = WorldRotation * Coordinates.Position( normal * R( op.Element( "normal" ), "min_velocity", "max_velocity" ) )
			+ inheritedVelocity * N( p.Atom, "inherit_velocity_multiplier", 1 );
		p.Age = 0;
		p.TickCredit = 0;
		RespawnCount++;
	}
	void TickChildren( Particle p, XmlNode op )
	{
		p.TickCredit = Math.Min( 8, p.TickCredit + Math.Max( 0, N( op, "frequency" ) ) * Time.Delta );
		int count = (int)p.TickCredit;
		p.TickCredit -= count;
		for ( int i = 0; i < count; i++ ) SpawnChildren( p, "tick", op );
	}
	void SpawnChildren( Particle p, string eventName, XmlNode op )
	{
		if ( ancestors.Count >= 4 ) return;
		foreach ( var spawn in p.Atom.Elements( "effect_spawn" ).Where( s => s.Attribute( "event" ) == eventName ) )
		{
			string path = spawn.Attribute( "effect" );
			if ( string.IsNullOrEmpty( path ) || path == EffectPath || ancestors.Contains( path ) ) continue;
			if ( Scene.GetAllComponents<PaydayEffect>().Count() >= 64 ) return;
			var go = new GameObject( Scene, false, "PAYDAY child effect" ) { NetworkMode = NetworkMode.Never };
			go.WorldPosition = p.Position;
			go.WorldRotation = op.Attribute( "use_rotation" ) == "true" ? WorldRotation * Rotation.FromRoll( p.Angle * 180 / MathF.PI ) : WorldRotation;
			var child = go.AddComponent<PaydayEffect>();
			child.EffectPath = path;
			child.ancestors = new HashSet<string>( ancestors ) { EffectPath };
			child.inheritedVelocity = op.Attribute( "use_velocity" ) == "true" ? p.Velocity : Vector3.Zero;
			children.Add( go ); ChildSpawnCount++;
			go.Enabled = true;
		}
	}
}
