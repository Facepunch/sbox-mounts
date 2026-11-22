using System;
using System.Runtime.CompilerServices;

namespace GoldSrc.HalfLife;

[Hide]
public sealed class Gib : Component, Component.ICollisionListener
{
	Npc.BloodColor _blood;
	ModelRenderer _renderer;
	Rigidbody _body;
	ClassicBounce _classic;
	int _decals = 5;
	TimeSince _age;
	TimeUntil _fade;
	bool _landed;
	bool _fading;
	bool _fleshShard;
	static readonly ConditionalWeakTable<Material, Material> FadeMaterials = new();

	internal static void Scatter( BBox hull, Vector3 eye, Vector3 direction, float health, bool human, Npc.BloodColor blood, int pieces = 4, bool skull = true, string sourceModel = null )
	{
		Vector3? headTarget = null;
		if ( human && skull && Game.Random.Int( 0, 100 ) <= 5 )
			headTarget = Game.ActiveScene.GetAllComponents<PlayerController>().MinBy( p => p.WorldPosition.DistanceSquared( eye ) )?.EyeTransform.Position;
		ScatterPieces( hull, eye, direction, health, human, blood, pieces, skull, headTarget, Game.Random.Next(), sourceModel );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ScatterPieces( BBox hull, Vector3 eye, Vector3 direction, float health, bool human, Npc.BloodColor blood, int pieces, bool skull, Vector3? headTarget, int seed, string sourceModel )
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ) return;
		var file = human ? "hgibs" : "agibs";
		var path = mount.GameDirs.OrderByDescending( dir => sourceModel?.StartsWith( $"mount://hl1/{dir}/" ) == true )
			.Select( dir => $"mount://hl1/{dir}/models/{file}.mdl.vmdl" ).FirstOrDefault( name => mount.GetByPath( name ) is ModelLoader );
		if ( path is null ) return;
		var model = Model.Load( path );
		if ( !model.IsValid() || model.IsError || model.Parts.All.FirstOrDefault() is not { } part ) return;
		var scale = health > -50 ? 0.7f : health > -200 ? 2 : 4;
		// The human model also contains prop pieces. Monsters only use its first six choices.
		var lastPiece = System.Math.Min( human ? 6 : 4, part.Choices.Count ) - 1;
		if ( lastPiece < (human ? 1 : 0) ) return;
		var random = new System.Random( seed );
		if ( human && skull )
		{
			var velocity = headTarget is { } target ? (target - eye).Normal * 300 + Vector3.Up * 100
				: new Vector3( random.Float( -100, 100 ), random.Float( -100, 100 ), random.Float( 200, 300 ) );
			Spawn( model, part.Name, 0, eye, velocity * scale, blood, random: random );
		}
		for ( int i = 0; i < pieces; ++i )
		{
			var velocity = (direction + random.VectorInCube( 0.25f )) * random.Float( 300, 400 ) * scale;
			Spawn( model, part.Name, random.Int( human ? 1 : 0, lastPiece ), random.VectorInCube( hull ) + Vector3.Up, velocity, blood, random: random );
		}
	}

	internal static void Spawn( Model model, string group, int variant, Vector3 point, Vector3 velocity, Npc.BloodColor blood, float fadeAfter = 0, System.Random random = null, bool fleshShard = false )
	{
		var go = new GameObject( false, "Gib" );
		go.NetworkMode = NetworkMode.Never;
		go.Tags.Add( "debris" );
		go.Tags.Add( "nocollide_props" );
		go.WorldPosition = point;
		var gib = go.AddComponent<Gib>();
		gib._blood = blood;
		gib._fleshShard = fleshShard;
		gib._renderer = go.AddComponent<ModelRenderer>();
		gib._renderer.Model = model;
		gib._renderer.SetBodyGroup( group, variant );
		random ??= Game.Random;
		var spin = Vector3.Zero;
		if ( fleshShard )
		{
			if ( random.Int( 0, 255 ) < 200 ) spin = random.VectorInCube( 256 );
		}
		else if ( fadeAfter == 0 )
			spin = new Vector3( 0, random.Float( 100, 200 ), random.Float( 100, 300 ) );
		velocity = fleshShard ? velocity : velocity.ClampLength( 1500 );
		gib._age = 0;
		if ( fadeAfter > 0 )
		{
			gib._landed = true;
			gib._fade = fadeAfter;
		}
		if ( ClassicBounce.Enabled )
		{
			gib._classic = new( velocity, new Angles( spin.y, spin.z, spin.x ), fleshShard ? 0.5f : 1, 0.55f );
			go.Enabled = true;
			return;
		}
		var collider = go.AddComponent<HullCollider>();
		collider.Type = HullCollider.PrimitiveType.Points;
		collider.Points = ((ModelLoader)Sandbox.Mounting.Directory.Get( "hl1" ).GetByPath( model.Name )).GibHull( group, variant );
		collider.Friction = 0.55f;
		collider.ColliderFlags |= ColliderFlags.IgnoreTraces;
		collider.Elasticity = 0.5f;
		gib._body = go.AddComponent<Rigidbody>();
		gib._body.RigidbodyFlags |= RigidbodyFlags.DisableCollisionSounds;
		gib._body.EnhancedCcd = true;
		gib._body.EnableImpactDamage = false;
		if ( fleshShard ) gib._body.GravityScale = 0.5f;
		go.Enabled = true;
		gib._body.Velocity = velocity;
		gib._body.AngularVelocity = spin * (MathF.PI / 180);
	}

	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( collision.Other.IsTrigger || collision.Other.Body?.BodyType != PhysicsBodyType.Static ) return;
		var normal = collision.Contact.Normal;
		if ( normal.Dot( collision.Self.Body.MassCenter - collision.Contact.Point ) < 0 ) normal = -normal;
		MarkImpact( collision.Contact.Point, normal, collision.Contact.Speed.z );
	}

	protected override void OnFixedUpdate()
	{
		if ( _classic is null ) return;
		var speed = _classic.Velocity.z;
		var hit = _classic.Step( GameObject, worldOnly: true );
		if ( hit.Hit ) MarkImpact( hit.EndPosition, hit.Normal, speed );
	}

	void MarkImpact( Vector3 point, Vector3 normal, float speed )
	{
		if ( _fleshShard && Game.Random.Int( 0, 5 ) == 0 && Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
		{
			var volume = 0.8f * MathF.Min( 1, MathF.Abs( speed ) / 450 );
			var pitch = Game.Random.Int( 0, 3 ) == 0 ? Game.Random.Float( 0.95f, 1.05f ) : 1;
			if ( volume > 0 ) NpcAudio.Play( mount.NpcAudio.Find( "debris/flesh" ), point, volume, pitch );
		}
		if ( _decals <= 0 || _blood == Npc.BloodColor.None ) return;
		var prefix = _blood == Npc.BloodColor.Red ? "blood" : "yblood";
		ImpactDecal.Place( point, normal, $"{prefix}{Game.Random.Int( 1, 6 )}", 0 );
		_decals--;
	}

	protected override void OnUpdate()
	{
		if ( !_landed )
		{
			if ( WorldPosition.z < -32768 ) { GameObject.Destroy(); return; }
			if ( _age < 4 || !(_classic?.Settled ?? _body.Sleeping) ) return;
			_landed = true;
			_fade = 25;
			if ( Networking.IsHost && _blood != Npc.BloodColor.None )
				Scene.GetSystem<Sandbox.Npcs.NpcSystem>().EmitStimulus( "meat", WorldPosition, GameObject, 384, 25 );
		}
		if ( _fade > 0 ) return;
		if ( !_fading )
		{
			_fading = true;
			for ( int i = 0; i < _renderer.Materials.Count; i++ )
			{
				var material = _renderer.Materials.GetOriginal( i );
				if ( material.IsValid() ) _renderer.Materials.SetOverride( i, FadeMaterials.GetValue( material, CreateFadeMaterial ) );
			}
		}
		var alpha = 1 + (float)_fade * (_fleshShard ? 0.5f : 70f / 255f);
		if ( alpha <= 0 ) GameObject.Destroy();
		else _renderer.Attributes.Set( "StudioOpacity", alpha );
	}

	static Material CreateFadeMaterial( Material original )
	{
		var copy = original.CreateCopy();
		copy.SetFeature( "F_STUDIO_FADE", 1 );
		return copy;
	}
}
