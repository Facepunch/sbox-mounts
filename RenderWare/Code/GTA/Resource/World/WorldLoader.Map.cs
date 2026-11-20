using System;

namespace Renderware;

partial class WorldLoader
{
	/// <summary>
	/// A model's static geometry with its textures resolved, and its own 2dfx lights (San Andreas).
	/// <see cref="Parts"/> is null if it can't be batched (skinned, unreadable).
	/// </summary>
	sealed record MapModel( List<WorldPart> Parts, IReadOnlyList<LightEffect> Lights );

	/// <summary>
	/// Model space geometry for one texture ("txd/texture" key, null for untextured). <see cref="HasAlpha"/> if it
	/// wants blending: its texture's alpha is a soft fade, or its vertices fade (prelit alpha below 255). On / off
	/// alpha (foliage) doesn't count - that's alpha tested in the cells.
	/// </summary>
	sealed record WorldPart( string Texture, SimpleVertex[] Vertices, int[] Indices, bool HasAlpha );

	/// <summary>How an instance's geometry blends, from its IDE flags.</summary>
	enum BlendMode
	{
		Opaque,

		/// <summary>Flag 0x4 "draw last": parts with alpha are alpha blended (light beams, glass). Such models are
		/// spawned as their own objects rather than batched, so they sort.</summary>
		Translucent,

		/// <summary>Flag 0x8: everything added on top, unlit (night windows, glows).</summary>
		Additive,
	}

	static BlendMode GetBlendMode( ItemFlags flags ) =>
		flags.HasFlag( ItemFlags.Additive ) ? BlendMode.Additive : flags.HasFlag( ItemFlags.Transparent ) ? BlendMode.Translucent : BlendMode.Opaque;

	readonly Dictionary<string, MapModel> mapModels = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, Sprite> sprites = new( StringComparer.OrdinalIgnoreCase );

	void BuildMap()
	{
		var worldRoot = new GameObject( true, "world" );
		var propsRoot = new GameObject( true, "props" );
		var lightsRoot = new GameObject( true, "lights" );

		var cells = new Dictionary<(int, int), Cell>();
		int skipped = 0, pointLights = 0, coronas = 0;

		foreach ( var instance in Host.Instances )
		{
			// high detail only - the mount marks LODs for all three games
			if ( instance.IsLod || Host.GetDefinition( instance.Id ) is not { } def )
				continue;

			// interiors are rooms parked out of sight (0 = outside, 13 = everywhere); the high bits are flags in SA
			if ( (instance.Interior & 0xFF) is not (0 or 13) )
			{
				skipped++;
				continue;
			}

			var transform = new Transform( instance.Position * Units.Scale, instance.Rotation, instance.Scale );
			var model = GetMapModel( def );

			// dynamic props (object.dat) stay their own objects, everything else is baked into cells -
			// collision goes with the geometry either way
			var collision = Host.GetCollision( def.Model );
			GameObject prop = null;
			if ( model.Parts is { } parts && !Host.IsDynamicObject( def.Model ) )
			{
				var key = CellKey( transform.Position );
				if ( !cells.TryGetValue( key, out var cell ) )
					cells[key] = cell = new Cell( this, CellOrigin( key ) );

				// see-through parts of "draw last" models (light beams, glass) aren't batched: translucent things sort
				// per object, and a beam baked into a whole cell would sort by the cell's centre. The rest of the model
				// (the building around the glass) still is
				var mode = GetBlendMode( def.Flags );
				if ( mode == BlendMode.Translucent && GetBlendedModel( def, model ) is { } blendedModel )
				{
					cell.Add( parts.Where( p => !p.HasAlpha ).ToList(), transform, mode );
					prop = new GameObject( propsRoot, true, def.Model ) { WorldTransform = transform };
					prop.AddComponent<ModelRenderer>().Model = blendedModel;
				}
				else
				{
					cell.Add( parts, transform, mode );
				}

				if ( collision is not null )
					cell.AddCollision( collision, transform );
			}
			else
			{
				prop = new GameObject( propsRoot, true, def.Model ) { WorldTransform = transform };
				var propModel = Model.Load( Host.ModelPath( def.Model ) );
				prop.AddComponent<ModelRenderer>().Model = propModel;

				// the model carries its collision (see ModelLoader)
				if ( collision is not null )
				{
					var collider = prop.AddComponent<ModelCollider>();
					//collider.Static = true;
					collider.Model = propModel;

					//var rigidbody = prop.AddComponent<Rigidbody>();
					//rigidbody.StartAsleep = true;
				}
			}

			foreach ( var light in Host.GetLights( def.Id ).Concat( model.Lights ) )
				SpawnLight( light, transform, prop ?? lightsRoot, def.Model, ref pointLights, ref coronas );
		}

		BuildCells( worldRoot, cells );
		BuildSpawnPoints( cells );

		Log.Info( $"World: {cells.Count} cells, {propsRoot.Children.Count} props, {skipped} interior instances skipped, {pointLights} point lights, {coronas} coronas" );
	}

