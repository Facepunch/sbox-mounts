using System;

namespace Overgrowth;

internal sealed class LevelLoader( string levelPath ) : SceneLoader<GameMount>
{
	protected override void BuildScene()
	{
		var level = XmlAsset.Parse( Host.FileSystem.ReadAllText( levelPath ) );
		if ( level.Element( "Terrain" ) is { } terrain && terrain.Element("Heightmap") is not null )
		{
			var model = Model.Load( Host.MountedPath(levelPath) + ".vmdl" );
			if ( model is { IsError: false } ) AddModel( new GameObject(true,"Terrain"),model,true );
		}
		if ( level.Element("Sky")?.Element("RayToSun") is { } sun )
		{
			var go = new GameObject(true,"Sun");
			var ray = Vector(sun,"r",0);
			if (!ray.IsNearlyZero()) go.WorldRotation = Rotation.LookAt(-ray.Normal);
			go.AddComponent<DirectionalLight>().SkyColor = Color.White * 0.35f;
		}
		foreach(var child in level.Children) Place(child);
	}

	void Place(XmlAsset node)
	{
		if(node.Name is "Group" or "Prefab" || node.Attribute("t0") is null)
		{
			foreach(var child in node.Children) Place(child);
			return;
		}
		var file=node.Attribute("type_file");
		GameObject Object() => new GameObject(true,node.Attribute("name") ?? System.IO.Path.GetFileNameWithoutExtension(file) ?? node.Name)
		{
			WorldTransform=new Transform(Vector(node,"t",0)*40,Orientation(node),Vector(node,"s",1))
		};
		switch(node.Name)
		{
			case "SpawnPoint":
				Object().AddComponent<SpawnPoint>();
				return;
			case "ActorObject" when node.Attribute("is_player")=="1":
				Object().AddComponent<SpawnPoint>();
				return;
			case "DynamicLightObject":
				var light=Object().AddComponent<PointLight>();
				light.Radius=(node.Number("s0")??1)*40;
				light.LightColor=new Color(node.Number("color_r")??1,node.Number("color_g")??1,node.Number("color_b")??1);
				return;
			case "ActorObject" when string.Equals( file, RabbitGuard.ActorPath, StringComparison.OrdinalIgnoreCase ):
				var guard = Model.Load( Host.MountedPath( RabbitGuard.ModelName ) + ".vmdl" );
				if ( guard is { IsError: false } )
					RabbitGuardPrefab.Configure( Object(), guard );
				return;
			case "ActorObject":
				if(string.IsNullOrEmpty(file)||!Host.FileSystem.FileExists(file))return;
				file=XmlAsset.Read(Host.FileSystem,file,"Actor").Element("Character")?.Value;
				break;
			case "EnvObject":
			case "ItemObject":
				break;
			default: return;
		}
		if(string.IsNullOrEmpty(file)) return;
		var model=Model.Load(Host.MountedPath(file)+".vmdl");
		if(model is { IsError:false }) AddModel(Object(),model,node.Name=="EnvObject");
	}

	static void AddModel(GameObject go,Model model,bool solid)
	{
		go.AddComponent<ModelRenderer>().Model=model;
		if(!solid || model.Physics is null || model.Physics.Parts.Count==0)return;
		go.Tags.Add("world");
		var collider=go.AddComponent<ModelCollider>();
		collider.Model=model;
		collider.Static=true;
	}

	static Vector3 Vector(XmlAsset n,string prefix,float fallback) => new(n.Number(prefix+"2")??fallback,n.Number(prefix+"0")??fallback,n.Number(prefix+"1")??fallback);

	static Rotation Orientation(XmlAsset n)
	{
		if(n.Attribute("q3") is not null) return new Rotation(n.Number("q2")??0,n.Number("q0")??0,n.Number("q1")??0,n.Number("q3")??1);
		if(n.Attribute("r0") is null)return Rotation.Identity;
		return Rotation.LookAt(new(n.Number("r10")??1,n.Number("r8")??0,n.Number("r9")??0),new(n.Number("r6")??0,n.Number("r4")??0,n.Number("r5")??1));
	}
}
