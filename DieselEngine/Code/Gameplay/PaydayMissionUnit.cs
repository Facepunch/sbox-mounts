namespace Diesel;

/// <summary>
/// Runtime state for a placed world unit that can change state: units the mission refers to by <c>unit_id</c>, and units
/// with their own damage rules (<see cref="UnitEndurance"/>: glass, bulbs, display stands…).
/// <list type="bullet">
/// <item>Holds the sequences run on the unit so far and rebuilds its geometry/collision from them (lenient evaluator).</item>
/// <item>Breakable units accumulate damage per body and type and run each endurance stage once its threshold is reached.</item>
/// <item>Mission units are pressable (E) while the mission waits for <c>interact</c>, <c>first_interact</c> or <c>done_crowbar</c>.</item>
/// <item>Every sequence a unit runs is reported to the mission (<see cref="PaydayMission.SequenceRan"/>).</item>
/// </list>
/// </summary>
[Hide]
public sealed class PaydayMissionUnit : Component, Component.IPressable, Component.IDamageable
{
	/// <summary>Map <c>unit_id</c> (0 if the placement has none).</summary>
	[Property] public int Id { get; set; }
	[Property] public string Unit { get; set; }
	[Property] public string MaterialVariation { get; set; }
	/// <summary>Has its own endurance damage rules.</summary>
	[Property] public bool Breakable { get; set; }
	/// <summary>Last sequence run on this unit, if any.</summary>
	[Property] public string Sequence { get; set; }

	/// <summary>Source-damage units per sandbox damage point. The mount's weapons apply 10× source damage.</summary>
	const float DamageScale = 0.1f;

	readonly List<string> applied = [];
	readonly Dictionary<(string Body, string Type), float> damageTaken = [];
	readonly HashSet<(string Body, int Stage)> stagesRun = [];
	HashSet<string> enabledBodies;
	TimeSince lastHit;

	internal static int BrokenStages;

	static PaydayMount Mount => Sandbox.Mounting.Directory.Get( "payday" ) as PaydayMount;

	/// <summary>
	/// Rebuild the unit with <paramref name="sequence"/> applied on top of everything run before. Returns false when the
	/// sequence changes nothing visible (effects/sounds only, or unreproducible). Host-side, not yet replicated.
	/// </summary>
	internal bool ApplySequence( string sequence )
	{
		Sequence = sequence;
		if ( Mount is not { Database: not null } mount || GetComponent<PaydayDoor>().IsValid() ) return false;
		applied.Add( sequence );
		if ( mount.GetEndurance( Unit ) is { } rules )
		{
			enabledBodies ??= new HashSet<string>( rules.DefaultEnabledBodies, StringComparer.Ordinal );
			foreach ( var (body, enabled) in rules.BodyToggles( sequence ) )
				if ( enabled ) enabledBodies.Add( body ); else enabledBodies.Remove( body );
		}
		string variation = MaterialVariation ?? "";
		UnitModel.Result result = null;
		try
		{
			string state = string.Join( "|", applied );
			result = UnitModel.BuildSequenceState( mount.Database, mount.RuntimeMaterials, Unit,
				$"mount://payday/runtime/{IdString.Hash( Unit ):x16}_{IdString.Hash( variation ):x16}_{IdString.Hash( state ):x16}.vmdl", variation, applied );
		}
		catch ( Exception e ) { Log.Warning( $"PAYDAY sequence {Unit}/{sequence}: {e.Message}" ); }
		if ( result is null ) return false;
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
			EnduranceDamage( rules, explosion ? "explosion" : damage.Tags?.Has( "melee" ) == true ? "melee" : "bullet", damage.Damage * DamageScale, damage.Attacker );
			return;
		}
		// Mission-only units without endurance rules: run the damage sequence the mission listens for.
		if ( lastHit < 0.15f ) return;
		lastHit = 0;
		foreach ( var mission in Scene.GetAllComponents<PaydayMission>() )
			mission.Damage( Id, explosion, damage.Attacker );
	}

	/// <summary>
	/// Hits reach every currently enabled body with endurance (one merged collider, so the struck body isn't known).
	/// Each body accumulates damage per type; stages run in order, once each, when a type reaches its threshold.
	/// </summary>
	void EnduranceDamage( UnitEndurance rules, string type, float amount, GameObject attacker )
	{
		enabledBodies ??= new HashSet<string>( rules.DefaultEnabledBodies, StringComparer.Ordinal );
		foreach ( var (body, stages) in rules.Bodies.ToList() )
		{
			if ( !enabledBodies.Contains( body ) ) continue;
			float total = damageTaken[(body, type)] = damageTaken.GetValueOrDefault( (body, type) ) + amount;
			for ( int i = 0; i < stages.Count; i++ )
			{
				if ( stagesRun.Contains( (body, i) ) || !stages[i].Thresholds.TryGetValue( type, out float threshold ) || total < threshold ) continue;
				stagesRun.Add( (body, i) );
				BrokenStages++;
				foreach ( var sequence in stages[i].Sequences )
				{
					ApplySequence( sequence );
					PaydayMission.SequenceRan( GameObject, sequence, attacker );
				}
				if ( !enabledBodies.Contains( body ) ) break; // the stage disabled this body (e.g. shattered)
			}
		}
	}

	PaydayMission Mission => Id == 0 ? null : Scene.GetAllComponents<PaydayMission>().FirstOrDefault( m => m.InteractionFor( Id ) is not null );

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e ) => Mission?.InteractionFor( Id ) switch
	{
		"first_interact" => new IPressable.Tooltip( "Start", "build", Label ),
		"done_crowbar" => new IPressable.Tooltip( "Pry open", "handyman", Label ),
		"interact" => new IPressable.Tooltip( "Use", "pan_tool", Label ),
		_ => null,
	};

	string Label => string.IsNullOrEmpty( Unit ) ? null : Unit[(Unit.LastIndexOf( '/' ) + 1)..].Replace( '_', ' ' );

	bool IPressable.CanPress( IPressable.Event e ) => Mission is not null;

	bool IPressable.Press( IPressable.Event e )
	{
		Interact( e.Source.GameObject );
		return true;
	}

	[Rpc.Host]
	void Interact( GameObject source )
	{
		if ( !PaydayInteraction.CanUse( this, source ) ) return;
		Mission?.Interact( Id, source.Root );
	}
}
