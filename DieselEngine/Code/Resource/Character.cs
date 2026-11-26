using System.Text;

namespace Diesel;
using Matrix = System.Numerics.Matrix4x4;

/// <summary>Authored character skeleton and skin, shared by browser resources and NPC appearances.</summary>
sealed class PaydayCharacter
{
	public ModelFile File { get; }
	public Dictionary<string, AnimationFile> Clips { get; }
	public (uint Id, string Name, uint Parent)[] Bones { get; }
	public Dictionary<uint, Transform> Bind { get; }
	public Model Model { get; }

	public PaydayCharacter( BundleDatabase db, MapMaterials materials, NpcDefinition definition, string path )
		: this( db, materials, definition.Unit, path, definition.Clips(), true ) { }

	public PaydayCharacter( BundleDatabase db, MapMaterials materials, string unitPath, string path )
		: this( db, materials, unitPath, path, [], false ) { }

	PaydayCharacter( BundleDatabase db, MapMaterials materials, string unitPath, string path,
		IEnumerable<KeyValuePair<string, string>> clips, bool npcPhysics )
	{
		var unit = UnitModel.Xml( db.Find( "unit", unitPath ) );
		string objectPath = unit.Element( "object" ).Attribute( "file" );
		var obj = UnitModel.Xml( db.Find( "object", objectPath ) );
		File = new( db.Find( "model", objectPath ) );
		var names = Encoding.Latin1.GetString( db.Find( "idstring_lookup", "idstring_lookup" ) ).Split( '\0' )
			.Distinct().GroupBy( IdString.Hash ).ToDictionary( g => g.Key, g => g.First() );
		Bones = File.RigNodes.Select( n => (n.Id, names.GetValueOrDefault( n.Name, $"bone_{n.Name:x}" ), n.Parent) ).ToArray();
		if ( Bones.Length > 255 ) throw new InvalidDataException( "Character exceeds byte skin index limit." );
		Clips = clips.ToDictionary( c => c.Key, c => new AnimationFile( db.Find( "animation", c.Value )
			?? throw new InvalidDataException( $"Missing character animation: {c.Value}" ) ) );
		var pose = File.Pose( null, 0 );
		Matrix.Invert( File.NodePose( pose, "root_point" ), out var root );
		Bind = pose.ToDictionary( p => p.Key, p => ModelFile.Convert( p.Value * root ) );
		var builder = Sandbox.Model.Builder.WithName( path );
		foreach ( var bone in Bones )
		{
			// Mesh-bearing runtime models expect model-space bind bones (same as the Source mount).
			var local = Bind[bone.Id];
			builder.AddBone( bone.Name, local.Position, local.Rotation, new BBox( new Vector3( -10 ), new Vector3( 10 ) ),
				bone.Parent == 0 ? null : Bones.First( b => b.Id == bone.Parent ).Name );
		}
		var indices = Bones.Select( (b, i) => (b.Id, Index: (byte)i) ).ToDictionary( b => b.Id, b => b.Index );
		string config = obj.Element( "diesel" ).Attribute( "materials" );
		foreach ( var g in File.ReadGeometry( UnitGeometry.Read( unit, obj ).Graphics, 0, raw: true ) )
		{
			var positions = new Vector3[g.Positions.Length];
			var normals = new Vector3[g.Normals.Length];
			File.Deform( g, pose, root, positions, normals );
			foreach ( var draw in g.Draws )
			{
				var material = materials.ResolveVariant( config, draw.Material, config );
				if ( material is null || draw.Count == 0 ) continue;
				var vertices = new SkinnedVertex[draw.Count];
				for ( int i = 0; i < vertices.Length; i++ )
				{
					int v = g.Indices[draw.Start + i];
					var skin = g.Skin;
					byte[] bi = new byte[4], bw = new byte[4];
					if ( skin is null )
					{
						bi[0] = indices[File.RigNodes.First( b => b.Name == g.Name ).Id]; bw[0] = 255;
					}
					else
					{
						int left = 255;
						for ( int j = 0; j < 4; j++ )
						{
							bi[j] = indices[skin.Bones[skin.Indices[v * 4 + j]]];
							bw[j] = (byte)(j == 3 ? left : Math.Min( left, (int)MathF.Round( skin.Weights[v * 4 + j] * 255 ) ));
							left -= bw[j];
						}
					}
					var normal = normals[v];
					var tangent = Vector3.Cross( normal, MathF.Abs( normal.z ) < .9f ? Vector3.Up : Vector3.Right ).Normal;
					vertices[i] = new SkinnedVertex( positions[v], normal, new Vector4( tangent, 1 ), g.UVs[v],
						new Color32( bi[0], bi[1], bi[2], bi[3] ), new Color32( bw[0], bw[1], bw[2], bw[3] ) )
					{ Color = g.Colors[v] };
				}
				var mesh = new Mesh( material );
				mesh.CreateVertexBuffer<SkinnedVertex>( vertices.Length, vertices.AsSpan() );
				mesh.CreateIndexBuffer( vertices.Length, Enumerable.Range( 0, vertices.Length ).ToArray() );
				mesh.Bounds = npcPhysics
					? new BBox( new Vector3( -45, -45, -10 ), new Vector3( 45, 45, 85 ) )
					: BBox.FromPoints( positions );
				builder.AddMesh( mesh );
			}
		}
		if ( Bones.Any( b => b.Name == "a_weapon_right_front" ) )
			builder.AddAttachment( "hold_r", Vector3.Zero, Rotation.Identity, "a_weapon_right_front" );
		if ( npcPhysics ) AddPhysics( builder );
		Model = builder.Create();
	}

