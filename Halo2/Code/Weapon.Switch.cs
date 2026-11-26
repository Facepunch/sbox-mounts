namespace Halo2;

public abstract partial class Weapon
{
	[Property] public int DefaultPutAwayFrames { get; set; }
	[Property] public int DefaultPutAwayRelease { get; set; }

	internal static int ReleaseFrame( Halo2ClipInfo clip ) =>
		clip.Cues.FirstOrDefault( cue => cue.Kind == "allow_interruption" )?.Frame ?? clip.Frames;

	BaseInventoryItem requestedItem;
	int putAwayFrames, releaseFrame;
	internal bool PuttingAway => putAwayFrames > 0;
	double putAwayAt;
	bool finishingSwitch;

	protected override bool OnHolstering( BaseInventoryItem next )
	{
		if ( finishingSwitch ) return true;
		if ( Offhand.IsValid() )
		{
			bool switchingToOffhand = next == Offhand;
			if ( Networking.IsHost ) Inventory.Drop( Offhand );
			if ( switchingToOffhand ) return false;
		}
		if ( PuttingAway )
		{
			requestedItem = next;
			return false;
		}
		var clip = HolderAnimator?.GetOverlayClip( "put_away" );
		int frames = clip?.Frames ?? DefaultPutAwayFrames;
		if ( frames <= 0 )
			return base.OnHolstering( next );
		if ( Networking.IsHost )
		{
			requestedItem = next;
			CancelReload();
			BeginPutAway( frames, clip is null ? DefaultPutAwayRelease : ReleaseFrame( clip ) );
		}
		return false;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void BeginPutAway( int frames, int release )
	{
		if ( !IsHeld || IsHolstered ) return;
		putAwayFrames = frames;
		releaseFrame = release;
		putAwayAt = System.Math.Floor( Time.NowDouble * 30 ) / 30;
		if ( !IsProxy ) SetZoom( 0 );
		PlayPutAway();
		HolderAnimator?.SampleWeaponOverlay( "put_away", 0 );
	}

	protected virtual void PlayPutAway() { }

	protected override void OnUpdate()
	{
		if ( !IsOffhand ) base.OnUpdate();
		UpdateWeaponState();
		if ( !PuttingAway ) return;
		double ticks = (Time.NowDouble - putAwayAt) * 30;
		// The unit channel holds its first sample for two native updates.
		float frame = (float)System.Math.Max( 0, ticks - 2 );
		HolderAnimator?.SampleWeaponOverlay( "put_away", (frame / System.Math.Max( 1, putAwayFrames - 1 )).Clamp( 0, 1 ) );
		if ( !Networking.IsHost || ticks + 0.0001 < releaseFrame ) return;
		var next = requestedItem;
		finishingSwitch = true;
		try
		{
			if ( next is null || (next.IsValid() && next.Inventory == Inventory) )
				Inventory?.Switch( next, next is null );
		}
		finally
		{
			finishingSwitch = false;
			if ( PuttingAway ) EndPutAway();
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void EndPutAway() => ClearPutAway();

	void ClearPutAway()
	{
		if ( PuttingAway ) HolderAnimator?.StopWeaponOverlay( "put_away" );
		putAwayFrames = 0;
		requestedItem = null;
	}

	protected override void OnHolstered()
	{
		pickupStarted = null;
		pickupRequested = false;
		if ( Networking.IsHost ) SetOffhand( null );
		ClearPutAway();
		base.OnHolstered();
	}
}
