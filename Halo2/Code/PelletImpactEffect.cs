using System.Threading.Tasks;
using Halo2.Blam;

namespace Halo2;

[Hide]
public sealed partial class PelletImpactEffect : Component
{
	public sealed record DecalProjection( string Marker, Vector2 Distance, float Cone );

	public sealed record ImpactDecal( string Tag, string Name, string TexturePath, Vector2 Modifier, Vector2 Radius,
		Vector2[] Sizes, Vector2[] Centers, int MaxOverlap, float RejectionDistance, float Lifetime, ParticleGradient Tint )
	{
		public DecalProjection Projection { get; set; }
		public int Layer { get; set; } = 2;
		DecalDefinition definition;
		internal DecalDefinition Material => definition ??= new DecalDefinition { ColorTexture = Texture.Load( TexturePath ), Width = 1, Height = 1 };
	}

	public sealed record ImpactLight( float Duration, Vector2 Radius, Color EndColor, Color StartColor, Vector2 Brightness );

	public sealed record ContactEvent( float Skip, Vector2 Delay, Vector2 Duration, ImpactDecal[] Marks, bool HasParticles, ImpactEmitter[] Emitters )
	{
		public ImpactLight[] Lights { get; init; } = [];
		public SoundEvent[] Sounds { get; init; } = [];
	}
	public sealed record ContactVisuals
	{
		public ContactEvent[] Events { get; }
		internal bool HasProjectedMarks { get; }

		public ContactVisuals( ContactEvent[] events )
		{
			Events = events;
			HasProjectedMarks = events.Any( e => e.Marks.Any( m => m.Projection is not null ) );
		}
	}

	[Property] public ContactVisuals Visuals { get; set; }
	static uint eventSeed = (uint)System.Random.Shared.Next();
	static uint decalSeed = (uint)System.Random.Shared.Next();

	internal bool Place( Surface surface, GameObject target, Vector3 point, Vector3 normal, Vector3 direction, string hitBone = null )
	{
		if ( Visuals is null || !target.IsValid() ) return false;
		var parent = target;
		if ( target.GetComponentInChildren<SkinnedModelRenderer>() is { CreateBoneObjects: true } skin )
		{
			var bone = hitBone is null ? skin.GetClosestBone( point ) : skin.Model?.Bones.GetBone( hitBone );
			parent = skin.GetBoneObject( bone ) ?? target;
		}
		var contactTransform = new Transform( point, Rotation.LookAt( -normal, Vector3.Random ) );
		_ = PlayContactEvents( Visuals, surface, target, parent, parent.WorldTransform.ToLocal( contactTransform ), contactTransform.Rotation.Inverse * direction );
		if ( !Visuals.HasProjectedMarks && surface.IsValid() ) surface.PlaceSplatter( point, direction, target );
		return true;
	}

	double EventFraction() => BlamRandom.Next( ref eventSeed ) * (double)(1f / 65535f);
	float EventTime( Vector2 range ) => range.x == range.y ? range.x : (float)(range.x + ((double)range.y - range.x) * EventFraction());

	async Task PlayContactEvents( ContactVisuals visuals, Surface surface, GameObject target, GameObject parent, Transform local, Vector3 incoming )
	{
		float elapsed = 0;
		var clock = TaskSource.Create();
		double started;
		var scene = parent.Scene;
		using ( scene.Push() ) started = Time.NowDouble;

		async Task WaitUntil( float elapsed )
		{
			while ( parent.IsValid() )
			{
				using ( scene.Push() )
					if ( Time.NowDouble >= started + elapsed ) return;
				await clock.Frame();
			}
		}
		for ( int i = 0; i < visuals.Events.Length; i++ )
		{
			var entry = visuals.Events[i];
			if ( i > 0 ) await WaitUntil( elapsed );
			if ( !parent.IsValid() ) return;
			// Retail selects event zero directly. Only transitions roll skip probability.
			if ( i > 0 && entry.Skip > 0 && EventFraction() < entry.Skip ) continue;
			float delay = EventTime( entry.Delay );
			elapsed += delay;
			if ( delay > 0 ) await WaitUntil( elapsed );
			if ( !parent.IsValid() ) return;

			var duration = EventTime( entry.Duration );
			using ( parent.Scene.Push() )
			{
				var transform = parent.WorldTransform.ToWorld( local );
				foreach ( var sound in entry.Sounds ) Sound.Play( sound, transform.Position );
				foreach ( var light in entry.Lights ) _ = PlayLight( parent, transform, light );
				foreach ( var mark in entry.Marks )
				{
					if ( mark.Projection is {} projection )
						ProjectMark( mark, projection, target, transform.Position, transform.Rotation * incoming );
					else
						PlaceMark( mark, parent, transform.Position, -transform.Rotation.Forward );
				}
				if ( entry.Emitters is null && entry.HasParticles && surface.IsValid() ) PlaceParticles( surface, parent, transform );
			}

			float end = elapsed + duration;
			if ( entry.Emitters is { Length: > 0 } emitters && duration > 0 )
			{
				var random = InitializeImpactSystems( emitters.Length );
				int tick = (int)System.MathF.Floor( elapsed * 30 + 0.00001f ) + 1;
				float previous = 0;
				while ( previous < duration )
				{
					float boundary = tick++ / 30f;
					await WaitUntil( boundary );
					if ( !parent.IsValid() ) return;
					float active = boundary + 0.000001f >= end ? duration : boundary - elapsed;
					using ( parent.Scene.Push() )
						EmitImpactParticles( emitters, random, previous, active, parent, parent.WorldTransform.ToWorld( local ), incoming );
					previous = active;
				}
			}
			elapsed = end;
		}
	}

