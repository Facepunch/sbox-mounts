using System;

namespace Magicka;

internal sealed partial class MagickaModelBuilder
{
	private static readonly string[] IgnoredRagdollBones =
	[
		"attach", "finger", "index", "pinky", "thumb", "middle", "ring",
		"toe", "footend", "jaw", "mouth", "eye", "hair", "hood", "hat",
		"cape", "cloak", "cloth", "skirt", "dress", "ribbon", "beard",
		"weapon", "prop", "ncl", "end", "tip", "ik", "nub",
	];

	/// <summary>
	/// Generates an approximated articulated ragdoll from the character's bind-pose skeleton so
	/// Magicka characters can be spawned as ragdolls and tossed around in Sandbox.
	/// </summary>
	private void AddRagdollPhysics( ModelBuilder builder, BBox totalBounds )
	{
		if ( bones.Count < 3 || totalBounds.Size.LengthSquared < 1f )
			return;

		var boneByName = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		for ( var i = 0; i < bones.Count; i++ )
			boneByName.TryAdd( bones[i].Name, i );

		var parentIndex = new int[bones.Count];
		var children = new List<int>[bones.Count];
		for ( var i = 0; i < bones.Count; i++ )
			children[i] = [];

		for ( var i = 0; i < bones.Count; i++ )
		{
			var p = bones[i].ParentName is not null && boneByName.TryGetValue( bones[i].ParentName, out var pi ) && pi != i ? pi : -1;
			parentIndex[i] = p;
			if ( p >= 0 )
				children[p].Add( i );
		}

		// Pick major structural bones that actually have mesh vertices skinned to them
		// (skips unused bones from shared skeletons like floppy wizard hats or capes).
		var kept = new List<int>();
		var keptSet = new HashSet<int>();
		for ( var i = 0; i < bones.Count; i++ )
		{
			var name = bones[i].Name;
			if ( IsIgnoredRagdollBone( name ) )
				continue;

			if ( boneVertexCounts is not null && boneVertexCounts[i] < 4 )
				continue;

			// Skip a ground-level root bone when a real pelvis/hips child exists.
			if ( parentIndex[i] < 0 && name.Equals( "root", StringComparison.OrdinalIgnoreCase ) && children[i].Count > 0 )
				continue;

			kept.Add( i );
			keptSet.Add( i );
			if ( kept.Count >= 18 )
				break;
		}

		if ( kept.Count < 2 )
			return;

		// Map each kept bone to its body index and nearest kept ancestor so joints form one connected tree.
		var bodyIndex = new Dictionary<int, int>();
		for ( var i = 0; i < kept.Count; i++ )
			bodyIndex[kept[i]] = i;

		var height = Math.Clamp( totalBounds.Size.z, 24f, 240f );
		var totalMass = Math.Clamp( 60f * MathF.Pow( height / 64f, 2.2f ), 20f, 350f );
		var massPerBody = totalMass / kept.Count;
		var physSurface = Surface.FindByName( string.IsNullOrEmpty( surface ) ? "flesh" : surface );

		var bindPoses = new Transform[kept.Count];
		for ( var b = 0; b < kept.Count; b++ )
		{
			var bi = kept[b];
			var bone = bones[bi];
			var bind = new Transform( bone.Position, bone.Rotation );
			bindPoses[b] = bind;

			var body = builder.AddBody( massPerBody, physSurface, bone.Name )
				.SetBindPose( bind )
				.SetDamping( 0.4f, 1.5f );

			var radius = EstimateBoneRadius( bone.Name, height );

			// Aim capsule toward the primary kept child (or any skinned structural child); otherwise use a sphere.
			var targetChild = -1;
			var bestDist = 0f;
			foreach ( var ci in children[bi] )
			{
				if ( !keptSet.Contains( ci ) )
				{
					if ( IsIgnoredRagdollBone( bones[ci].Name ) )
						continue;
					if ( boneVertexCounts is not null && boneVertexCounts[ci] < 4 )
						continue;
				}

				var d = bone.Position.Distance( bones[ci].Position );
				if ( d > bestDist )
				{
					bestDist = d;
					targetChild = ci;
				}
			}

			if ( targetChild >= 0 && bestDist > 2.5f )
			{
				var localEnd = bind.PointToLocal( bones[targetChild].Position ) * 0.9f;
				var capRadius = Math.Clamp( radius, 2.5f, MathF.Max( 3.5f, bestDist * 0.45f ) );
				body.AddCapsule( new Capsule( Vector3.Zero, localEnd, capRadius ) );
			}
			else
			{
				var sphereRadius = Math.Clamp( radius, 2.5f, 18f );
				var offset = Vector3.Zero;
				if ( parentIndex[bi] >= 0 )
				{
					var fromParent = bone.Position - bones[parentIndex[bi]].Position;
					if ( fromParent.LengthSquared > 0.25f )
						offset = bind.PointToLocal( bone.Position + fromParent.Normal * (sphereRadius * 0.45f) );
				}
				body.AddSphere( new Sandbox.Sphere( offset, sphereRadius ) );
			}
		}

		// Connect every kept bone to its nearest kept ancestor (or body 0 if disconnected).
		var rootBody = 0;
		for ( var b = 1; b < kept.Count; b++ )
		{
			var bi = kept[b];
			var ancestor = parentIndex[bi];
			while ( ancestor >= 0 && !bodyIndex.ContainsKey( ancestor ) )
				ancestor = parentIndex[ancestor];

			var parentBody = ancestor >= 0 ? bodyIndex[ancestor] : rootBody;
			var childFrame = Transform.Zero;
			var parentFrame = bindPoses[parentBody].ToLocal( bindPoses[b] );
			var (swing, twist) = EstimateJointLimits( bones[bi].Name );
			var friction = massPerBody * 18f;

			builder.AddBallJoint( b, parentBody, childFrame, parentFrame, collision: false )
				.WithSwingLimit( swing )
				.WithTwistLimit( -twist, twist )
				.WithTargetVelocity( Vector3.Zero )
				.WithMaxTorque( friction );
		}

		builder.WithMass( totalMass );
	}

	private static bool IsIgnoredRagdollBone( string name )
	{
		foreach ( var token in IgnoredRagdollBones )
		{
			if ( name.Contains( token, StringComparison.OrdinalIgnoreCase ) )
				return true;
		}
		return false;
	}

	private static float EstimateBoneRadius( string name, float height )
	{
		if ( name.Contains( "hip", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "pelvis", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "spine", StringComparison.OrdinalIgnoreCase ) )
			return height * 0.11f;

		if ( name.Contains( "head", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "face", StringComparison.OrdinalIgnoreCase ) )
			return height * 0.10f;

		if ( name.Contains( "upleg", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "thigh", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "arm", StringComparison.OrdinalIgnoreCase ) )
			return height * 0.065f;

		if ( name.Contains( "hand", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "wrist", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "foot", StringComparison.OrdinalIgnoreCase ) )
			return height * 0.045f;

		return height * 0.055f;
	}

	private static (float Swing, float Twist) EstimateJointLimits( string name )
	{
		if ( name.Contains( "spine", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "neck", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "head", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "face", StringComparison.OrdinalIgnoreCase ) )
			return (35f, 25f);

		if ( name.Contains( "forearm", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "hand", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "wrist", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "foot", StringComparison.OrdinalIgnoreCase ) )
			return (55f, 20f);

		return (65f, 35f);
	}
}
