using Sandbox.Animation;

namespace Diesel;

/// <summary>Plays a scripted unit's embedded timeline with collision sampled from the same authored pose.</summary>
[Title( "PAYDAY Scripted Prop" ), Group( "PAYDAY" )]
public sealed class PaydayScriptedProp : Component
{
	public const string NavigationTag = "payday_scripted_prop";
	[Property] public SkinnedModelRenderer Renderer { get; set; }
	/// <summary>Authored group name. Empty selects the full embedded timeline.</summary>
	[Property] public string AnimationGroup { get; set; } = "";
	[Property, Range( 0, 1 )] public float Cycle { get; set; }
	[Property] public bool Playing { get; set; }
	[Property] public bool Loop { get; set; }
	[Property] public float PlaybackRate { get; set; } = 1;
	PaydayCharacter assets;
	Clip clip;
	Graph graph;
	SkinnedModelRenderer boundRenderer;
	Model boundModel;
	string boundGroup;
	GameObject collisionRoot;
	readonly List<(uint Node, GameObject Object)> colliders = [];

	protected override void OnUpdate()
	{
		if ( !Renderer.IsValid() || Renderer.Model is null ) { Release(); return; }
		if ( boundRenderer != Renderer || boundModel != Renderer.Model || boundGroup != (AnimationGroup ?? "") ) Initialize();
		if ( clip is null ) return;
		if ( Playing && float.IsFinite( PlaybackRate ) )
		{
			Cycle += Time.Delta * PlaybackRate / clip.Duration;
			if ( Loop ) Cycle = ((Cycle % 1) + 1) % 1;
			else if ( Cycle <= 0 || Cycle >= 1 ) { Cycle = Math.Clamp( Cycle, 0, 1 ); Playing = false; }
		}
		Cycle = float.IsFinite( Cycle ) ? Math.Clamp( Cycle, 0, 1 ) : 0;
		Renderer.Animation.Parameters.Set( "cycle", Cycle );
		var pose = assets.SampleEmbedded( Cycle * clip.Duration, boundGroup );
		foreach ( var (node, go) in colliders )
			go.WorldTransform = Renderer.WorldTransform.ToWorld( pose[node] );
	}

	void Initialize()
	{
		Release();
		var path = Renderer.Model.ResourcePath;
		if ( Sandbox.Mounting.MountUtility.FindLoader( path ) is not DieselModelLoader loader )
			throw new InvalidOperationException( "PAYDAY Scripted Prop requires a mounted scripted unit model." );
		assets = loader.GetCharacter();
		var parts = assets.GetScriptedCollision( path );
		var group = AnimationGroup ?? "";
		clip = Clip.Load( path + (group.Length == 0 ? "/embedded.anim" : $"/groups/{group}.anim") );
		if ( clip is null || clip.IsError ) throw new InvalidOperationException( "Scripted unit has no embedded timeline." );
		var builder = new GraphBuilder().WithSkeleton( clip.Skeleton );
		var cycle = builder.FloatParameter( "cycle", 0, 1 );
		graph = builder.Create( builder.TimeControlledClip( clip, cycle, new() { SampleRootMotion = false } ) );
		Renderer.UseAnimGraph = true;
		Renderer.Animation.Graph = graph;
		boundRenderer = Renderer;
		boundModel = Renderer.Model;
		boundGroup = group;
		collisionRoot = new GameObject( true, "PAYDAY animated collision" )
		{
			Parent = GameObject, Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked
		};
		Scene.NavMesh.ExcludedBodies.Add( NavigationTag );
		foreach ( var part in parts )
		{
			var go = new GameObject( true, $"Collision {part.Node}" ) { Parent = collisionRoot };
			go.Tags.Add( NavigationTag );
			var collider = go.AddComponent<ModelCollider>();
			collider.Static = false;
			collider.Model = part.Model;
			colliders.Add( (part.Node, go) );
		}
	}

	void Release()
	{
		if ( collisionRoot.IsValid() ) { collisionRoot.Enabled = false; collisionRoot.Destroy(); }
		collisionRoot = null;
		colliders.Clear();
		if ( boundRenderer.IsValid() && boundRenderer.Animation.Graph == graph ) boundRenderer.Animation.Graph = null;
		graph?.Dispose();
		graph = null; clip = null; assets = null; boundRenderer = null; boundModel = null; boundGroup = null;
	}

	protected override void OnDisabled() => Release();
	protected override void OnDestroy() => Release();
}
