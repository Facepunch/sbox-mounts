namespace Source1;

static partial class SourceEntities
{
	/// <summary>What every entity of a kind starts from: one node per object, one entry per component, only values that differ from their defaults.</summary>
	[Sandbox.SkipHotload]
	static readonly Dictionary<SourceEntityKind, string> Templates = new()
	{
		[SourceEntityKind.Weapon] = """
			{
				".": {
					"Source1.BulletWeapon": {"CanCancelReload": false, "DeployTime": 0.2, "DroppedGameObject": {"ref": "standalone_wm"}, "PreferredSlot": 0, "SecondaryDelay": 1, "Value": 10},
					"Sandbox.ModelCollider": {},
					"Sandbox.Rigidbody": {}
				},
				"standalone_wm": {
					"Sandbox.BaseWeaponModel": {"EjectBrass": null, "Renderer": {"ref": "standalone_wm:Sandbox.SkinnedModelRenderer"}, "TracerEffect": null},
					"Sandbox.SkinnedModelRenderer": {"CreateAttachments": true, "CreateBoneObjects": true, "Sequence": {"Blending": true, "Looping": true, "Name": null}, "UseAnimGraph": false},
					"Source1.MountedModel": {"Renderer": {"ref": "standalone_wm:Sandbox.SkinnedModelRenderer"}, "Collider": {"ref": ".:Sandbox.ModelCollider"}}
				}
			}
			""",
		[SourceEntityKind.ViewModel] = """
			{
				".": {
					"@": {"NetworkMode": 0},
					"Source1.ViewModel": {"Renderer": {"ref": "Viewmodel:Sandbox.SkinnedModelRenderer"}, "MuzzleEffect": null, "TracerEffect": null}
				},
				"Viewmodel": {
					"Sandbox.SkinnedModelRenderer": {"CreateAttachments": true, "CreateBoneObjects": true, "Parameters": {"bools": {}, "floats": {}, "ints": {}, "rotations": {}, "vectors": {}}, "RenderOptions": {"AfterUILayer": false, "BloomLayer": false, "GameLayer": false, "OverlayLayer": true}, "RenderType": "Off", "Sequence": {"Blending": true, "Looping": true, "Name": null}, "UseAnimGraph": false},
					"Source1.MountedModel": {"Renderer": {"ref": "Viewmodel:Sandbox.SkinnedModelRenderer"}}
				}
			}
			""",
		[SourceEntityKind.WorldModel] = """
			{
				".": {
					"@": {"NetworkMode": 0},
					"Source1.WeaponWorldModel": {"EjectBrass": null, "MuzzleEffect": null, "Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "TracerEffect": null}
				},
				"model": {
					"Sandbox.SkinnedModelRenderer": {"CreateAttachments": true, "CreateBoneObjects": true, "Sequence": {"Blending": true, "Looping": true, "Name": null}, "UseAnimGraph": false},
					"Source1.MountedModel": {"Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}}
				}
			}
			""",
		[SourceEntityKind.Npc] = """
			{
				".": {
					"@": {"Tags": "npc"},
					"Sandbox.NavMeshAgent": {"Acceleration": 2000, "Height": 72, "Radius": 13},
					"Sandbox.Npcs.Layers.NavigationLayer": {},
					"Sandbox.Npcs.Layers.AnimationLayer": {},
					"Sandbox.Npcs.Layers.SpeechLayer": {},
					"Sandbox.Npcs.Layers.SensesLayer": {"FieldOfView": 300, "HearingRange": 1200, "SightRange": 2048},
					"Sandbox.Rigidbody": {"AngularDamping": 2, "EnableImpactDamage": false, "LinearDamping": 0.5, "Locking": {"Pitch": true, "Roll": true, "X": false, "Y": false, "Yaw": false, "Z": false}, "MotionEnabled": false},
					"Source1.NpcDefaults": {},
					"Source1.CreatureNpc": {"Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}}
				},
				"model": {
					"Sandbox.SkinnedModelRenderer": {"CreateAttachments": true, "CreateBoneObjects": true, "MaterialGroup": "default", "Sequence": {"Blending": true, "Looping": true, "Name": null}, "UseAnimGraph": false},
					"Source1.MountedModel": {"Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}},
					"Sandbox.ModelHitboxes": {"Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "Target": {"ref": "."}}
				},
				"collider": {
					"@": {"Tags": "playercontroller"},
					"Sandbox.BoxCollider": {}
				}
			}
			""",
		[SourceEntityKind.Pickup] = """
			{
				".": {
					"@": {"Tags": "prop,removable", "NetworkMode": 1, "NetworkOrphaned": 3},
					"Source1.Hl2.Pickup": {"Kind": "Ammo", "SoundName": "BaseCombatCharacter.AmmoPickup"},
					"Sandbox.ModelCollider": {},
					"Sandbox.Rigidbody": {"EnableImpactDamage": false}
				},
				"model": {
					"Sandbox.SkinnedModelRenderer": {"UseAnimGraph": false},
					"Source1.MountedModel": {"Renderer": {"ref": "model:Sandbox.SkinnedModelRenderer"}, "Collider": {"ref": ".:Sandbox.ModelCollider"}}
				}
			}
			""",
		[SourceEntityKind.Charger] = """
			{
				".": {
					"@": {"Tags": "prop,removable", "NetworkMode": 1},
					"Source1.Hl2.Charger": {},
					"Sandbox.SkinnedModelRenderer": {"UseAnimGraph": false},
					"Sandbox.ModelCollider": {"Static": true},
					"Source1.MountedModel": {"Renderer": {"ref": ".:Sandbox.SkinnedModelRenderer"}, "Collider": {"ref": ".:Sandbox.ModelCollider"}}
				}
			}
			""",
		[SourceEntityKind.Effect] = """
			{
				".": {
					"TemporaryEffect": {"BecomeOrphan": true, "DestroyAfterSeconds": 2},
					"Source1.MountedEffect": {}
				}
			}
			""",
	};
}
