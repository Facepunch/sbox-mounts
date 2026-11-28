using System;

namespace Renderware;

partial class DffModelBuilder
{
	/// <summary>
	/// A bone of the rig. <see cref="Bind"/> is its model space bind pose in GTA units, <see cref="Bounds"/> the bone
	/// local bounds of the geometry weighted to it. <see cref="Frame"/> is null for an animation node with no frame.
	/// </summary>
	sealed class RigBone
	{
		public int Index;
		public string Name;
		public RigBone Parent;
		public Frame Frame;
		public Transform Bind;
		public BBox? Bounds;
	}

	readonly List<RigBone> rig = new();
	readonly Dictionary<Frame, RigBone> frameBones = new();

	/// <summary>
	/// Whether the rig comes from an animation hierarchy, its bone indices being the ones the skins use.
	/// </summary>
	bool skinnedRig;

	/// <summary>Stands skinned geometry up, see <see cref="GetUpright"/>.</summary>
	Rotation upright = Rotation.Identity;

	/// <summary>
	/// Skinned models (San Andreas / Vice City peds) get a bone per node of their animation hierarchy, in node order,
	/// which is what the skin's bone indices count. Everything else (vehicles, III's peds, which are rigid limbs)
	/// gets a bone per frame.
	/// </summary>
	void BuildRig()
	{
		var root = dff.FrameList.HierarchyRoot;
		var skin = dff.GeometryList.Geometries.FirstOrDefault( g => g?.Skin is not null )?.Skin;

		skinnedRig = root is not null && skin is not null;
		if ( skinnedRig )
			BuildSkinnedRig( root.HAnim.Nodes, skin );
		else
			BuildFrameRig();

		// names must be unique, and some files repeat them (Vice City peds have two "Root" frames)
		var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var bone in rig )
		{
			var name = string.IsNullOrWhiteSpace( bone.Name ) ? $"bone{bone.Index}" : bone.Name.Trim();
			for ( int n = 2; !names.Add( name ); n++ )
				name = $"{bone.Name.Trim()}_{n}";
			bone.Name = name;
		}
	}

	/// <summary>
	/// Every frame, parents first (so a bone's index never comes before its parent's).
	/// </summary>
	void BuildFrameRig()
	{
		var frames = dff.FrameList;

		void Add( Frame frame, RigBone parent )
		{
			var bone = new RigBone { Index = rig.Count, Name = frame.Name, Parent = parent, Frame = frame, Bind = frame.Transform };
			rig.Add( bone );
			frameBones[frame] = bone;

			foreach ( var child in frames.Frames.Where( f => frames.GetParent( f ) == frame && !frameBones.ContainsKey( f ) ) )
				Add( child, bone );
		}

		foreach ( var root in frames.Frames.Where( f => frames.GetParent( f ) is null ) )
			Add( root, null );
	}

	/// <summary>
	/// A bone per animation node, bind poses from the skin's inverse bind matrices. Those, and so the skinned
	/// vertices, aren't in the frame hierarchy's space: Vice City's peds lie along Y, San Andreas' along X - in game
	/// the animations stand them up. We stand the whole rig up once instead, facing +Y like the game's models.
	/// </summary>
	void BuildSkinnedRig( HAnim.Node[] nodes, Skin skin )
	{
		var frames = dff.FrameList;

		for ( int i = 0; i < nodes.Length; i++ )
		{
			frames.TryGetBone( nodes[i].NodeId, out var frame );

			var bind = i < skin.InverseBindPoses.Length ? skin.GetBindPose( i ) : frame?.Transform ?? Transform.Zero;
			var bone = new RigBone { Index = i, Name = frame?.Name ?? $"node{nodes[i].NodeId}", Frame = frame, Bind = bind };
			rig.Add( bone );

			if ( frame is not null )
				frameBones.TryAdd( frame, bone );
		}

		// parent: the closest ancestor frame that's a bone - nodes are depth first, so it's always an earlier one
		foreach ( var bone in rig.Where( b => b.Frame is not null ) )
		{
			for ( var frame = frames.GetParent( bone.Frame ); frame is not null; frame = frames.GetParent( frame ) )
			{
				if ( frameBones.TryGetValue( frame, out var parent ) )
				{
					bone.Parent = parent.Index < bone.Index ? parent : null;
					break;
				}
			}
		}

		upright = GetUpright();
		foreach ( var bone in rig )
			bone.Bind = new Transform( upright * bone.Bind.Position, upright * bone.Bind.Rotation );
	}

	/// <summary>
	/// The rotation that stands a skinned ped up - head over pelvis along +Z, facing +Y with its right arm along +X -
	/// from its bind pose. The node ids are the same in Vice City and San Andreas: 1 pelvis, 5 head, 22 / 32 right /
	/// left upper arm. Identity if the hierarchy doesn't have them.
	/// </summary>
	Rotation GetUpright()
	{
		bool TryGetPosition( int nodeId, out Vector3 position )
		{
			var frame = dff.FrameList.TryGetBone( nodeId, out var f ) ? f : null;
			var bone = frame is not null ? frameBones.GetValueOrDefault( frame ) : null;
			position = bone?.Bind.Position ?? default;
			return bone is not null;
		}

		if ( !TryGetPosition( 1, out var pelvis ) || !TryGetPosition( 5, out var head ) || !TryGetPosition( 22, out var rightArm ) || !TryGetPosition( 32, out var leftArm ) )
			return Rotation.Identity;

		var up = SnapToAxis( head - pelvis );
		var right = SnapToAxis( rightArm - leftArm );
		if ( MathF.Abs( up.Dot( right ) ) > 0.5f )
			return Rotation.Identity;

		var forward = Vector3.Cross( up, right );

		// forward -> x, up -> z, then turn x to y
		return Rotation.FromYaw( 90 ) * Rotation.LookAt( forward, up ).Inverse;
	}

	static Vector3 SnapToAxis( Vector3 v )
	{
		var a = v.Abs();
		return a.x >= a.y && a.x >= a.z ? new Vector3( MathF.Sign( v.x ), 0, 0 )
			: a.y >= a.z ? new Vector3( 0, MathF.Sign( v.y ), 0 )
			: new Vector3( 0, 0, MathF.Sign( v.z ) );
	}

	/// <summary>
	/// The bone rigid geometry on <paramref name="frame"/> follows: its frame's, or the closest ancestor's that has
	/// one, or the root.
	/// </summary>
	RigBone GetBone( Frame frame )
	{
		for ( var f = frame; f is not null; f = dff.FrameList.GetParent( f ) )
		{
			if ( frameBones.TryGetValue( f, out var bone ) )
				return bone;
		}

		return rig[0];
	}

	/// <summary>
	/// A model space (frame hierarchy) transform in the rig's space - the same thing for a frame rig, for a skinned one
	/// relative to the bone's bind pose (geometry on a frame keeps its place relative to that frame's bone).
	/// </summary>
	Transform ToRigSpace( RigBone bone, Transform transform )
	{
		if ( bone.Frame is null )
			return transform;

		return bone.Bind.ToWorld( bone.Frame.Transform.ToLocal( transform ) );
	}

	void AddBoundsPoint( RigBone bone, Vector3 position )
	{
		var local = bone.Bind.PointToLocal( position );
		bone.Bounds = bone.Bounds?.AddPoint( local ) ?? new BBox( local, local );
	}

	ModelBuilder.Bone[] GetBones() => rig.Select( b =>
	{
		// bounds help culling once the bones move
		var bounds = b.Bounds is { } bb ? new BBox( bb.Mins * Units.Scale, bb.Maxs * Units.Scale ) : (BBox?)null;
		return new ModelBuilder.Bone( b.Name, b.Parent?.Name, b.Bind.Position * Units.Scale, b.Bind.Rotation ) { Bounds = bounds };
	} ).ToArray();
}
