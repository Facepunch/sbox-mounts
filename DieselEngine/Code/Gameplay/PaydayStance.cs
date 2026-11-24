namespace Diesel;

static class PaydayStance
{
	// Original standing shoulder/head pivots (Diesel centimetres and yaw/pitch/roll).
	public static Transform Get( string id, bool aiming = false )
	{
		(Vector3 shoulder, Vector3 shoulderAngles, Vector3 head, Vector3 headAngles) pivots = id switch
		{
			"b9s" => (new(8.27388f,54.885f,-1.42176f),new(-.838946f,.825787f,-4.85158f),new(5,29,-2),new(-2,-1,0)),
			"crosskill" => (new(8.59479f,54.7405f,-.624365f),new(-.807596f,1.47504f,-1.76123f),new(5,29,-2),new(-2,-1,0)),
			"bronco44" => (new(9.36965f,58.7379f,-1.8196f),new(-1.71169f,.000728827f,.0000877357f),new(5,35,-2),new(0,0,0)),
			"stryk" => (new(8.73016f,54.6357f,-2.2865f),new(-.807598f,1.47537f,-1.76124f),new(5,29,-2),new(-2,-1,0)),
			"reinbeck" => (new(8.08536f,37.3747f,-3.28995f),new(-.835943f,.75668f,-.12396f),new(5,20,-2),new(-2,-2,0)),
			"locomotive" => (new(8.08536f,37.3747f,-3.28995f),new(-.835943f,.75668f,-.12396f),new(6,15,-6),new(0,0,2)),
			"compact5" => (new(10.4541f,35.4915f,-2.955f),new(-.286163f,.214701f,.878467f),new(6,17,-.5f),new(0,-2,0)),
			"mark11" => (new(7.48993f,40.865f,-1.38648f),new(-.752184f,-.37808f,2.04525f),new(10,20,-1.7f),new(0,-1,0)),
			"m308" => (new(8.40396f,33.2866f,-.763476f),new(.151441f,.645593f,-1.19106f),new(7,16,-.5f),new(0,1,0)),
			"brenner21" => (new(17.7884f,25.4958f,-5.87593f),new(-15.9596f,-1.5637f,-1.19957f),new(7,15,-3.5f),new(0,0,0)),
			"ak" => (new(8.61463f,33.4594f,-3.75638f),new(.15141f,.645553f,-1.19108f),new(6,15,-.5f),new(0,0,0)),
			"gl40" => (new(8.58595f,40.5661f,-2.41104f),new(.143334f,1.04565f,-1.1923f),new(5.2f,16,-.5f),new(0,1,0)),
			_ => (new(8.40486f,33.2872f,-.76365f),new(.151694f,.644497f,-1.1892f),new(7,16,-.5f),new(0,1,0))
		};
		if ( aiming )
		{
			pivots.head = new Vector3( 0, id switch { "compact5" => 10, "mark11" => 16, "reinbeck" => 15, "locomotive" => 25,
				"b9s" or "crosskill" or "stryk" => 30, "bronco44" => 28, "ak" => 1, _ => 7 }, 0 );
			pivots.headAngles = id == "locomotive" ? new Vector3( 0, -2, 0 ) : Vector3.Zero;
		}
		var rotation = Convert( pivots.headAngles ) * Convert( pivots.shoulderAngles ).Inverse;
		return new Transform( Coordinates.Position( pivots.head ) - rotation * Coordinates.Position( pivots.shoulder ), rotation );
	}
	static Rotation Convert( Vector3 angles ) => Rotation.From( -angles.y, angles.x, angles.z );
}
