using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Source1;

// IK as Source's bone setup runs it while posing a sequence. A rule holds the end bone of a chain relative
// to another bone, which is how hands stay on a weapon that is animated on a bone of its own; a release rule
// lets the chain go. A lock keeps an end bone where it was while a layer is added on top.
// Ground and attachment rules need the world around the model and are not solved.
internal sealed partial class MdlFile
{
	// mstudioikrule_t types
	private const int IkSelf = 1;
	private const int IkWorld = 2;
	private const int IkGround = 3;
	private const int IkRelease = 4;
	private const int IkAttachment = 5;
	private const int IkUnlatch = 6;

	// Source gives up on a chain stretched to within about a degree of straight.
	private const float StraightChain = 0.9998f;

	/// <summary>Three bones from the root joint to the end bone, in the owning MDL's bone indices. End is -1 for an unusable chain.</summary>
	internal readonly record struct IkChain( string Name, int Root, int Middle, int End, Vector3 KneeDirection );

	private IkChain[] ReadIkChains( byte[] data )
	{
		if ( _header.NumIkChains is < 0 or > 64 )
			throw new InvalidDataException( "Invalid IK chain count." );
		var chains = new IkChain[_header.NumIkChains];
		for ( var i = 0; i < chains.Length; i++ )
		{
			// mstudioikchain_t: name offset, link type, link count, link offset
			var offset = checked((int)(_header.IkChainOffset + i * 16L));
			var entry = data.AsSpan( offset, 16 );
			var name = ReadRelativeString( data, offset, BinaryPrimitives.ReadInt32LittleEndian( entry ) );
			chains[i] = new( name, -1, -1, -1, default );
			if ( BinaryPrimitives.ReadInt32LittleEndian( entry[8..] ) != 3 )
				continue;
			// mstudioiklink_t: bone, knee direction, unused vector
			var links = data.AsSpan( offset + BinaryPrimitives.ReadInt32LittleEndian( entry[12..] ), 3 * 28 );
			var root = BinaryPrimitives.ReadInt32LittleEndian( links );
			var middle = BinaryPrimitives.ReadInt32LittleEndian( links[28..] );
			var end = BinaryPrimitives.ReadInt32LittleEndian( links[56..] );
			if ( (uint)root < BoneCount && (uint)middle < BoneCount && (uint)end < BoneCount )
				chains[i] = new( name, root, middle, end, Vector96( links[4..] ) );
		}
		return chains;
	}

	// One mstudioikrule_t and where its error stream lives.
	private readonly record struct IkRuleData( byte[] Buffer, int RegionStart, int RegionLength, int Offset, int Type, int Chain, int Bone,
		int FirstFrame, int ErrorIndex, int CompressedIndex, float Start, float Peak, float Tail, float End )
	{
		public ReadOnlySpan<byte> Region => Buffer.AsSpan( RegionStart, RegionLength );
	}

	private IkRuleData? ReadIkRule( byte[] data, Func<byte[]> blocks, int animationIndex, StudioAnimDesc animation, int rule )
	{
		const int size = 152;
		var buffer = data;
		var start = 0;
		var length = data.Length;
		var descriptor = _header.LocalAnimIndex + (long)animationIndex * Unsafe.SizeOf<StudioAnimDesc>();
		long offset;
		if ( animation.IkRuleIndex != 0 )
		{
			offset = descriptor + animation.IkRuleIndex;
		}
		else if ( animation.AnimBlockIkRuleIndex == 0 )
		{
			return null;
		}
		else if ( animation.AnimBlock == 0 )
		{
			offset = descriptor + animation.AnimBlockIkRuleIndex;
		}
		else
		{
			if ( animation.AnimBlock < 0 || animation.AnimBlock >= _header.NumAnimBlocks )
				return null;
			var extent = data.AsSpan( checked((int)(_header.AnimBlockOffset + animation.AnimBlock * 8L)), 8 );
			buffer = blocks();
			start = BinaryPrimitives.ReadInt32LittleEndian( extent );
			length = BinaryPrimitives.ReadInt32LittleEndian( extent[4..] ) - start;
			if ( start < 0 || length < 0 || start + length > buffer.Length )
				throw new InvalidDataException( "Invalid animation block extent." );
			offset = animation.AnimBlockIkRuleIndex;
		}
		var at = checked((int)(offset + (long)rule * size));
		var entry = buffer.AsSpan( start, length ).Slice( at, size );
		return new( buffer, start, length, at,
			BinaryPrimitives.ReadInt32LittleEndian( entry[4..] ),
			BinaryPrimitives.ReadInt32LittleEndian( entry[8..] ),
			BinaryPrimitives.ReadInt32LittleEndian( entry[12..] ),
			BinaryPrimitives.ReadInt32LittleEndian( entry[68..] ),
			BinaryPrimitives.ReadInt32LittleEndian( entry[72..] ),
			BinaryPrimitives.ReadInt32LittleEndian( entry[60..] ),
			BinaryPrimitives.ReadSingleLittleEndian( entry[76..] ),
			BinaryPrimitives.ReadSingleLittleEndian( entry[80..] ),
			BinaryPrimitives.ReadSingleLittleEndian( entry[84..] ),
			BinaryPrimitives.ReadSingleLittleEndian( entry[88..] ) );
	}

