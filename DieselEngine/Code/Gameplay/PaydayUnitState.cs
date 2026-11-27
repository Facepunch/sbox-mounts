namespace Diesel;

/// <summary>
/// Runtime geometry, collision and damage state for a placed world unit.
/// <list type="bullet">
/// <item>Holds the sequences run on the unit so far and rebuilds its geometry/collision from them (lenient evaluator).</item>
/// <item>Breakable units accumulate damage per body and type and run each endurance stage once its threshold is reached.</item>
/// </list>
/// </summary>
[Hide]
[Alias( "Diesel.PaydayMissionUnit" )]
public sealed class PaydayUnitState : Component, Component.IDamageable
{
	/// <summary>Map <c>unit_id</c> (0 if the placement has none).</summary>
	[Property] public new int Id { get; set; }
	[Property] public string Unit { get; set; }
	[Property] public string MaterialVariation { get; set; }
	/// <summary>Has its own endurance damage rules.</summary>
	[Property] public bool Breakable { get; set; }
	/// <summary>Authored collision-body bounds in unit space; used for approximate impact attribution.</summary>
	[Property] public Dictionary<string, BBox> DamageBodies { get; set; } = [];
	/// <summary>Last sequence run on this unit, if any.</summary>
	[Property] public string Sequence { get; set; }

	/// <summary>Source-damage units per sandbox damage point. The mount's weapons apply 10× source damage.</summary>
	const float DamageScale = 0.1f;

	readonly List<string> applied = [];
	readonly Dictionary<(string Body, string Type), float> damageTaken = [];
	readonly HashSet<(string Body, int Stage)> stagesRun = [];
	HashSet<string> enabledBodies;

	internal static int BrokenStages;

	static PaydayMount Mount => Sandbox.Mounting.Directory.Get( "payday" ) as PaydayMount;

	/// <summary>
	/// Rebuild the unit with <paramref name="sequence"/> applied on top of everything run before. Returns false when the
	/// sequence changes nothing visible (effects/sounds only, or unreproducible). Host-side; the full list is published
	/// through <see cref="PaydayMapState"/> so clients rebuild the same state.
	/// </summary>
	internal bool ApplySequence( string sequence )
	{
		Sequence = sequence;
		if ( GetComponent<PaydayDoor>().IsValid() ) return false;
		applied.Add( sequence );
		TrackBodies( sequence );
		PaydayMapState.For( Scene )?.SetUnitState( GameObject, applied );
		PlaySounds( sequence );
		PaydaySequenceEffects.Play( this, sequence );
		return Rebuild();
	}

	/// <summary>
	/// Client: adopt the host's full applied-sequence list for this unit and rebuild. With <paramref name="audible"/>, the
	/// sequences appended since the last state play their sounds (not on the join snapshot, which only restores state).
	/// </summary>
	internal void ApplyState( IReadOnlyList<string> sequences, bool audible = false )
	{
		if ( GetComponent<PaydayDoor>().IsValid() ) return;
		int known = applied.Count <= sequences.Count && applied.SequenceEqual( sequences.Take( applied.Count ) ) ? applied.Count : sequences.Count;
		if ( audible ) foreach ( var sequence in sequences.Skip( known ) )
		{
			PlaySounds( sequence );
			PaydaySequenceEffects.Play( this, sequence );
		}
		applied.Clear();
		enabledBodies = null;
		foreach ( var sequence in sequences ) { applied.Add( sequence ); TrackBodies( sequence ); }
		Sequence = applied.LastOrDefault();
		Rebuild();
	}

	/// <summary>Play the sound events a sequence's <c>sound</c> actions name, at the unit (before it's rebuilt or hidden).</summary>
	void PlaySounds( string sequence )
	{
		if ( Mount?.SequenceSoundPaths( Unit, sequence ) is not { Length: > 0 } paths ) return;
		var renderer = GetComponent<ModelRenderer>();
		var position = renderer.IsValid() && renderer.Model is not null ? renderer.Bounds.Center : WorldPosition;
		foreach ( var path in paths ) _ = PlayAt( path, position );
	}

	static async Task PlayAt( string path, Vector3 position )
	{
		SoundEvent sound = null;
		try { sound = await ResourceLibrary.LoadAsync<SoundEvent>( path ); }
		catch ( Exception e ) { Log.Warning( $"PAYDAY sequence sound {path}: {e.Message}" ); }
		if ( sound is not null ) Sound.Play( sound, position );
	}

	void TrackBodies( string sequence )
	{
		if ( Mount?.GetEndurance( Unit ) is not { } rules ) return;
		enabledBodies ??= new HashSet<string>( rules.DefaultEnabledBodies, StringComparer.Ordinal );
		foreach ( var (body, enabled) in rules.BodyToggles( sequence ) )
			if ( enabled ) enabledBodies.Add( body ); else enabledBodies.Remove( body );
	}