	static async Task PlayLight( GameObject parent, Transform transform, ImpactLight data )
	{
		var go = new GameObject( parent, false, "Impact light" );
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.WorldTransform = transform;
		var light = go.AddComponent<PointLight>();
		var clock = TaskSource.Create();
		TimeSince elapsed = 0;
		while ( go.IsValid() )
		{
			using ( go.Scene.Push() )
			{
				float control = 1 - System.MathF.Floor( elapsed * 30 ) / (data.Duration * 30);
				if ( control <= 0 ) break;
				light.Radius = MathX.Lerp( data.Radius.x, data.Radius.y, control );
				light.LightColor = Color.Lerp( data.EndColor, data.StartColor, control );
				light.Intensity = MathX.Lerp( data.Brightness.x, data.Brightness.y, control ).Clamp( 0, 4 );
				go.Enabled = true;
			}
			await clock.Frame();
		}
		if ( go.IsValid() ) go.Destroy();
	}

	static void PlaceParticles( Surface surface, GameObject parent, Transform transform )
	{
		var dust = surface.PrefabCollection.BulletImpact ?? surface.GetBaseSurface()?.PrefabCollection.BulletImpact;
		if ( dust.IsValid() )
		{
			var impact = dust.Clone( new CloneConfig { Transform = transform, StartEnabled = true } );
			impact.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
			impact.SetParent( parent, true );
			if ( impact.GetComponent<TemporaryEffect>() is {} effect ) effect.BecomeOrphan = true;
		}
	}

	void ProjectMark( ImpactDecal mark, DecalProjection projection, GameObject target, Vector3 point, Vector3 incoming )
	{
		var axis = projection.Marker == "gravity" ? Vector3.Down : incoming;
		var direction = Spread.Deviate( axis, 0, projection.Cone, ref decalSeed );
		float distance = projection.Distance.x;
		if ( distance != projection.Distance.y )
			distance += (projection.Distance.y - distance) * BlamRandom.Next( ref decalSeed ) * (1f / 65535f);
		var ray = target.Scene.Trace.Ray( point, point + direction * distance ).UseHitPosition();
		if ( target.GetComponent<MapCollider>() is null )
			ray = ray.IgnoreGameObjectHierarchy( target.GetComponentInParent<ModelPhysics>( true )?.GameObject ?? target );
		var hit = ray.Run();
		if ( hit.StartedSolid ) hit = ray.RunAll().FirstOrDefault( h => h.Hit && !h.StartedSolid && h.Distance > 0 );
		if ( hit.Hit && hit.GameObject.IsValid() ) PlaceMark( mark, hit.GameObject, hit.HitPosition, hit.Normal );
	}

	void PlaceMark( ImpactDecal data, GameObject parent, Vector3 point, Vector3 normal )
	{
		float Fraction() => BlamRandom.Next( ref decalSeed ) * (1f / 65535f);
		float Sample( Vector2 range ) => range.x + (range.y - range.x) * Fraction();
		float modifier = data.Modifier.x == data.Modifier.y ? data.Modifier.x : Sample( data.Modifier );
		if ( data.MaxOverlap > 0 )
		{
			int overlaps = 0;
			foreach ( var existing in parent.Scene.GetAllComponents<PelletMark>() )
			{
				if ( existing.Tag != data.Tag ) continue;
				float distance = existing.Radius + data.RejectionDistance;
				if ( existing.WorldTransform.PointToWorld( existing.Contact ).DistanceSquared( point ) > distance * distance ) continue;
				if ( ++overlaps >= data.MaxOverlap ) return;
			}
		}

		var tangent = Shotgun.Perpendicular( normal );
		var second = Vector3.Cross( normal, tangent ).Normal;
		float angle = Fraction() * System.MathF.Tau;
		var up = second * System.MathF.Sin( angle ) + tangent * System.MathF.Cos( angle );
		var transform = new Transform( point, Rotation.LookAt( -normal, up ) );

		// The original high-word selection multiplies by the last sequence index.
		var definition = data.Material;
		bool sheet = definition.ColorTexture.SequenceCount > 0;
		int sequence = sheet ? ((int)BlamRandom.Next( ref decalSeed ) * (data.Sizes.Length - 1)) >> 16 : 0;
		float radius = Sample( data.Radius ) * (modifier == 0 ? 1 : modifier);
		// The color draw advances the stream even for these fixed white decal tints.
		Fraction();
		var mark = new GameObject( parent, false, data.Name );
		mark.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		var center = data.Centers[sequence] * radius;
		mark.WorldTransform = transform.WithPosition( transform.Position + transform.Rotation.Left * center.x + transform.Rotation.Up * center.y );
		var footprint = mark.AddComponent<PelletMark>();
		footprint.Tag = data.Tag;
		footprint.Contact = mark.WorldTransform.PointToLocal( point );
		footprint.Radius = radius;
		var decal = mark.AddComponent<Decal>();
		decal.Decals = [definition];
		decal.Transient = true;
		decal.SheetSequence = sheet;
		decal.SequenceId = (uint)sequence;
		decal.Size = data.Sizes[sequence] * radius;
		decal.Rotation = 0;
		decal.LifeTime = data.Lifetime;
		decal.ColorTint = data.Tint;
		decal.SortLayer = (uint)data.Layer;
		mark.AddComponent<TemporaryEffect>();
		mark.Enabled = true;
	}
}

[Hide]
internal sealed class PelletMark : Component
{
	internal string Tag { get; set; }
	internal Vector3 Contact { get; set; }
	internal float Radius { get; set; }
}