	// Studio_IKRuleWeight: how strongly a rule applies at a cycle, ramping in to its peak and out from its tail.
	private static float IkRuleWeight( float start, float peak, float tail, float end, float cycle )
	{
		if ( end > 1 && cycle < start )
			cycle += 1;
		float value;
		if ( cycle < start )
			return 0;
		if ( cycle < peak )
			value = (cycle - start) / (peak - start);
		else if ( cycle < tail )
			return 1;
		else if ( cycle < end )
			value = 1 - (cycle - tail) / (end - tail);
		else
			return 0;
		return value * value * (3 - 2 * value);
	}

	// Studio_IKAnimationError: where a rule wants the end bone, in the space of the rule's bone.
	private static bool ReadIkError( IkRuleData rule, int frameCount, float cycle, out Vector3 position, out Rotation rotation )
	{
		position = default;
		rotation = Rotation.Identity;
		if ( rule.End > 1 && cycle < rule.Start )
			cycle += 1;
		var at = (frameCount - 1) * (MathF.Min( MathF.Max( cycle, rule.Start ), rule.End ) - rule.Start) + rule.FirstFrame;
		var frame = (int)at;
		var fraction = cycle < rule.Start ? 0 : at - frame;
		var region = rule.Region;
		if ( rule.ErrorIndex != 0 )
		{
			// mstudioikerror_t: position, quaternion
			var entry = region[(rule.Offset + rule.ErrorIndex + (frame - rule.FirstFrame) * 28)..];
			position = Vector96( entry );
			rotation = Quaternion128( entry[12..] );
			if ( fraction >= 0.001f )
			{
				position = Vector3.Lerp( position, Vector96( entry[28..] ), fraction );
				rotation = QuaternionBlend( rotation, Quaternion128( entry[40..] ), fraction );
			}
			return true;
		}
		if ( rule.CompressedIndex == 0 )
			return false;

		// mstudiocompressedikerror_t: six scales, then six offsets to RLE runs of position and Euler angles
		var compressed = rule.Offset + rule.CompressedIndex;
		Span<float> pair = stackalloc float[2];
		Span<float> first = stackalloc float[6];
		Span<float> second = stackalloc float[6];
		for ( var channel = 0; channel < 6; channel++ )
		{
			pair.Clear();
			int run = BinaryPrimitives.ReadInt16LittleEndian( region[(compressed + 24 + channel * 2)..] );
			if ( run > 0 )
				DecodeRun( region, compressed + run, frame - rule.FirstFrame, pair, BinaryPrimitives.ReadSingleLittleEndian( region[(compressed + channel * 4)..] ) );
			first[channel] = pair[0];
			second[channel] = pair[1];
		}
		position = new Vector3( first[0], first[1], first[2] );
		rotation = AngleQuaternion( first[3], first[4], first[5] );
		if ( fraction > 0.0001f )
		{
			position = Vector3.Lerp( position, new Vector3( second[0], second[1], second[2] ), fraction );
			rotation = QuaternionBlend( rotation, AngleQuaternion( second[3], second[4], second[5] ), fraction );
		}
		return true;
	}

