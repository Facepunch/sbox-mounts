namespace Overgrowth;

public sealed class ClashFlash : Component
{
	PointLight light;
	TimeSince age;
	float characterStep;

	internal static void Emit( Vector3 point, float radius, int characterFrames )
	{
		var go = new GameObject( false, "Weapon clash flash" ) { WorldPosition = point, NetworkMode = NetworkMode.Never };
		var flash = go.AddComponent<ClashFlash>();
		flash.light = go.AddComponent<PointLight>();
		flash.light.Radius = radius;
		flash.light.LightColor = Color.White * 0.5f;
		flash.light.Shadows = false;
		flash.characterStep = characterFrames / 120f;
		flash.age = 0f;
		go.Enabled = true;
	}

	protected override void OnUpdate()
	{
		if ( !light.IsValid() || characterStep <= 0f ) { Destroy(); return; }
		float brightness = 0.5f - (int)(age / characterStep) * (15f / 120f);
		if ( brightness <= 0f ) GameObject.Destroy();
		else light.LightColor = Color.White * brightness;
	}
}
