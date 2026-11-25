using System.Threading.Tasks;
using Halo2.Blam;

namespace Halo2;

[Hide]
public sealed partial class PelletImpactEffect : Component
{
	public sealed record ImpactDecal( string Tag, string Name, string TexturePath, Vector2 Modifier, Vector2 Radius,
		Vector2[] Sizes, Vector2[] Centers, int MaxOverlap, float RejectionDistance, float Lifetime, ParticleGradient Tint )
	{
		DecalDefinition definition;
		internal DecalDefinition Material => definition ??= new DecalDefinition { ColorTexture = Texture.Load( TexturePath ), Width = 1, Height = 1 };
	}

	public sealed record ContactEvent( float Skip, Vector2 Delay, Vector2 Duration, ImpactDecal[] Marks, bool HasParticles, ImpactEmitter[] Emitters );
	public sealed record ContactVisuals( ContactEvent[] Events );

	[Property] public ContactVisuals Visuals { get; set; }
	static uint eventSeed = (uint)System.Random.Shared.Next();
	static uint decalSeed = (uint)System.Random.Shared.Next();

	internal bool Place( Surface surface, GameObject target, Vector3 point, Vector3 normal, Vector3 direction )
	{
		if ( Visuals is null || !target.IsValid() ) return false;
		var parent = target;
		if ( target.GetComponentInChildren<SkinnedModelRenderer>() is { CreateBoneObjects: true } skin && skin.GetClosestBone( point ) is {} bone )
			parent = skin.GetBoneObject( bone ) ?? target;
		var contactTransform = new Transform( point, Rotation.LookAt( -normal, Vector3.Random ) );
		_ = PlayContactEvents( Visuals, surface, parent, parent.WorldTransform.ToLocal( contactTransform ), contactTransform.Rotation.Inverse * direction );
		if ( surface.IsValid() ) surface.PlaceSplatter( point, direction, target );
		return true;
	}

	double EventFraction() => BlamRandom.Next( ref eventSeed ) * (double)(1f / 65535f);
	float EventTime( Vector2 range ) => range.x == range.y ? range.x : (float)(range.x + ((double)range.y - range.x) * EventFraction());

	async Task PlayContactEvents( ContactVisuals visuals, Surface surface, GameObject parent, Transform local, Vector3 incoming )
	{
		float elapsed = 0;
		var started = Time.Now;
		for ( int i = 0; i < visuals.Events.Length; i++ )
		{
			var entry = visuals.Events[i];
			float remaining = elapsed - (Time.Now - started);
			if ( remaining > 0 ) await GameTask.DelaySeconds( remaining );
			if ( !parent.IsValid() ) return;
			// Retail selects event zero directly. Only transitions roll skip probability.
			if ( i > 0 && entry.Skip > 0 && EventFraction() < entry.Skip ) continue;
			elapsed += EventTime( entry.Delay );
			float wait = elapsed - (Time.Now - started);
			if ( wait > 0 ) await GameTask.DelaySeconds( wait );
			if ( !parent.IsValid() ) return;

			using var scope = parent.Scene.Push();
			var transform = parent.WorldTransform.ToWorld( local );
			foreach ( var mark in entry.Marks ) PlaceMark( mark, parent, transform.Position, -transform.Rotation.Forward );
			if ( entry.Emitters is {} emitters )
				EmitImpactParticles( emitters, parent, transform, incoming );
			else if ( entry.HasParticles && surface.IsValid() ) PlaceParticles( surface, parent, transform );
			elapsed += EventTime( entry.Duration );
		}
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
		decal.SortLayer = 2;
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