	private static Rotation Quaternion128( ReadOnlySpan<byte> data ) => new(
		BinaryPrimitives.ReadSingleLittleEndian( data ),
		BinaryPrimitives.ReadSingleLittleEndian( data[4..] ),
		BinaryPrimitives.ReadSingleLittleEndian( data[8..] ),
		BinaryPrimitives.ReadSingleLittleEndian( data[12..] ) );

	private static Transform Compose( Transform parent, Transform local )
		=> new( parent.Position + parent.Rotation * local.Position, (parent.Rotation * local.Rotation).Normal );

	private static Transform Relative( Transform parent, Transform world )
	{
		var inverse = parent.Rotation.Inverse;
		return new( inverse * (world.Position - parent.Position), (inverse * world.Rotation).Normal );
	}

	// A rotation from the axes it turns X, Y and Z into.
	private static Rotation FromAxes( Vector3 x, Vector3 y, Vector3 z )
	{
		var trace = x.x + y.y + z.z;
		if ( trace > 0 )
		{
			var s = MathF.Sqrt( trace + 1 ) * 2;
			return new Rotation( (y.z - z.y) / s, (z.x - x.z) / s, (x.y - y.x) / s, s / 4 );
		}
		if ( x.x > y.y && x.x > z.z )
		{
			var s = MathF.Sqrt( 1 + x.x - y.y - z.z ) * 2;
			return new Rotation( s / 4, (y.x + x.y) / s, (z.x + x.z) / s, (y.z - z.y) / s );
		}
		if ( y.y > z.z )
		{
			var s = MathF.Sqrt( 1 + y.y - x.x - z.z ) * 2;
			return new Rotation( (y.x + x.y) / s, s / 4, (z.y + y.z) / s, (z.x - x.z) / s );
		}
		else
		{
			var s = MathF.Sqrt( 1 + z.z - x.x - y.y ) * 2;
			return new Rotation( (z.x + x.z) / s, (z.y + y.z) / s, s / 4, (x.y - y.x) / s );
		}
	}

	// Studio_AlignIKMatrix: points the bone's X axis along a direction, keeping its Z axis as near as it can.
	private static Rotation AlignBone( Rotation rotation, Vector3 direction )
	{
		var x = direction.Normal;
		var y = Vector3.Cross( rotation * new Vector3( 0, 0, 1 ), x ).Normal;
		return FromAxes( x, y, Vector3.Cross( x, y ) ).Normal;
	}

	// Studio_SolveIK: bends a three-bone chain so its end bone reaches the target, keeping the middle joint
	// on the side it is on, or on the side the chain's knee direction names. Only the three world transforms change.
	private static bool SolveIkChain( IkChain chain, Vector3 target, Transform[] world, Vector3? kneePosition = null, Vector3? kneeDirection = null )
	{
		var root = world[chain.Root].Position;
		var middle = world[chain.Middle].Position;
		var end = world[chain.End].Position;
		var upper = (middle - root).Length;
		var lower = (end - middle).Length;
		if ( upper < 0.0001f || lower < 0.0001f )
			return false;

		Vector3 knee;
		Vector3 direction;
		if ( kneeDirection is { } lockedDirection )
		{
			knee = (kneePosition ?? middle) - root;
			direction = lockedDirection;
		}
		else if ( chain.KneeDirection.LengthSquared > 0 )
		{
			knee = middle - root;
			direction = world[chain.Root].Rotation * chain.KneeDirection;
		}
		else
		{
			var reach = (end - root).Length;
			// Too straight to tell which way the joint bends.
			if ( reach > (upper + lower) * StraightChain || reach < 0.0001f )
				return false;
			knee = middle - root;
			direction = (knee - (end - root) * (upper / reach)).Normal;
		}

		var foot = target - root;
		// Exaggerate the knee target so nearly straight chains still bend the right way.
		var kneeTarget = knee + direction * (MathF.Max( upper + lower, foot.Length - MathF.Min( upper, lower ) ) * 100);
		if ( foot.Length > (upper + lower) * StraightChain )
			foot = foot.Normal * ((upper + lower) * StraightChain);
		var nearest = MathF.Max( MathF.Abs( upper - lower ) * 1.15f, MathF.Min( upper, lower ) * 0.15f );
		if ( foot.Length < nearest )
			foot = (end - root).Normal * nearest;

		// Two-link solve: the joint lies at distance `along` on the line to the target and `aside` towards the knee target.
		var reachLength = foot.Length;
		var x = foot / reachLength;
		var y = (kneeTarget - x * Vector3.Dot( kneeTarget, x )).Normal;
		var along = (reachLength + (upper * upper - lower * lower) / reachLength) / 2;
		if ( !(along > reachLength - lower && along < upper) )
			return false;
		var aside = MathF.Sqrt( upper * upper - along * along );
		var joint = x * along + y * aside;

		world[chain.Root] = new Transform( root, AlignBone( world[chain.Root].Rotation, joint ) );
		world[chain.Middle] = new Transform( root + joint, AlignBone( world[chain.Middle].Rotation, foot - joint ) );
		world[chain.End] = new Transform( root + foot, world[chain.End].Rotation );
		return true;
	}

