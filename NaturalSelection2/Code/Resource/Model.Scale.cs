using System;
using System.Numerics;

namespace NS2;

public partial class ModelLoader
{
	sealed class ScaleTrack( Curve<Vector3> stretch, Curve<float> uniform )
	{
		Curve<Quaternion> orientation;
		Matrix4x4? constant;

		public void SetOrientation( Curve<Quaternion> value )
		{
			orientation = value;
			if ( Array.TrueForAll( stretch.Values, v => (v - stretch.Values[0]).LengthSquared < 1e-10f ) &&
				Array.TrueForAll( uniform.Values, v => MathF.Abs( v - uniform.Values[0] ) < 1e-5f ) &&
				Array.TrueForAll( value.Values, q => MathF.Abs( Quaternion.Dot( q, value.Values[0] ) ) > 0.9999999f ) )
				constant = Sample( 0 );
		}

		public Matrix4x4 Sample( float time )
		{
			if ( constant is { } matrix ) return matrix;
			var scale = stretch.Values.Length == 0 ? Vector3.One : SampleVector3( stretch, time );
			if ( uniform.Values.Length > 0 )
			{
				int hi = LowerBound( uniform.Times, time );
				float factor = uniform.Values[hi];
				if ( hi > 0 && time < uniform.Times[hi] )
					factor = MathX.Lerp( uniform.Values[hi - 1], factor, (time - uniform.Times[hi - 1]) / (uniform.Times[hi] - uniform.Times[hi - 1]) );
				scale *= factor;
			}
			return StretchMatrix( scale, SampleRotation( orientation, time ) );
		}
	}

	// The common case is a unit scale track. Scan its values in the reader's buffer
	// before allocating any key arrays, and only decode tracks that affect a pose.
	static ScaleTrack ReadScaleTrack( ref Reader r )
	{
		long start = r.Offset;
		int vectors = r.Int();
		r.Skip( (long)vectors * sizeof( float ) );
		bool scaled = false;
		for ( int i = 0; i < vectors; i++ )
			scaled |= (r.ReadVector3() - Vector3.One).LengthSquared > 1e-8f;
		int scalars = r.Int();
		r.Skip( (long)scalars * sizeof( float ) );
		for ( int i = 0; i < scalars; i++ )
			scaled |= MathF.Abs( r.Float() - 1 ) > 0.0001f;
		if ( !scaled ) return null;

		r.Seek( start );
		var stretch = ReadVector3Curve( ref r );
		int keys = r.Int();
		return new( stretch, new() { Times = r.Floats( keys ), Values = r.Floats( keys ) } );
	}

	static List<int> ScaledBones( Bone[] bones, ScaleTrack[] scales )
	{
		var affected = new bool[bones.Length];
		var indices = new List<int>();
		for ( int b = 0; b < bones.Length; b++ )
		{
			int parent = bones[b].Parent;
			affected[b] = scales[b] is not null || parent >= 0 && affected[parent];
			if ( affected[b] ) indices.Add( b );
		}
		return indices;
	}

	static Matrix4x4 StretchMatrix( Vector3 scale, Rotation orientation )
	{
		if ( (scale - Vector3.One).LengthSquared < 1e-8f ) return Matrix4x4.Identity;
		var basis = Matrix4x4.CreateFromQuaternion( ConvertRotation( orientation ) );
		return Matrix4x4.Transpose( basis ) * Matrix4x4.CreateScale( ConvertDirection( scale ) ) * basis;
	}

	// AG2 uses uniform bone scale. Carry Spark's stretch down the hierarchy so each
	// child joint keeps its authored position even when its parent stretches along one axis.
	static void BakeBoneScale( ref Transform pose, in Bone source, int bone, Matrix4x4 stretch, Matrix4x4?[] residuals )
	{
		if ( source.Parent >= 0 && residuals[source.Parent] is { } inherited )
		{
			pose.Position = System.Numerics.Vector3.Transform( pose.Position, inherited );
			if ( !source.HasChildren ) return;
			var rotation = Matrix4x4.CreateFromQuaternion( pose.Rotation );
			stretch *= rotation * inherited * Matrix4x4.Transpose( rotation );
		}
		residuals[bone] = stretch.IsIdentity ? null : stretch;
	}
}
