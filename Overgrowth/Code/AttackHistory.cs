using System;

namespace Overgrowth;

// MovementObject::CheckAttackHistory and AddToAttackHistory.
internal sealed class AttackHistory
{
	readonly Dictionary<(string From, string To), float> transitions = new();
	RabbitAttack previous;
	string previousDirection;
	TimeSince elapsed;

	internal float Record( RabbitAttack move, bool flipped )
	{
		var direction = flipped ? move.Direction switch { "left" => "right", "right" => "left", _ => move.Direction } : move.Direction;
		float score = 0.5f;
		if ( previous is not null )
		{
			score += Similarity( previousDirection, direction, "left", "right" );
			score += Similarity( previous.Height, move.Height, "low", "high" );
			score += previous.AssetPath == move.AssetPath ? 0.15f : -0.15f;
			if ( elapsed < 2f )
			{
				var pair = (previous.AssetPath, move.AssetPath);
				if ( transitions.TryGetValue( pair, out var count ) )
					score += (1f - score) * count / transitions.Where( entry => entry.Key.From == previous.AssetPath ).Sum( entry => entry.Value );
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
		previousDirection = direction;
		elapsed = 0;
		return score;
	}

	static float Similarity( string a, string b, string left, string right ) =>
		a == b ? 0.15f : (a == left && b == right || a == right && b == left) ? -0.15f : 0f;
}
