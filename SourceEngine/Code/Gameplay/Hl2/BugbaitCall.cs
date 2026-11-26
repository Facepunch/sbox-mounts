namespace Source1.Hl2;

/// <summary>The short-lived AI sound left where an unsuppressed pheropod bursts.</summary>
public sealed class BugbaitCall : Component
{
	[ConVar( "bugbait_hear_radius", ConVarFlags.Server )]
	public static int HearingRadius { get; set; } = 2500;

	[ConVar( "bugbait_distract_time", ConVarFlags.Server )]
	public static float Duration { get; set; } = 5;

	double expires;
	float radiusSquared;
	TimeUntil nextListen;

	internal static void Emit( Scene scene, Vector3 position )
	{
		if ( !Networking.IsHost || Duration <= 0 ) return;
		var go = new GameObject( scene, false, "Bugbait call" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;
		var call = go.AddComponent<BugbaitCall>();
		call.expires = Time.NowDouble + Duration;
		call.radiusSquared = (float)HearingRadius * HearingRadius;
		go.Enabled = true;
	}

	protected override void OnFixedUpdate()
	{
		if ( Time.NowDouble >= expires )
		{
			GameObject.Destroy();
			return;
		}

		if ( nextListen > 0 ) return;
		nextListen = 0.1f;
		foreach ( var npc in Scene.GetAllComponents<CreatureNpc>() )
			npc.ListenForBugbait( WorldPosition, radiusSquared, expires );
	}
}