	/// <summary>Rebuild geometry/collision from <see cref="applied"/>. False when the sequences change nothing visible.</summary>
	bool Rebuild()
	{
		if ( Mount is not { Database: not null } mount || applied.Count == 0 ) return false;
		string variation = MaterialVariation ?? "";
		UnitModel.Result result = null;
		try
		{
			string state = string.Join( "|", applied );
			result = UnitModel.BuildSequenceState( mount.Database, mount.RuntimeMaterials, Unit,
				$"mount://payday/runtime/{IdString.Hash( Unit ):x16}_{IdString.Hash( variation ):x16}_{IdString.Hash( state ):x16}.vmdl", variation, applied );
		}
		catch ( Exception e ) { Log.Warning( $"PAYDAY sequence {Unit}/{applied[^1]}: {e.Message}" ); }
		if ( result is null ) return false;
		DamageBodies = result.DamageBodies;
		var renderer = GetComponent<ModelRenderer>();
		var collider = GetComponent<ModelCollider>();
		if ( !result.HasGraphics && !result.HasCollision ) { GameObject.Enabled = false; return true; }
		GameObject.Enabled = true;
		if ( renderer.IsValid() ) { renderer.Model = result.Model; renderer.Enabled = result.HasGraphics; }
		if ( collider.IsValid() ) { collider.Model = result.Model; collider.Enabled = result.HasCollision; }
		return true;
	}

	void IDamageable.OnDamage( in DamageInfo damage )
	{
		if ( !Networking.IsHost || damage is null || !float.IsFinite( damage.Damage ) || damage.Damage <= 0 ) return;
		bool explosion = damage.Tags?.Has( "explosion" ) == true || damage.Tags?.Has( "blast" ) == true;
		if ( Breakable && Mount?.GetEndurance( Unit ) is { } rules )
		{
			enabledBodies ??= new HashSet<string>( rules.DefaultEnabledBodies, StringComparer.Ordinal );
			string body = explosion ? null : FindDamageBody( damage.Position, enabledBodies );
			if ( !explosion && body is null ) return;
			EnduranceDamage( rules, explosion ? "explosion" : damage.Tags?.Has( "melee" ) == true ? "melee" : "bullet", damage.Damage * DamageScale, body );
		}
	}

	/// <summary>
	/// Direct impacts select one authored collision body's bounds; explosions affect all enabled bodies.
	/// Each body accumulates damage per type; stages run in order, once each, when a type reaches its threshold.
	/// </summary>
	void EnduranceDamage( UnitEndurance rules, string type, float amount, string hitBody )
	{
		enabledBodies ??= new HashSet<string>( rules.DefaultEnabledBodies, StringComparer.Ordinal );
		foreach ( var (body, stages) in rules.Bodies.ToList() )
		{
			if ( !enabledBodies.Contains( body ) || (hitBody is not null && hitBody != body) ) continue;
			float total = damageTaken[(body, type)] = damageTaken.GetValueOrDefault( (body, type) ) + amount;
			for ( int i = 0; i < stages.Count; i++ )
			{
				if ( stagesRun.Contains( (body, i) ) || !stages[i].Thresholds.TryGetValue( type, out float threshold ) || total < threshold ) continue;
				stagesRun.Add( (body, i) );
				BrokenStages++;
				foreach ( var sequence in stages[i].Sequences )
					ApplySequence( sequence );
				if ( !enabledBodies.Contains( body ) ) break; // the stage disabled this body (e.g. shattered)
			}
		}
	}

	internal string FindDamageBody( Vector3 worldPosition, IEnumerable<string> candidates )
	{
		var names = candidates.Where( b => enabledBodies?.Contains( b ) != false ).OrderBy( b => b, StringComparer.Ordinal ).ToArray();
		var point = WorldTransform.PointToLocal( worldPosition );
		if ( !float.IsFinite( point.x ) || !float.IsFinite( point.y ) || !float.IsFinite( point.z ) ) return null;
		if ( names.Length == 1 ) return names[0];
		// The map collider merges source shapes. Nearest AABB is an explicit approximation;
		// centre distance breaks overlapping-bound ties without damaging unrelated bodies.
		return names.Where( b => DamageBodies?.ContainsKey( b ) == true )
			.OrderBy( b => Distance( DamageBodies[b], point ) )
			.ThenBy( b => DamageBodies[b].Center.DistanceSquared( point ) ).FirstOrDefault();
	}

	static float Distance( BBox box, Vector3 point )
	{
		var nearest = new Vector3( Math.Clamp( point.x, box.Mins.x, box.Maxs.x ),
			Math.Clamp( point.y, box.Mins.y, box.Maxs.y ), Math.Clamp( point.z, box.Mins.z, box.Maxs.z ) );
		return nearest.DistanceSquared( point );
	}

}
