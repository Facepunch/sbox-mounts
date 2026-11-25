// Adapted from Overgrowth's CDrawTailImpl in movementobject.cpp.
// Copyright 2022 Wolfire Games LLC. Apache-2.0; see LICENSE.
using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	TailMotion tailMotion;

	sealed class TailMotion( int count )
	{
		public readonly Vector3[] Starts = new Vector3[count], Ends = new Vector3[count];
		public readonly Vector3[] LastStarts = new Vector3[count], LastEnds = new Vector3[count];
		public readonly Vector3[] Points = new Vector3[count + 1], Previous = new Vector3[count + 1];
		public readonly Vector3[] Correction = new Vector3[count + 1], Display = new Vector3[count + 1];
		public readonly float[] Length = new float[count];
		public bool Initialized;
		public float Remainder;

		public void Step( RabbitGuard guard, int frames, float fraction, bool reset )
		{
			Vector3 Start( int i ) => Vector3.Lerp( LastStarts[i], Starts[i], fraction );
			Vector3 End( int i ) => Vector3.Lerp( LastEnds[i], Ends[i], fraction );

			Previous[count] = reset ? Start( count - 1 ) : Points[count];
			Points[count] = Start( count - 1 );
			if ( reset )
			{
				for ( int i = count - 1; i >= 0; i-- )
				{
					Previous[i] = Points[i] = End( i );
					Length[i] = Points[i].Distance( Points[i + 1] );
				}
			}
			for ( int i = 0; i < count; i++ )
			{
				var point = Points[i];
				Points[i] += (point - Previous[i]) * 0.95f + Vector3.Down * (frames / 120f * 4f);
				Previous[i] = point;
			}
			Correction[count - 1] += (End( count - 1 ) - Points[count - 1]) * (1f - MathF.Pow( 0.9f, frames ));
			for ( int i = count - 2; i >= 0; i-- )
			{
				var bend = Rotation.FromToRotation( (End( i + 1 ) - Start( i + 1 )).Normal, (Points[i + 1] - Points[i + 2]).Normal );
				Correction[i] = (Points[i + 1] + bend * (End( i ) - End( i + 1 )) - Points[i]) * 0.1f;
				Correction[i + 1] -= Correction[i];
			}
			for ( int i = 0; i < count; i++ ) Points[i] += Correction[i];
			for ( int pass = 0; pass < Math.Max( 5, (int)(frames * 1.5f) ); pass++ )
			{
				Array.Clear( Correction );
				for ( int i = 0; i < count; i++ )
				{
					var middle = (Points[i] + Points[i + 1]) * 0.5f;
					var half = (Points[i] - Points[i + 1]).Normal * (Length[i] * 0.5f);
					Correction[i] += (middle + half - Points[i]) * 0.75f;
					Correction[i + 1] += (middle - half - Points[i + 1]) * 0.25f;
				}
				for ( int i = 0; i < count; i++ ) Points[i] += Correction[i] * Math.Min( 1f, 0.5f + pass * 0.125f );
			}
			for ( int i = count - 1; i >= 0; i-- ) Points[i] = guard.SweepSecondaryBone( Points[i + 1], Points[i] );
		}
	}

	void DrawTail( Transform model )
	{
		var tail = tailMotion;
		if ( tail is null ) return;
		int count = tailIndices.Length;
		for ( int i = 0; i < count; i++ )
		{
			var pose = model.ToWorld( solvedPose[tailIndices[i]] );
			var half = pose.Rotation.Forward * (boundAnimations.TailBones[i].Length * 0.5f * model.Scale.z);
			tail.Starts[i] = pose.Position - half;
			tail.Ends[i] = pose.Position + half;
		}

		int frames = SecondaryPoseFrames;
		float tick = frames / 120f;
		if ( !tail.Initialized || secondaryReset > 0f )
		{
			tail.Step( this, frames, 1f, true );
			tail.Initialized = true;
			tail.Remainder = 0f;
		}
		else
		{
			float elapsed = tail.Remainder + Time.Delta;
			int steps = (int)(elapsed / tick);
			for ( int i = 1; i <= steps; i++ )
				tail.Step( this, frames, Math.Clamp( (i * tick - tail.Remainder) / Time.Delta, 0f, 1f ), false );
			tail.Remainder = elapsed - steps * tick;
		}
		Array.Copy( tail.Starts, tail.LastStarts, count );
		Array.Copy( tail.Ends, tail.LastEnds, count );

		tail.Display[count] = tail.Starts[count - 1];
		for ( int i = count - 1; i >= 0; i-- )
		{
			var previous = tail.Previous[i] - tail.Previous[i + 1];
			var current = tail.Points[i] - tail.Points[i + 1];
			var direction = Vector3.Lerp( previous, current, tail.Remainder / tick ).Normal;
			float length = i == count - 1 ? tail.Starts[i].Distance( tail.Ends[i] ) : tail.Length[i];
			tail.Display[i] = tail.Display[i + 1] + direction * length;
			var pose = model.ToWorld( solvedPose[tailIndices[i]] );
			pose.Position = (tail.Display[i] + tail.Display[i + 1]) * 0.5f;
			pose.Rotation = Rotation.FromToRotation( pose.Rotation.Forward, direction ) * pose.Rotation;
			solvedPose[tailIndices[i]] = model.ToLocal( pose );
		}
	}
}
