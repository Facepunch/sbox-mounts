using System;

namespace Overgrowth;

// MovementObject::CheckAttackHistory and AddToAttackHistory.
internal sealed class AttackHistory
{
	readonly Dictionary<(RabbitAttack From, RabbitAttack To), float> transitions = new();
	RabbitAttack previous;
	TimeSince elapsed;

	internal float Record( RabbitAttack move )
	{
		float score = 0.5f;
		if ( previous is not null )
		{
			score += Similarity( previous.Direction, move.Direction, "left", "right" );
			score += Similarity( previous.Height, move.Height, "low", "high" );
			score += previous == move ? 0.15f : -0.15f;
			if ( elapsed < 2f )
			{
				var pair = (previous, move);
				if ( transitions.TryGetValue( pair, out var count ) )
					score += (1f - score) * count / transitions.Where( entry => entry.Key.From == previous ).Sum( entry => entry.Value );
				foreach ( var key in transitions.Keys.ToArray() )
				{
					var weight = transitions[key] * 0.9f - 0.05f;
					if ( weight <= 0 ) transitions.Remove( key );
					else transitions[key] = weight;
				}
				transitions[pair] = transitions.GetValueOrDefault( pair ) + 1f;
			}
		}
		previous = move;
		elapsed = 0;
		return score;
	}

	static float Similarity( string a, string b, string left, string right ) =>
		a == b ? 0.15f : (a == left && b == right || a == right && b == left) ? -0.15f : 0f;
}
