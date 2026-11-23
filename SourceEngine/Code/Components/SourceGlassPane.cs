using Sandbox;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Source1;

[Title( "Source Breakable Glass" )]
public sealed partial class SourceGlassPane : Component, Component.IDamageable, Component.ITriggerListener, Component.ICollisionListener, Component.ExecuteInEditor, Component.IHasBounds
{
	[Property] public float Width { get; set; } = 96;
	[Property] public float Height { get; set; } = 96;
	int Columns => Math.Clamp( (int)(Width / 12), 1, 16 );
	int Rows => Math.Clamp( (int)(Height / 12), 1, 16 );
	float CellWidth => Width / Columns;
	float CellHeight => Height / Rows;
	float HalfWidth => Width / 2;
	[Property, Range( 0, 100 )] public float Fragility { get; set; } = 100;
	[Sync] public bool Cracked { get; private set; }
	[Sync] public GlassMask Holes { get; private set; }
	[Sync] private int StyleSeed { get; set; }
	readonly float[] support = new float[256];
	readonly float[] supportScratch = new float[256];
	bool checkSupport;
	GlassMask displayedHoles;
	bool displayedCracked;
	int displayedSeed;
	SceneObject glass;
	BoxCollider trigger;
	readonly List<BoxCollider> strips = new();

	public BBox LocalBounds => new( new Vector3( -0.5f, -HalfWidth, 0 ), new Vector3( 0.5f, HalfWidth, Height ) );

	bool Inside( int x, int y ) => x >= 0 && y >= 0 && x < Columns && y < Rows;

	bool Solid( int x, int y ) => !Inside( x, y ) || !Holes.Has( x, y );
	static bool Coin() => Game.Random.Int( 0, 1 ) == 0;

	protected override void OnEnabled()
	{
		Array.Fill( support, 1f );
		Refresh();
	}

	protected override void OnUpdate()
	{
		if ( displayedHoles != Holes || displayedCracked != Cracked || displayedSeed != StyleSeed )
			Refresh();
		if ( glass.IsValid() ) glass.Transform = WorldTransform;
		UpdateShards();
	}

	protected override void OnDisabled()
	{
		glass?.Delete();
		glass = null;
		ClearColliders();
		ClearShards();
	}

	[Property, Doo.ArgumentHint<GameObject>( "user" )] public Doo OnBreak { get; set; }

	void Crack( GameObject breaker = null )
	{
		if ( Cracked ) return;
		Cracked = true;
		RunDoo( OnBreak, context => context.SetArgument( "user", breaker ?? GameObject ) );
		StyleSeed = Game.Random.Int( 0, 1000000 );
		PlayGlassSound( true, WorldTransform.PointToWorld( Vector3.Up * Height / 2 ) );
	}

	[Rpc.Broadcast]
	private void PlayGlassSound( bool first, Vector3 at ) => PlaySample( first, at );

	void PlaySample( bool first, Vector3 at )
	{
		var sample = first ? $"glass_sheet_break{Game.Random.Int( 1, 3 )}" : Game.Random.FromArray( new[] { "glass_cup_break1", "glass_cup_break2", "glass_bottle_break2" } );
		var file = SoundFile.Load( $"mount://{ContentMount}/sound/physics/glass/{sample}.wav.vsnd" );
		if ( file is not null ) Sound.PlayFile( file, first ? 1 : Game.Random.Float( 0.75f, 1 ), first || Game.Random.Int( 0, 2 ) != 0 ? 1 : Game.Random.Float( 0.95f, 1.29f ) ).Position = at;
	}

