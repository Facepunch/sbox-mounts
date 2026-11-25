using System;

namespace Overgrowth;

internal sealed class ArterialBleeding : Component, Component.INetworkSnapshot
{
	const float Step = 1f / 30f;
	const float Period = MathF.Tau / 7f;
	float remaining = 10f, nextSound = Period * 0.24f, accumulator;
	int tick;
	BillboardParticle lastDrop;

	void INetworkSnapshot.WriteSnapshot( ref ByteStream writer )
	{
		writer.Write( tick );
		writer.Write( remaining );
		writer.Write( nextSound );
	}

	void INetworkSnapshot.ReadSnapshot( ref ByteStream reader )
	{
		tick = reader.Read<int>();
		remaining = reader.Read<float>();
		nextSound = reader.Read<float>();
	}

	protected override void OnUpdate()
	{
		if ( remaining <= 0f || RabbitGuard.BloodLevel == 0 || GetComponent<RagdollDamping>() is { Settled: true } ) return;
		if ( GetComponent<ModelPhysics>() is not { Active: true } physics ) return;
		bool draw = !Application.IsDedicatedServer;
		accumulator += Time.Delta;
		if ( accumulator < Step ) return;
		var renderer = GetComponent<SkinnedModelRenderer>();
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ||
			CharacterLoader.AverageChainFrame( renderer, "head" ) is not { } head ||
			CharacterLoader.AverageChainFrame( renderer, "torso" ) is not { } torso ) return;
		var point = Vector3.Lerp( head.Position, torso.Position, 0.2f );
		var velocity = CharacterLoader.RagdollVelocity( physics ) ?? Vector3.Zero;
		if ( tick == 0 && draw )
		{
			for ( int i = 0; i < 10; i++ )
				mount.EmitBillboard( "Data/Particles/bloodcloud.xml", point, velocity +
					head.Left * Random.Shared.Float( -200f, 200f ) + head.Up * Random.Shared.Float( 0f, 200f ), RabbitGuard.BloodColor );
		}

		while ( accumulator >= Step && remaining > 0f )
		{
			accumulator -= Step;
			if ( tick++ % 2 == 0 && draw )
			{
				if ( RabbitGuard.BloodLevel == 2 && Random.Shared.Next( 16 ) == 0 && CharacterLoader.FromModel( renderer.Model ) is { } character )
				{
					var direction = new Vector3( 1f, Random.Shared.Float( -1f, 1f ), Random.Shared.Float( -0.3f, 0.3f ) );
					renderer.GameObject.GetOrAddComponent<CutMarks>().AddHeadDrip( character, 1, direction );
				}
				float force = 0.5f + 0.5f * MathF.Sin( (Time.Now - accumulator) * 7f );
				var drop = mount.EmitBillboard( "Data/Particles/blooddrop.xml", point, velocity + head.Up * (remaining * force * 40f), RabbitGuard.BloodColor );
				if ( lastDrop.IsValid() ) lastDrop.Connect( drop );
				lastDrop = drop;
			}
			remaining = Math.Max( 0f, remaining - Step * 0.5f );
			nextSound -= Step;
			if ( nextSound <= 0f )
			{
				nextSound += Period;
				if ( draw ) mount.PlayGroup( "Data/Sounds/blood/artery_squirt.xml", point, remaining * 0.1f );
			}
		}
	}
}