	/// <summary>An IK rule gathered while posing: where a chain's end bone should be, relative to a master bone.</summary>
	private readonly record struct IkRule( int Type, int Chain, int Bone, Vector3 Position, Rotation Rotation, float Weight, float RuleWeight );

	private sealed partial class AnimationImporter
	{
		// The master model's usable IK chains.
		private IkChain[] chains;

		private List<IkRule>[] NewIkContext()
		{
			if ( chains.Length == 0 )
				return null;
			var context = new List<IkRule>[chains.Length];
			for ( var i = 0; i < context.Length; i++ )
			{
				context[i] = [];
			}
			return context;
		}

		// A chain of an included MDL names a chain of the master model.
		private int MasterChain( AnimationSource source, int chain )
		{
			if ( (uint)chain >= source.File.IkChains.Length )
				return -1;
			if ( source == sources[0] )
				return chains[chain].End < 0 ? -1 : chain;
			var name = source.File.IkChains[chain].Name;
			return Array.FindIndex( chains, x => x.End >= 0 && x.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) );
		}

		// The animations a sequence blends at the pose parameters, with their weights: Studio_SeqAnims.
		private List<(int Animation, float Weight)> BlendedAnimations( AnimationSource source, int index, (int X, int Y)? cell, Dictionary<string, float> parameters )
		{
			var x = cell is { } pinned ? (Index: pinned.X, Weight: 0f) : LocalPoseParameter( source, index, 0, parameters );
			var y = cell is { } pinnedRow ? (Index: pinnedRow.Y, Weight: 0f) : LocalPoseParameter( source, index, 1, parameters );
			var result = new List<(int, float)>( 4 );
			for ( var corner = 0; corner < 4; corner++ )
			{
				var weight = ((corner & 1) == 0 ? 1 - x.Weight : x.Weight) * ((corner & 2) == 0 ? 1 - y.Weight : y.Weight);
				if ( weight > 0 )
					result.Add( (source.Animation( index, x.Index + (corner & 1), y.Index + (corner >> 1) ), weight) );
			}
			return result;
		}