	public void OnDamage( in DamageInfo damage )
	{
		if ( IsProxy || damage.Damage <= 0 || Holes == GlassMask.MaxValue ) return;
		Crack( damage.Attacker );
		var dir = (damage.Position - damage.Origin).Normal;
		if ( damage.Tags.Has( "explosion" ) || damage.Tags.Has( "blast" ) || damage.Tags.Has( "sonic" ) )
		{
			var force = WorldRotation.Forward * (dir.Dot( WorldRotation.Forward ) > 0 ? 3000 : -3000);
			EmitShards( ~Holes, force, damage.Position, false );
			Holes = GlassMask.MaxValue;
			return;
		}
		if ( damage.Tags.Has( "crush" ) || damage.Tags.Has( "impact" ) || damage.Tags.Has( "slash" ) ) return;

		var p = WorldTransform.PointToLocal( damage.Position );
		float u = Math.Clamp( (p.y + HalfWidth) / CellWidth, 0, Columns - 0.0001f );
		float v = Math.Clamp( p.z / CellHeight, 0, Rows - 0.0001f );
		int x = (int)u, y = (int)v;
		GlassMask before = Holes;
		Break( x, y );
		if ( u - x > 0.8f ) Break( x + 1, y );
		else if ( u - x < 0.2f ) Break( x - 1, y );
		if ( v - y > 0.8f ) Break( x, y + 1 );
		else if ( v - y < 0.2f ) Break( x, y - 1 );
		EmitShards( Holes ^ before, dir * 500, damage.Position, true );
		before = Holes;
		if ( Coin() )
		{
			Break( x, y + 1 );
			if ( Coin() ) Break( x, y + 2 );
		}
		EmitShards( Holes ^ before, dir * 1000, damage.Position, false );
	}

	void Break( int x, int y )
	{
		if ( !Inside( x, y ) || !Solid( x, y ) ) return;
		Holes = Holes.With( x, y );
		checkSupport = true;
	}

	public void Restore()
	{
		if ( IsProxy ) return;
		Cracked = false;
		Holes = default;
		checkSupport = false;
		Array.Fill( support, 1f );
	}

	public void ShatterAt( Vector3 data ) => Shatter( new Vector2( data.x, data.y ), data.z );

	public void Shatter( Vector2 center, float radius )
	{
		if ( IsProxy ) return;
		Crack();
		GlassMask before = Holes;
		for ( int y = 0; y < Rows; y++ )
			for ( int x = 0; x < Columns; x++ )
				if ( (new Vector2( (x + 0.5f) * CellWidth, (y + 0.5f) * CellHeight ) - new Vector2( center.x * Width, center.y * Height )).Length <= radius )
					Break( x, y );
		var pos = new Vector3( 0, center.x * Width - HalfWidth, center.y * Height );
		EmitShards( Holes ^ before, WorldRotation.Forward * 500, WorldTransform.PointToWorld( pos ), true );
	}

	void ICollisionListener.OnCollisionStart( Collision hit )
	{
		if ( IsProxy || Cracked || hit.Other.Body is not { } body || body.Mass < 2 ) return;
		if ( hit.Other.GameObject?.GetComponentInParent<PlayerController>() is not null ) return;
		if ( Math.Abs( hit.Contact.NormalSpeed ) < (body.Mass > 50 ? 25 : 50) ) return;
		Crack( hit.Other.GameObject );
		Touch( hit.Other.Collider );
	}

	void ITriggerListener.OnTriggerEnter( Collider other ) => Touch( other );

	void Touch( Collider other )
	{
		if ( IsProxy || !Cracked || !other.IsValid() || other.IsTrigger || other.GameObject == GameObject ) return;
		var bounds = BBox.FromPoints( other.GetWorldBounds().Corners.Select( point => WorldTransform.PointToLocal( point ) ) );
		int left = Math.Clamp( (int)MathF.Floor( (bounds.Mins.y + HalfWidth) / CellWidth ), 0, Columns );
		int right = Math.Clamp( (int)MathF.Ceiling( (bounds.Maxs.y + HalfWidth) / CellWidth ), 0, Columns );
		GlassMask before = Holes;
		for ( int y = Math.Max( 0, (int)MathF.Floor( bounds.Mins.z / CellHeight ) ); y < Math.Min( Rows, MathF.Ceiling( bounds.Maxs.z / CellHeight ) ); y++ )
		{
			if ( Coin() ) Break( left - 1, y );
			for ( int x = left; x < right; x++ ) Break( x, y );
			if ( Coin() ) Break( right + 1, y );
		}
		EmitShards( Holes ^ before, other.GetVelocityAtPoint( other.WorldPosition ) * 5, other.WorldPosition, true );
	}

