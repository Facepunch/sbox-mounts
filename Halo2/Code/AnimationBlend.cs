namespace Halo2;

internal struct AnimationBlend
{
	int tick, ticks;
	bool enabled;

	public readonly float Weight
	{
		get
		{
			if ( ticks == 0 ) return 0;
			float t = tick / (float)ticks;
			float square = t * t;
			return 3 * square - (square + square) * t;
		}
	}

	public void Advance() => tick = (tick + (enabled ? 1 : -1)).Clamp( 0, ticks );

	public void Select( bool active, int durationTicks )
	{
		int next = durationTicks.Clamp( 1, 127 ) - 1;
		if ( next == ticks && active == enabled ) return;
		if ( next == 0 ) tick = 0;
		else if ( ticks > 0 ) tick = (int)(tick / (float)ticks * (next - 1));
		else tick = active ? 1 : next - 1;
		ticks = next;
		enabled = active;
	}
}