		// CIKContext::AddDependencies: gathers the sequence's IK rules at a cycle. A rule at full weight
		// replaces what the chain had; a full release just clears it.
		private void AddIkRules( List<IkRule>[] context, AnimationSource source, int index, float cycle, float weight, (int X, int Y)? cell, Dictionary<string, float> parameters )
		{
			var sequence = source.Sequences[index];
			if ( context is null || sequence.NumIkRules is <= 0 or > 64 )
				return;
			if ( !float.IsFinite( cycle ) )
				cycle = 0;
			if ( cycle < 0 || cycle >= 1 )
				cycle = (sequence.Flags & SequenceLooping) != 0 ? cycle - MathF.Floor( cycle ) : Math.Clamp( cycle, 0, 0.9999f );
			try
			{
				var animations = BlendedAnimations( source, index, cell, parameters );
				for ( var i = 0; i < sequence.NumIkRules; i++ )
				{
					if ( !SequenceIkRule( source, animations, i, cycle, out var rule ) )
						continue;
					var chain = MasterChain( source, rule.Chain );
					if ( chain < 0 )
						continue;
					var bone = (uint)rule.Bone < source.MasterBone.Length ? source.MasterBone[rule.Bone] : -1;
					if ( rule.Type == IkSelf && rule.Bone >= 0 && bone < 0 )
						continue;
					rule = rule with { Chain = chain, Bone = bone, RuleWeight = weight };
					if ( rule.RuleWeight * rule.Weight > 0.999f && rule.Type != IkUnlatch )
					{
						context[chain].Clear();
						if ( rule.Type == IkRelease )
							continue;
					}
					context[chain].Add( rule );
				}
			}
			catch ( Exception e ) when ( IsDataError( e ) )
			{
				// Invalid IK data leaves the animated pose as it is.
			}
		}

		// Studio_IKSequenceError: one rule blended over the sequence's animations.
		private bool SequenceIkRule( AnimationSource source, List<(int Animation, float Weight)> animations, int index, float cycle, out IkRule result )
		{
			result = default;
			var rules = new (IkRuleData Rule, int FrameCount, float Weight)[animations.Count];
			float start = 0, peak = 0, tail = 0, end = 0, previousStart = 0;
			var ruleCount = -1;
			for ( var i = 0; i < rules.Length; i++ )
			{
				var animation = source.Mdl.ReadAnimationDescriptor( source.Data, animations[i].Animation );
				if ( ruleCount < 0 )
					ruleCount = animation.NumIkRules;
				if ( index >= animation.NumIkRules || animation.NumIkRules != ruleCount )
					return false;
				if ( source.Mdl.ReadIkRule( source.Data, source.File.Blocks, animations[i].Animation, animation, index ) is not { } rule )
					return false;
				rules[i] = (rule, animation.NumFrames, animations[i].Weight);
				// Rules of blended animations may start on either side of the loop point.
				var shift = 0f;
				if ( previousStart != 0 )
					shift = rule.Start - previousStart > 0.5f ? -1 : rule.Start - previousStart < -0.5f ? 1 : 0;
				else
					previousStart = rule.Start;
				start += (rule.Start + shift) * animations[i].Weight;
				peak += (rule.Peak + shift) * animations[i].Weight;
				tail += (rule.Tail + shift) * animations[i].Weight;
				end += (rule.End + shift) * animations[i].Weight;
			}
			if ( rules.Length == 0 )
				return false;
			if ( start > 1 )
			{
				start -= 1;
				peak -= 1;
				tail -= 1;
				end -= 1;
			}
			var weight = IkRuleWeight( start, peak, tail, end, cycle );
			if ( !(weight > 0.001f) )
				return false;

			var position = Vector3.Zero;
			float x = 0, y = 0, z = 0, w = 0;
			foreach ( var (rule, frameCount, ruleWeight) in rules )
			{
				if ( rule.Type is not (IkSelf or IkWorld or IkGround or IkAttachment) )
					continue;
				if ( !ReadIkError( rule, frameCount, cycle, out var p, out var q ) )
					return false;
				position += p * ruleWeight;
				var sign = x * q.x + y * q.y + z * q.z + w * q.w < 0 ? -ruleWeight : ruleWeight;
				x += q.x * sign;
				y += q.y * sign;
				z += q.z * sign;
				w += q.w * sign;
			}
			var length = MathF.Sqrt( x * x + y * y + z * z + w * w );
			var rotation = length > 0.0001f ? new Rotation( x / length, y / length, z / length, w / length ) : Rotation.Identity;
			result = new( rules[0].Rule.Type, rules[0].Rule.Chain, rules[0].Rule.Bone, position, rotation, weight, 1 );
			return float.IsFinite( position.x ) && float.IsFinite( position.y ) && float.IsFinite( position.z );
		}