	void AddPhysics( ModelBuilder builder )
	{
		var hitboxes = builder.AddHitboxSet( "default" );
		// Capsule proxies fit the authored joints; living collision is supplied by the prefab.
		(string Bone, string End, string Parent, float Radius)[] bodies =
		[
			("Hips", "Spine", null, 6), ("Spine", "Neck", "Hips", 7), ("Head", null, "Spine", 4),
			("LeftArm", "LeftForeArm", "Spine", 3), ("LeftForeArm", "LeftHand", "LeftArm", 2.5f),
			("RightArm", "RightForeArm", "Spine", 3), ("RightForeArm", "RightHand", "RightArm", 2.5f),
			("LeftUpLeg", "LeftLeg", "Hips", 4), ("LeftLeg", "LeftFoot", "LeftUpLeg", 3),
			("RightUpLeg", "RightLeg", "Hips", 4), ("RightLeg", "RightFoot", "RightUpLeg", 3)
		];
		Transform Bone( string name ) => Bind[Bones.First( b => b.Name == name ).Id];
		for ( int i = 0; i < bodies.Length; i++ )
		{
			var b = bodies[i]; var bind = Bone( b.Bone );
			var end = b.End is null ? Vector3.Up * 3 : bind.PointToLocal( Bone( b.End ).Position );
			hitboxes.AddCapsule( b.Bone, b.Bone, new Capsule( Vector3.Zero, end, b.Radius ) )
				.WithSurface( "flesh" ).AddTag( b.Bone == "Head" ? "head" : "body" );
			builder.AddBody( i < 2 ? 15 : 5, Surface.FindByName( "flesh" ), b.Bone )
				.SetBindPose( bind ).SetDamping( .4f, 2 ).AddCapsule( new Capsule( Vector3.Zero, end, b.Radius ) );
			if ( b.Parent is null ) continue;
			int parent = Array.FindIndex( bodies, x => x.Bone == b.Parent );
			builder.AddBallJoint( parent, i, Bone( b.Parent ).ToLocal( bind ), Transform.Zero )
				.WithSwingLimit( i < 3 ? 35 : 85 ).WithTwistLimit( -30, 30 );
		}
	}

	public Dictionary<uint, Transform> Sample( string clip, float time )
	{
		var pose = File.Pose( Clips[clip], time, preserveBoneLengths: true );
		// Strip authored root travel: navigation owns world movement.
		Matrix.Invert( File.NodePose( pose, "root_point" ), out var root );
		return pose.ToDictionary( p => p.Key, p => ModelFile.Convert( p.Value * root ) );
	}

	[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential )]
	struct SkinnedVertex( Vector3 position, Vector3 normal, Vector4 tangent, Vector2 uv, Color32 indices, Color32 weights )
	{
		[VertexLayout.Position] public Vector3 Position = position;
		[VertexLayout.Normal] public Vector3 Normal = normal;
		[VertexLayout.Tangent] public Vector4 Tangent = tangent;
		[VertexLayout.TexCoord] public Vector2 UV = uv;
		[VertexLayout.Color] public Vector4 Color = Vector4.One;
		[VertexLayout.BlendIndices] public Color32 Indices = indices;
		[VertexLayout.BlendWeight] public Color32 Weights = weights;
	}
}

sealed class PaydayCharacterLoader : ResourceLoader<PaydayMount>
{
	readonly NpcDefinition definition;
	public PaydayCharacterLoader( NpcDefinition definition )
	{
		this.definition = definition;
		Tags.Add( "character" );
		Flags = ResourceFlags.DeveloperOnly;
	}
	protected override object Load()
	{
		var character = new PaydayCharacter( Host.Database, Host.BrowserMaterials, definition, Path );
		Host.Characters[definition.Id] = character;
		return character.Model;
	}
}
