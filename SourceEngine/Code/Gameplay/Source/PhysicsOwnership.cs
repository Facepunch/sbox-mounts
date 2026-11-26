namespace Source1;

/// <summary>Returns a borrowed physics object to its owner when the last holder releases it.</summary>
sealed class PhysicsOwnership : Component
{
	Connection _owner;
	int _holders;

	internal static bool TryBorrow( Component target, GameObject entity, out PhysicsOwnership borrowed )
	{
		borrowed = null;
		var root = target.Network.RootGameObject;
		if ( !root.IsValid() ) return !target.IsProxy;
		if ( !target.IsProxy )
		{
			borrowed = root.GetComponent<PhysicsOwnership>();
			if ( borrowed.IsValid() ) borrowed._holders++;
			return true;
		}
		if ( root != entity || root.Network.Owner is not { } owner ) return false;
		root.Network.DropOwnership();
		if ( target.IsProxy ) return false;
		borrowed = root.AddComponent<PhysicsOwnership>();
		borrowed.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked | ComponentFlags.NotCloned;
		borrowed._owner = owner;
		borrowed._holders = 1;
		return true;
	}

	internal void Release()
	{
		if ( !IsValid || --_holders > 0 ) return;
		var root = GameObject;
		var owner = _owner;
		Destroy();
		if ( root.IsValid() && root.Network.Owner is null && owner.IsActive )
			root.Network.AssignOwnership( owner );
	}
}