		private Transform[] WorldPose( Transform[] pose )
		{
			var world = new Transform[pose.Length];
			var done = new bool[pose.Length];
			void Build( int bone, int depth )
			{
				if ( done[bone] )
					return;
				done[bone] = true;
				var parent = master.Bones[bone].Parent;
				if ( (uint)parent >= pose.Length || depth > pose.Length )
				{
					world[bone] = new Transform( pose[bone].Position, pose[bone].Rotation );
					return;
				}
				Build( parent, depth + 1 );
				world[bone] = Compose( world[parent], pose[bone] );
			}
			for ( var bone = 0; bone < pose.Length; bone++ )
			{
				Build( bone, 0 );
			}
			return world;
		}

		// SolveBone: a bone's parent-space transform from the world transforms.
		private void WriteBone( Transform[] pose, Transform[] world, int bone )
		{
			var parent = master.Bones[bone].Parent;
			pose[bone] = (uint)parent < pose.Length ? Relative( world[parent], world[bone] ) : world[bone];
		}

		// CIKContext::SolveDependencies: once every layer is in, moves each chain with rules to its target.
		private void SolveIk( Transform[] pose, List<IkRule>[] context )
		{
			if ( context is null || context.All( x => x.Count == 0 ) )
				return;
			var world = WorldPose( pose );
			var targets = new Transform[chains.Length];
			var weights = new float[chains.Length];
			for ( var chain = 0; chain < chains.Length; chain++ )
			{
				if ( chains[chain].End < 0 )
					continue;
				targets[chain] = world[chains[chain].End];
				foreach ( var rule in context[chain] )
				{
					var weight = rule.Weight * rule.RuleWeight;
					Transform goal;
					if ( rule.Type == IkSelf )
					{
						var local = new Transform( rule.Position, rule.Rotation );
						goal = rule.Bone >= 0 ? Compose( world[rule.Bone], local ) : local;
						weights[chain] = weights[chain] * (1 - weight) + weight;
					}
					else if ( rule.Type == IkRelease )
					{
						// Moves the target back towards where the animation has the bone.
						goal = world[chains[chain].End];
					}
					else
					{
						continue;
					}
					targets[chain] = new Transform( Vector3.Lerp( targets[chain].Position, goal.Position, weight ),
						Rotation.Slerp( targets[chain].Rotation, goal.Rotation, weight ) );
				}
			}
			for ( var chain = 0; chain < chains.Length; chain++ )
			{
				if ( weights[chain] <= 0 || !SolveIkChain( chains[chain], targets[chain].Position, world ) )
					continue;
				world[chains[chain].End] = new Transform( world[chains[chain].End].Position, targets[chain].Rotation );
				WriteBone( pose, world, chains[chain].End );
				WriteBone( pose, world, chains[chain].Middle );
				WriteBone( pose, world, chains[chain].Root );
			}
		}

		private readonly record struct IkLock( int Chain, float PositionWeight, float RotationWeight, Transform End, Vector3 KneePosition, Vector3 KneeDirection );

		// CIKContext::AddSequenceLocks: remembers where the locked chains' end bones are before a layer is added.
		private IkLock[] RecordIkLocks( AnimationSource source, int index, Transform[] pose )
		{
			var sequence = source.Sequences[index];
			if ( sequence.NumIkLocks is <= 0 or > 64 || chains.Length == 0 )
				return null;
			var world = WorldPose( pose );
			var locks = new List<IkLock>();
			for ( var i = 0; i < sequence.NumIkLocks; i++ )
			{
				// mstudioiklock_t: chain, position weight, local rotation weight, flags
				var entry = source.SequenceData( index, sequence.IkLockIndex + i * 32L, 32 );
				var chain = MasterChain( source, BinaryPrimitives.ReadInt32LittleEndian( entry ) );
				if ( chain < 0 )
					continue;
				var knee = chains[chain].KneeDirection.LengthSquared > 0 ? world[chains[chain].Root].Rotation * chains[chain].KneeDirection : Vector3.Zero;
				locks.Add( new( chain, BinaryPrimitives.ReadSingleLittleEndian( entry[4..] ), BinaryPrimitives.ReadSingleLittleEndian( entry[8..] ),
					world[chains[chain].End], world[chains[chain].Middle].Position, knee ) );
			}
			return locks.Count > 0 ? locks.ToArray() : null;
		}