	float Support( int x, int y ) => !Inside( x, y ) ? 1 : Solid( x, y ) ? support[y * 16 + x] : 0;

	protected override void OnFixedUpdate()
	{
		if ( IsProxy || Scene.IsEditor ) return;
		if ( trigger.IsValid() )
			foreach ( var other in trigger.Touching )
				if ( other.IsValid() && other.GetVelocityAtPoint( other.WorldPosition ).LengthSquared > 0.01f ) Touch( other );
		if ( !checkSupport ) return;
		checkSupport = false;
		var next = supportScratch;
		for ( int y = 0; y < Rows; y++ )
			for ( int x = 0; x < Columns; x++ )
				next[y * 16 + x] = (0.01f + Support( x, y + 1 ) + 1.25f * Support( x, y - 1 ) + Support( x - 1, y ) + Support( x + 1, y )
					+ Support( x - 1, y - 1 ) + Support( x + 1, y - 1 ) + 0.25f * (Support( x - 1, y + 1 ) + Support( x + 1, y + 1 ))) / 6.75f;
		Array.Copy( next, support, next.Length );
		GlassMask before = Holes;
		for ( int y = 0; y < Rows; y++ )
			for ( int x = 0; x < Columns; x++ )
				if ( Solid( x, y ) && support[y * 16 + x] < Fragility * 0.002f )
				{
					Break( x, y );
					if ( Coin() ) DropPane( x, y );
				}
		EmitShards( Holes ^ before, Vector3.Zero, WorldPosition, true );
	}

	void ClearColliders()
	{
		foreach ( var box in strips ) box.Destroy();
		strips.Clear();
		trigger?.Destroy();
		trigger = null;
	}

	BoxCollider Box( Vector3 center, Vector3 size )
	{
		var box = Components.Create<BoxCollider>();
		box.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
		box.Center = center;
		box.Scale = size;
		box.Surface = Surface.FindByName( "glass" );
		return box;
	}

	void Refresh()
	{
		displayedCracked = Cracked;
		displayedHoles = Holes;
		displayedSeed = StyleSeed;
		glass?.Delete();
		glass = null;
		if ( IntactBrush.IsValid() ) IntactBrush.Enabled = !Cracked;
		if ( Cracked )
			glass = new SceneObject( Scene.SceneWorld, BuildPane(), WorldTransform );
		if ( Scene.IsEditor ) return;
		foreach ( var box in strips ) box.Destroy();
		strips.Clear();
		if ( !Cracked )
		{
			trigger?.Destroy();
			trigger = null;
			strips.Add( Box( new( 0, 0, Height / 2 ), new( 1, Width, Height ) ) );
			return;
		}
		if ( !trigger.IsValid() )
		{
			trigger = Box( new( 0, 0, Height / 2 ), new( 4, Width, Height ) );
			trigger.IsTrigger = true;
			trigger.ColliderFlags = ColliderFlags.IgnoreTraces;
		}
		for ( int y = 0; y < Rows; y++ )
			for ( int x = 0; x < Columns; )
			{
				int start = x;
				while ( x < Columns && Solid( x, y ) ) x++;
				if ( x > start ) strips.Add( Box( new( 0, (start + x) * CellWidth / 2 - HalfWidth, (y + 0.5f) * CellHeight ), new( 1, (x - start) * CellWidth, CellHeight ) ) );
				x++;
			}
	}
}