	MapModel GetMapModel( ItemDefinition def )
	{
		if ( mapModels.TryGetValue( def.Model, out var model ) )
			return model;

		List<WorldPart> parts = null;
		IReadOnlyList<LightEffect> lights = Array.Empty<LightEffect>();

		try
		{
			if ( Host.OpenModel( def.Model ) is { } dff )
			{
				lights = dff.Lights;

				// everything placed by ipls is a map object, so frame transforms are ignored like the game does
				var staticParts = dff.GetStaticParts( out bool hasSkinned, simpleModel: true );
				if ( !hasSkinned && staticParts.Count > 0 )
				{
					parts = staticParts.Select( p =>
					{
						var texture = Host.ResolveTexture( def.Txd, p.Texture );
						bool alpha = Host.TextureHasSoftAlpha( texture ) || p.Vertices.Any( v => v.Color.a < 255 );
						return new WorldPart( texture, p.Vertices, p.Indices, alpha );
					} ).ToList();
				}
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't read {def.Model} for the world: {e.Message}" );
		}

		return mapModels[def.Model] = new MapModel( parts, lights );
	}

	readonly Dictionary<string, Model> blendedModels = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Just the alpha blended parts of a map model (<see cref="WorldPart.HasAlpha"/>) as their own model, null if it
	/// has none. Unlit and without collision - the opaque rest of the model and its collision go in the cell.
	/// </summary>
	Model GetBlendedModel( ItemDefinition def, MapModel model )
	{
		if ( blendedModels.TryGetValue( def.Model, out var blended ) )
			return blended;

		var parts = model.Parts?.Where( p => p.HasAlpha ).ToList();
		if ( parts is { Count: > 0 } )
		{
			var builder = Model.Builder.WithName( $"{def.Model}_blended" );
			foreach ( var part in parts )
			{
				var mesh = new Mesh( DffModelBuilder.GetVariant( Host, part.Texture, "F_ALPHA_BLEND" ) );
				mesh.CreateIndexBuffer( part.Indices.Length, part.Indices );
				mesh.CreateVertexBuffer( part.Vertices.Length, part.Vertices );
				mesh.Bounds = BBox.FromPoints( part.Vertices.Select( v => v.Position ) );
				builder.AddMesh( mesh );
			}
			blended = builder.Create();
		}

		return blendedModels[def.Model] = blended;
	}

	/// <summary>
	/// A 2dfx light: a real light when it has a range, and a corona sprite if enabled. Parented to the prop when
	/// there is one, so it goes where the lamppost goes.
	/// </summary>
	void SpawnLight( in LightEffect light, Transform transform, GameObject parent, string model, ref int pointLights, ref int coronas )
	{
		var position = transform.PointToWorld( light.Position * Units.Scale );

		if ( light.LightType > 1 )
			return; // special ones like traffic lights - skip for now

		if ( light.Range > 0 )
		{
			var pointLight = new GameObject( parent, true, $"{model} light" ) { WorldPosition = position }.AddComponent<PointLight>();
			pointLight.LightColor = light.Color;
			pointLight.Radius = light.Range * Units.Scale;
			pointLight.Shadows = false; // there are thousands of these
			pointLights++;
		}

		if ( Coronas && light.Size > 0 && GetSprite( light.Corona ) is { } sprite )
		{
			var corona = new GameObject( parent, true, $"{model} corona" ) { WorldPosition = position }.AddComponent<SpriteRenderer>();
			corona.Sprite = sprite;
			corona.Size = light.Size * Units.Scale * 2; // 2dfx size is the radius
			corona.Color = light.Color;
			corona.Additive = true;
			corona.Lighting = false;
			corona.Shadows = false;
			corona.Billboard = SpriteRenderer.BillboardMode.Always;
			corona.FogStrength = 0; // additive - fogging would only brighten it towards the fog colour
			corona.CameraFadeFar = 20;
			coronas++;
		}
	}

	/// <summary>
	/// A sprite for a mounted texture, cached by name. Marked as an embedded resource so it's written inline when the
	/// built scene gets serialized - it has no file of its own to be referenced by.
	/// </summary>
	Sprite GetSprite( string texture )
	{
		if ( string.IsNullOrEmpty( texture ) )
			return null;

		if ( !sprites.TryGetValue( texture, out var sprite ) )
		{
			sprite = Host.LoadTexture( Host.FindTexture( texture ) ) is { } tex ? Sprite.FromTexture( tex ) : null;
			if ( sprite is not null )
				sprite.EmbeddedResource = new Sandbox.Resources.EmbeddedResource { ResourceCompiler = "embed" };
			sprites[texture] = sprite;
		}

		return sprite;
	}
}