		// CIKContext::SolveSequenceLocks: after the layer, pulls each locked end bone back to where it was.
		private void SolveIkLocks( Transform[] pose, IkLock[] locks )
		{
			foreach ( var ikLock in locks )
			{
				var chain = chains[ikLock.Chain];
				var world = WorldPose( pose );
				var target = Vector3.Lerp( world[chain.End].Position, ikLock.End.Position, Math.Clamp( ikLock.PositionWeight, 0, 1 ) );
				if ( ikLock.KneeDirection.LengthSquared > 0 )
					SolveIkChain( chain, target, world, ikLock.KneePosition, ikLock.KneeDirection );
				else
					SolveIkChain( chain, target, world );
				// The end bone takes the orientation it had, then eases back to its animated local rotation.
				var animated = pose[chain.End].Rotation;
				world[chain.End] = new Transform( world[chain.End].Position, ikLock.End.Rotation );
				WriteBone( pose, world, chain.End );
				pose[chain.End].Rotation = Rotation.Slerp( pose[chain.End].Rotation, animated, Math.Clamp( ikLock.RotationWeight, 0, 1 ) );
				WriteBone( pose, world, chain.Middle );
				WriteBone( pose, world, chain.Root );
			}
		}

		// A chain is held when, with the whole sequence posed, one rule at full weight is all it has.
		private static bool HoldsChain( List<IkRule> rules, out IkRule rule )
		{
			rule = rules.Count == 1 ? rules[0] : default;
			return rules.Count == 1 && rule.Type == IkSelf && rule.Bone >= 0 && rule.Weight * rule.RuleWeight > 0.999f;
		}

		private string IkBoneName( (int Chain, int Bone) target ) => $"ik_target:{chains[target.Chain].Name}:{master._boneNames[target.Bone]}";

		// What the sequence holds with IK, for a consumer that layers sequences at runtime and has to solve
		// them on the final pose as Source does. Each target is a bone added to the skeleton as a child of the
		// bone the rule is relative to; the clips animate it to where the chain's end bone belongs.
		private ModelSequenceData.IkTarget[] IkTargets( AnimationSource source, int index )
		{
			if ( chains.Length == 0 )
				return [];
			// Only a hold that lasts the whole sequence is offered: a consumer cannot tell when a hand lets go.
			var held = new int[chains.Length];
			foreach ( var cycle in new[] { 0f, 0.25f, 0.5f, 0.75f, 0.9999f } )
			{
				var context = NewIkContext();
				Accumulate( null, source, index, cycle, 1, 0, null, NoParameters, null, context );
				for ( var chain = 0; chain < held.Length; chain++ )
				{
					var bone = HoldsChain( context[chain], out var rule ) ? rule.Bone : -1;
					held[chain] = cycle == 0 || held[chain] == bone ? bone : -1;
				}
				if ( held.All( x => x < 0 ) )
					return [];
			}
			var targets = new List<ModelSequenceData.IkTarget>();
			for ( var chain = 0; chain < held.Length; chain++ )
			{
				if ( held[chain] < 0 )
					continue;
				if ( !ikBones.Contains( (chain, held[chain]) ) )
					ikBones.Add( (chain, held[chain]) );
				targets.Add( new() { Bone = master._boneNames[chains[chain].End], TargetBone = IkBoneName( (chain, held[chain]) ) } );
			}
			return targets.ToArray();
		}

		// A frame with the IK target bones behind the model's own: each sits where its rule puts the chain's
		// end bone, in the space of the bone it is a child of.
		private Transform[] WithIkBones( Transform[] pose, List<IkRule>[] context )
		{
			if ( ikBones.Count == 0 )
				return pose;
			var frame = new Transform[pose.Length + ikBones.Count];
			pose.CopyTo( frame, 0 );
			for ( var i = 0; i < ikBones.Count; i++ )
			{
				frame[pose.Length + i] = context is not null && HoldsChain( context[ikBones[i].Chain], out var rule ) && rule.Bone == ikBones[i].Bone
					? new Transform( rule.Position, rule.Rotation )
					: Transform.Zero;
			}
			return frame;
		}
	}
}
