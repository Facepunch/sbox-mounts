using System;

namespace Magicka;

/// <summary>Places an item model on a character's attachment bone once its renderer has a model.</summary>
[Title( "Magicka Held Item" )]
[Category( "Magicka" )]
public sealed class MagickaHeldItem : Component
{
	[Property] public SkinnedModelRenderer Renderer { get; set; }
	[Property] public string ModelPath { get; set; }
	[Property] public string Bone { get; set; } = "rightattach";
	[Property] public float Scale { get; set; } = 1f;
	[Property] public Angles LocalAngles { get; set; }
	[Property] public Vector3 FallbackOffset { get; set; }

	private bool _placed;

	protected override void OnStart() => Place();

	protected override void OnUpdate()
	{
		if ( !_placed )
			Place();
	}

	private void Place()
	{
		if ( _placed || !Renderer.IsValid() || !Renderer.Model.IsValid() || string.IsNullOrEmpty( ModelPath ) )
			return;

		var model = Model.Load( ModelPath );
		if ( !model.IsValid() || model.IsError )
			return;

		var bone = Renderer.GetBoneObject( Bone );
		if ( !bone.IsValid() )
		{
			foreach ( var b in Renderer.Model.Bones.AllBones )
			{
				if ( string.Equals( b.Name, Bone, StringComparison.OrdinalIgnoreCase ) )
				{
					bone = Renderer.GetBoneObject( b );
					break;
				}
			}
		}

		var weapon = new GameObject( GameObject, true, "weapon" );
		weapon.LocalScale = Vector3.One * Math.Clamp( Scale, 0.1f, 10f );

		if ( bone.IsValid() )
		{
			weapon.Parent = bone;
			weapon.LocalPosition = Vector3.Zero;
			weapon.LocalRotation = LocalAngles.ToRotation();
		}
		else
		{
			weapon.LocalPosition = FallbackOffset;
		}

		var renderer = weapon.AddComponent<ModelRenderer>();
		renderer.Model = model;
		_placed = true;
	}
}
