namespace Overgrowth;

// The physics skeleton drives the bones, while the active ragdoll clip still drives finger shapes.
internal sealed class RagdollHands : Component
{
	[Sync( SyncFlags.Interpolate )] Vector2 Closure { get; set; }
	CharacterMorph[] shapes;
	SkinnedModelRenderer renderer;
	RabbitGuard fighter;
	float remainder;
	Vector2? displayed;

	protected override void OnStart()
	{
		renderer = GetComponent<SkinnedModelRenderer>();
		fighter = GetComponentInParent<RabbitGuard>();
		shapes = CharacterLoader.FromModel( renderer?.Model )?.HandShapes;
	}

	internal void Begin( SkinnedModelRenderer source, Vector2? inherited = null )
	{
		displayed = null;
		remainder = 0f;
		Closure = inherited ?? default;
		if ( inherited.HasValue || !source.IsValid() ) return;
		var hands = CharacterLoader.FromModel( source.Model )?.HandShapes;
		if ( hands is null ) return;
		foreach ( var hand in hands )
		{
			source.Animation.Debug.TryGetFloatChannel( hand.Channel, hand.Name, out var weight );
			Closure = hand.Name == "fist_l" ? Closure.WithX( weight ) : Closure.WithY( weight );
		}
	}

	internal Vector2 Current => Closure;

	internal void Advance( RabbitClip clip, float time, float delta, float strength )
	{
		const float step = 4f / 120f;
		remainder += delta;
		while ( remainder >= step )
		{
			remainder -= step;
			if ( strength <= 0f || shapes is null ) continue;
			float phase = ((time - remainder) / clip.Duration) % 1f;
			foreach ( var hand in shapes )
			{
				clip.Animation.TryGetFloatChannel( phase, hand.Channel, hand.Name, out var weight );
				bool left = hand.Name == "fist_l";
				float previous = left ? Closure.x : Closure.y;
				float value = previous + (weight - previous) * strength;
				Closure = left ? Closure.WithX( value ) : Closure.WithY( value );
			}
		}
	}

	protected override void OnUpdate()
	{
		if ( shapes is null || !renderer.IsValid() ) return;
		if ( fighter.IsValid() && !fighter.Downed )
		{
			displayed = null;
			return;
		}
		var value = Closure;
		if ( fighter.IsValid() )
		{
			if ( fighter.LeftHandItem.IsValid() ) value = value.WithX( 1f );
			if ( fighter.RightHandItem.IsValid() ) value = value.WithY( 1f );
		}
		if ( displayed == value ) return;
		displayed = value;
		foreach ( var hand in shapes )
			hand.Apply( renderer, hand.Name == "fist_l" ? value.x : value.y );
	}
}
