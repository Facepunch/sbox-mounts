using System;

namespace Source1;

/// <summary>Converts a Source particle system definition to the engine's ordinary particle components.</summary>
internal sealed class SourceParticlePrefabLoader( string sourcePath, PcfFile.Element definition ) : ResourceLoader<BaseMount>
{
	private PrefabFile prefab;
	private readonly SortedSet<string> unsupported = new( StringComparer.OrdinalIgnoreCase );

	protected override object Load()
	{
		unsupported.Clear();
		var builder = new PrefabBuilder().WithName( Path );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			Build( definition, null, 0, [] );
		}

		if ( unsupported.Count > 0 )
			Log.Warning( $"{sourcePath}/{definition.Name}: particle features not imported: {string.Join( ", ", unsupported )}" );

		prefab = builder.Create() ?? throw new InvalidOperationException( $"{Path}: could not create particle prefab." );
		return prefab;
	}

	GameObject Build( PcfFile.Element system, GameObject parent, float delay, HashSet<PcfFile.Element> ancestors )
	{
		if ( ancestors.Count >= 32 || !ancestors.Add( system ) )
			throw new InvalidDataException( "Cyclic or excessively deep particle children." );

		var root = parent is null ? new GameObject( true, system.Name ) : new GameObject( parent, true, system.Name );
		if ( parent is null ) root.AddComponent<TemporaryEffect>().BecomeOrphan = true;

		new SystemImport( this, system, root ).Run( delay );

		foreach ( var child in system.Elements( "children" ) )
		{
			if ( child.Attributes.GetValueOrDefault( "child" ) is PcfFile.Element childSystem )
				Build( childSystem, root, delay + child.Number( "delay" ), ancestors );
		}

		ancestors.Remove( system );
		return root;
	}

	bool TryMaterial( string material, out VmtFile source, out VmtParameters parameters, out SourceTexture texture )
	{
		source = null;
		parameters = default;
		texture = null;
		try
		{
			source = Host.GetSourceMaterial( material ).Source;
			parameters = new VmtParameters( VmtFile.SelectParameters( source ), material );
			var path = parameters.String( "$basetexture" );
			if ( string.IsNullOrWhiteSpace( path ) )
			{
				unsupported.Add( $"{source.ShaderName} material without a base texture: {material}" );
				return false;
			}

			texture = Host.GetSourceTexture( path );
			return true;
		}
		catch ( FileNotFoundException e )
		{
			// Some shipped PCFs keep optional children whose art belongs to another game.
			unsupported.Add( $"missing particle art: {e.FileName}" );
			return false;
		}
	}

	/// <summary>Maps one system's operators. Fallbacks are the Source SDK's unpack defaults.</summary>
	sealed class SystemImport( SourceParticlePrefabLoader loader, PcfFile.Element system, GameObject root )
	{
		readonly ParticleEffect effect = root.AddComponent<ParticleEffect>();
		readonly SourceParticleVelocity velocity = new();
		readonly List<PcfFile.Element> alphaOps = new();
		readonly List<PcfFile.Element> radiusOps = new();
		readonly List<PcfFile.Element> spinOps = new();
		readonly List<PcfFile.Element> rotationOscillations = new();
		readonly List<PcfFile.Element> colorFades = new();
		readonly Dictionary<int, Func<float, (float Min, float Max)>> timedFields = new();
		PcfFile.Element sphere;
		PcfFile.Element box;
		PcfFile.Element sequenceLifetime;
		PcfFile.Element yawFlip;
		ParticleFloat trailLength;
		int sequenceMin;
		int sequenceMax;
		float alphaMin = 1;
		float alphaMax = 1;
		float emissionStart;
		float emissionDuration;
		bool emissionForever;

		SortedSet<string> Unsupported => loader.unsupported;

		public void Run( float delay )
		{
			effect.MaxParticles = Math.Clamp( (int)system.Number( "max_particles", 1000 ), 1, 16384 );
			effect.ApplyShape = true;
			// Source stores the sprite's radius; the engine's scale is its full width.
			effect.InitialScale = MathF.Max( 0, system.Number( "radius", 5 ) * 2 );
			effect.ApplyColor = true;
			effect.ApplyAlpha = true;
			effect.Gradient = ColorValue( system, "color", Color.White );
			effect.ApplyRotation = true;
			effect.InitialRoll = system.Number( "rotation" );
			trailLength = system.Number( "trail_length", 0.1f );
			sequenceMin = (int)system.Number( "sequence_number" );
			sequenceMax = sequenceMin;

			ReadEmissionTiming();
			foreach ( var op in system.Elements( "initializers" ) ) Initializer( op );
			ApplyTimedFields();
			foreach ( var op in system.Elements( "operators" ) ) Operator( op );
			foreach ( var op in system.Elements( "constraints" ) ) Constraint( op );
			foreach ( var op in system.Elements( "forces" ) ) Skip( op );

			ApplyAlpha();
			ApplyRadius();
			ApplyRotation();
			ApplyColorFades();
			ApplyVelocity();

			foreach ( var op in system.Elements( "emitters" ) ) Emitter( op, delay );
			foreach ( var op in system.Elements( "renderers" ) ) Renderer( op );
		}

		static string Name( PcfFile.Element op ) => op.Text( "functionName", op.Name );

		void Skip( PcfFile.Element op, string detail = null )
		{
			Unsupported.Add( detail is null ? Name( op ) : $"{Name( op )} ({detail})" );
		}

		bool ControlPointZero( PcfFile.Element op, string key )
		{
			if ( op.Number( key ) == 0 ) return true;
			Skip( op, "control point other than 0" );
			return false;
		}

		void CheckStrengthFade( PcfFile.Element op )
		{
			if ( op.Number( "operator start fadein" ) != 0 || op.Number( "operator end fadein" ) != 0
				|| op.Number( "operator start fadeout" ) != 0 || op.Number( "operator end fadeout" ) != 0 )
				Skip( op, "operator strength fade" );
		}

		void Initializer( PcfFile.Element op )
		{
			CheckStrengthFade( op );
			switch ( Name( op ).ToLowerInvariant() )
			{
				case "lifetime random":
					timedFields.Remove( LifetimeField );
					effect.Lifetime = Range( op, "lifetime_min", "lifetime_max", 0, "lifetime_random_exponent" );
					break;
				case "trail length random":
					trailLength = Range( op, "length_min", "length_max", 0.1f, "length_random_exponent" );
					break;
				case "radius random":
					timedFields.Remove( RadiusField );
					effect.InitialScale = Range( op, "radius_min", "radius_max", 1, "radius_random_exponent", 2 );
					break;
				case "alpha random":
					timedFields.Remove( AlphaField );
					alphaMin = op.Number( "alpha_min", 255 ) / 255;
					alphaMax = op.Number( "alpha_max", 255 ) / 255;
					break;
				case "color random":
					effect.Gradient = new ParticleGradient
					{
						Type = ParticleGradient.ValueType.Range,
						Evaluation = ParticleGradient.EvaluationType.Particle,
						ConstantA = ColorValue( op, "color1", Color.White ),
						ConstantB = ColorValue( op, "color2", Color.White )
					};
					break;
				case "rotation random":
					timedFields.Remove( RollField );
					var initial = op.Number( "rotation_initial" );
					effect.InitialRoll = new ParticleFloat( initial + op.Number( "rotation_offset_min" ), initial + op.Number( "rotation_offset_max", 360 ) );
					break;
				case "rotation yaw flip random":
					yawFlip = op;
					break;
				case "sequence random":
					sequenceMin = (int)op.Number( "sequence_min" );
					sequenceMax = (int)op.Number( "sequence_max" );
					break;
				case "lifetime from sequence":
					sequenceLifetime = op;
					break;
				case "velocity noise":
					if ( ControlPointZero( op, "Control Point Number" ) ) velocity.AddNoise( op );
					break;
				case "position within sphere random":
					sphere = op;
					velocity.AddSphere( op );
					if ( op.Number( "control_point_number" ) != 0 || op.Number( "create in model" ) != 0
						|| op.Boolean( "randomly distribute to highest supplied Control Point" ) || op.Number( "scale cp (distance/speed/local speed)", -1 ) >= 0 )
						Skip( op, "control point or model constraints" );
					break;
				case "position within box random":
					if ( ControlPointZero( op, "control point number" ) ) box = op;
					break;
				case "position modify offset random":
					if ( ControlPointZero( op, "control_point_number" ) ) PositionOffset( op );
					break;
				case "remap noise to scalar":
					// Approximation: coherent noise becomes a uniform random value in its output range.
					var noiseField = (int)op.Number( "output field", RadiusField );
					if ( IsScalarField( noiseField ) ) SetField( noiseField, op.Number( "output minimum" ), op.Number( "output maximum", 1 ) );
					else Skip( op, $"output field {noiseField}" );
					break;
				case "remap initial scalar":
					RemapCreationTime( op );
					break;
				default:
					Skip( op );
					break;
			}
		}

		const int LifetimeField = 1;
		const int RadiusField = 3;
		const int RollField = 4;
		const int AlphaField = 7;

		static bool IsScalarField( int field ) => field is LifetimeField or RadiusField or RollField or AlphaField;

		void ReadEmissionTiming()
		{
			var emitter = system.Elements( "emitters" ).FirstOrDefault( op => Name( op ) is "emit_continuously" or "emit_instantaneously" );
			if ( emitter is null ) return;
			emissionStart = emitter.Number( "emission_start_time" );
			if ( Name( emitter ) != "emit_continuously" ) return;
			emissionDuration = emitter.Number( "emission_duration" );
			emissionForever = emissionDuration <= 0;
		}

		(float Min, float Max) CurrentField( int field )
		{
			static (float, float) Bounds( ParticleFloat value, float scale ) => value.Type switch
			{
				ParticleFloat.ValueType.Constant => (value.ConstantValue * scale, value.ConstantValue * scale),
				ParticleFloat.ValueType.Range => (value.ConstantA * scale, value.ConstantB * scale),
				_ => (value.Evaluate( 0, 0 ) * scale, value.Evaluate( 1, 1 ) * scale)
			};

			return field switch
			{
				LifetimeField => Bounds( effect.Lifetime, 1 ),
				RadiusField => Bounds( effect.InitialScale, 0.5f ),
				RollField => Bounds( effect.InitialRoll, 1 ),
				_ => (alphaMin, alphaMax)
			};
		}

		/// <summary>Sets a scalar initial value in Source units: seconds, radius, degrees or 0-1 alpha.</summary>
		void SetField( int field, float min, float max )
		{
			timedFields.Remove( field );
			switch ( field )
			{
				case LifetimeField:
					effect.Lifetime = new ParticleFloat( min, max );
					break;
				case RadiusField:
					effect.InitialScale = new ParticleFloat( min * 2, max * 2 );
					break;
				case RollField:
					effect.InitialRoll = new ParticleFloat( min, max );
					break;
				case AlphaField:
					alphaMin = Math.Clamp( min, 0, 1 );
					alphaMax = Math.Clamp( max, 0, 1 );
					break;
			}
		}

		/// <summary>C_INIT_RemapScalar from the particle's creation time, which is the system's age at emission.</summary>
		void RemapCreationTime( PcfFile.Element op )
		{
			var field = (int)op.Number( "output field", RadiusField );
			if ( (int)op.Number( "input field", 8 ) != 8 || !IsScalarField( field ) )
			{
				Skip( op, "input other than creation time, or unsupported output field" );
				return;
			}

			var start = op.Number( "emitter lifetime start time (seconds)", -1 );
			var end = op.Number( "emitter lifetime end time (seconds)", -1 );
			var inputMin = op.Number( "input minimum" );
			var inputMax = op.Number( "input maximum", 1 );
			var outputMin = op.Number( "output minimum" );
			var outputMax = op.Number( "output maximum", 1 );
			if ( field == AlphaField )
			{
				outputMin = Math.Clamp( outputMin, 0, 1 );
				outputMax = Math.Clamp( outputMax, 0, 1 );
			}
			else if ( field == RollField )
			{
				outputMin = outputMin.RadianToDegree();
				outputMax = outputMax.RadianToDegree();
			}

			var scale = op.Boolean( "output is scalar of initial random range" );
			var activeRange = op.Boolean( "only active within specified input range" );
			var current = CurrentField( field );
			Func<float, (float Min, float Max)> previous = timedFields.TryGetValue( field, out var earlier ) ? earlier : _ => current;
			timedFields[field] = time =>
			{
				var value = previous( time );
				if ( start != -1 && end != -1 && (time < start || time >= end) ) return value;
				if ( activeRange && (time < inputMin || time > inputMax) ) return value;
				var output = SourceParticleMath.RemapClamped( time, inputMin, inputMax, outputMin, outputMax );
				return scale ? (value.Min * output, value.Max * output) : (output, output);
			};
		}

		/// <summary>
		/// Radius and roll follow the emitter's progress, as Source's creation time does. Lifetime and alpha are
		/// evaluated per particle in the engine, so they take the range of values over the emission.
		/// </summary>
		void ApplyTimedFields()
		{
			foreach ( var (field, value) in timedFields.ToList() )
			{
				if ( emissionDuration <= 0 || emissionForever )
				{
					// Instantaneous emitters create everything at the start time; endless ones settle after their windows.
					var settled = value( emissionForever ? emissionStart + 1000 : emissionStart );
					SetField( field, settled.Min, settled.Max );
					continue;
				}

				float Time( float progress ) => emissionStart + progress * emissionDuration;
				if ( field is RadiusField or RollField )
				{
					var scale = field == RadiusField ? 2 : 1;
					var curve = new ParticleFloat
					{
						Type = ParticleFloat.ValueType.CurveRange,
						Evaluation = ParticleFloat.EvaluationType.Life,
						CurveA = SourceParticleMath.Sample( u => value( Time( u ) ).Min * scale ),
						CurveB = SourceParticleMath.Sample( u => value( Time( u ) ).Max * scale )
					};
					if ( field == RadiusField ) effect.InitialScale = curve;
					else effect.InitialRoll = curve;
					continue;
				}

				var samples = Enumerable.Range( 0, SourceParticleMath.Samples + 1 ).Select( i => value( Time( i / (float)SourceParticleMath.Samples ) ) ).ToList();
				SetField( field, samples.Min( s => s.Min ), samples.Max( s => s.Max ) );
			}
			timedFields.Clear();
		}

		void Operator( PcfFile.Element op )
		{
			CheckStrengthFade( op );
			switch ( Name( op ).ToLowerInvariant() )
			{
				case "movement basic":
					effect.Force = true;
					effect.ForceDirection = Vector( op, "gravity" );
					effect.ForceSpace = ParticleEffect.SimulationSpace.World;
					effect.Damping = SourceParticleMath.DragToDamping( op.Number( "drag" ) );
					break;
				case "lifespan decay":
					break;
				case "alpha fade in random":
				case "alpha fade out random":
				case "alpha fade and decay":
					alphaOps.Add( op );
					break;
				case "radius scale":
					radiusOps.Add( op );
					break;
				case "rotation spin roll":
					spinOps.Add( op );
					break;
				case "oscillate scalar":
					if ( (int)op.Number( "oscillation field", 7 ) == 4 && op.Boolean( "proportional 0/1", true ) && op.Boolean( "start/end proportional", true ) )
						rotationOscillations.Add( op );
					else
						Skip( op, "field other than rotation, or absolute time" );
					break;
				case "color fade":
					colorFades.Add( op );
					break;
				case "movement lock to control point":
					if ( !ControlPointZero( op, "control_point_number" ) ) break;
					var fadeStart = op.Number( "start_fadeout_min", 1 );
					effect.LocalSpace = fadeStart >= 1 ? 1 : LifeCurve( (0, 1), (fadeStart, 1), (op.Number( "end_fadeout_max", 1 ), 0) );
					break;
				default:
					Skip( op );
					break;
			}
		}

		void Constraint( PcfFile.Element op )
		{
			CheckStrengthFade( op );
			if ( !Name( op ).Equals( "Collision via traces", StringComparison.OrdinalIgnoreCase ) )
			{
				Skip( op );
				return;
			}

			// Approximation: the engine traces one radius for every particle and has its own slide model.
			effect.Collision = true;
			effect.Bounce = op.Number( "amount of bounce" );
			effect.Friction = 1 - Math.Clamp( op.Number( "amount of slide" ), 0, 1 );
			effect.CollisionRadius = MathF.Max( 0.01f, op.Number( "radius scale", 1 ) * system.Number( "radius", 5 ) );
			if ( op.Boolean( "kill particle on collision" ) ) effect.DieOnCollisionChance = 1;
		}

		void PositionOffset( PcfFile.Element op )
		{
			var offset = root.AddComponent<ParticlePositionOffset>();
			offset.LocalSpace = op.Boolean( "offset in local space 0/1" );
			offset.ScaleWithSize = op.Boolean( "offset proportional to radius 0/1" );
			// Source scales by radius; the engine's initial size is the full width.
			offset.Offset = VectorRange( Vector( op, "offset min" ), Vector( op, "offset max" ), offset.ScaleWithSize ? 0.5f : 1 );
		}

		void ApplyAlpha()
		{
			var fadesA = new List<Func<float, float?>>();
			var fadesB = new List<Func<float, float?>>();
			var lifetime = effect.Lifetime;
			var meanLifetime = MathF.Max( 0.001f, lifetime.Evaluate( 0.5f, 0.5f ) );

			foreach ( var op in alphaOps )
			{
				var name = Name( op ).ToLowerInvariant();
				if ( name == "alpha fade and decay" )
				{
					var decay = SourceParticleMath.FadeAndDecay( op.Number( "start_alpha", 1 ), op.Number( "end_alpha" ),
						op.Number( "start_fade_in_time" ), op.Number( "end_fade_in_time", 0.5f ),
						op.Number( "start_fade_out_time", 0.5f ), op.Number( "end_fade_out_time", 1 ) );
					fadesA.Add( decay );
					fadesB.Add( decay );
					continue;
				}

				var fadeIn = name == "alpha fade in random";
				var prefix = fadeIn ? "fade in time" : "fade out time";
				var min = op.Number( $"{prefix} min", 0.25f );
				var max = op.Number( $"{prefix} max", 0.25f );
				if ( op.Number( $"{prefix} exponent", 1 ) != 1 ) Skip( op, "random exponent" );
				if ( !op.Boolean( "proportional 0/1", true ) )
				{
					// Seconds; exact only when the lifetime is constant.
					if ( lifetime.Type != ParticleFloat.ValueType.Constant ) Skip( op, "seconds with a random lifetime, uses the mean lifetime" );
					min /= meanLifetime;
					max /= meanLifetime;
				}

				if ( fadeIn )
				{
					fadesA.Add( SourceParticleMath.FadeIn( min ) );
					fadesB.Add( SourceParticleMath.FadeIn( max ) );
					continue;
				}

				var ease = op.Boolean( "ease in and out", true );
				var bias = op.Number( "fade bias", 0.5f );
				fadesA.Add( SourceParticleMath.FadeOut( min, ease, bias ) );
				fadesB.Add( SourceParticleMath.FadeOut( max, ease, bias ) );
			}

			effect.Alpha = new ParticleFloat
			{
				Type = ParticleFloat.ValueType.CurveRange,
				Evaluation = ParticleFloat.EvaluationType.Life,
				CurveA = SourceParticleMath.Timeline( fadesA, alphaMin ),
				CurveB = SourceParticleMath.Timeline( fadesB, alphaMax )
			};
		}

		void ApplyRadius()
		{
			if ( radiusOps.Count == 0 ) return;

			var scales = radiusOps.Select( op => SourceParticleMath.RadiusScale(
				op.Number( "start_time" ), op.Number( "end_time", 1 ),
				op.Number( "radius_start_scale", 1 ), op.Number( "radius_end_scale", 1 ),
				op.Boolean( "ease_in_and_out" ), op.Number( "scale_bias", 0.5f ) ) ).ToList();

			effect.Scale = new ParticleFloat
			{
				Type = ParticleFloat.ValueType.Curve,
				Evaluation = ParticleFloat.EvaluationType.Life,
				CurveA = SourceParticleMath.Timeline( scales, 1 )
			};
		}

		void ApplyRotation()
		{
			if ( spinOps.Count == 0 && rotationOscillations.Count == 0 ) return;

			// The random oscillation rate spans the two curves; frequency and window use their midpoints.
			float Rate( float life, bool maximum )
			{
				var rate = 0f;
				foreach ( var op in spinOps )
					rate += SourceParticleMath.SpinRate( life, op.Number( "spin_rate_degrees" ), op.Number( "spin_stop_time" ), op.Number( "spin_rate_min" ) );

				foreach ( var op in rotationOscillations )
				{
					var amplitude = op.Number( maximum ? "oscillation rate max" : "oscillation rate min" );
					var frequency = (op.Number( "oscillation frequency min", 1 ) + op.Number( "oscillation frequency max", 1 )) * 0.5f;
					var start = (op.Number( "start time min" ) + op.Number( "start time max" )) * 0.5f;
					var end = (op.Number( "end time min", 1 ) + op.Number( "end time max", 1 )) * 0.5f;
					rate += SourceParticleMath.OscillationRate( life, amplitude, frequency, op.Number( "oscillation multiplier", 2 ),
						op.Number( "oscillation start phase", 0.5f ), start, end );
				}

				return rate;
			}

			effect.AngularVelocity = new ParticleVector3
			{
				Z = new ParticleFloat
				{
					Type = ParticleFloat.ValueType.CurveRange,
					Evaluation = ParticleFloat.EvaluationType.Life,
					CurveA = SourceParticleMath.Sample( life => Rate( life, false ) ),
					CurveB = SourceParticleMath.Sample( life => Rate( life, true ) )
				}
			};
		}

		void ApplyColorFades()
		{
			if ( colorFades.Count == 0 ) return;
			if ( effect.Gradient.Type == ParticleGradient.ValueType.Range ) Unsupported.Add( "Color Fade (from the midpoint of the random initial color)" );

			var startColor = effect.Gradient.Evaluate( 0, 0.5f );
			var frames = new Gradient.ColorFrame[65];
			var color = startColor;
			for ( var i = 0; i < frames.Length; i++ )
			{
				var time = i / 64f;
				foreach ( var fade in colorFades )
				{
					var t = SourceParticleMath.Window( time, fade.Number( "fade_start_time" ), fade.Number( "fade_end_time", 1 ) );
					if ( t is null ) continue;
					var amount = fade.Boolean( "ease_in_and_out", true ) ? SourceParticleMath.SimpleSpline( t.Value ) : t.Value;
					color = Color.Lerp( startColor, ColorValue( fade, "color_fade", Color.White ).WithAlpha( startColor.a ), amount );
				}
				frames[i] = new Gradient.ColorFrame( time, color );
			}

			effect.Gradient = new ParticleGradient
			{
				Type = ParticleGradient.ValueType.Gradient,
				Evaluation = ParticleGradient.EvaluationType.Life,
				GradientA = new Gradient( frames )
			};
		}

		void ApplyVelocity()
		{
			if ( velocity.HasWorld ) effect.InitialVelocity = VectorRange( velocity.WorldMin, velocity.WorldMax );
			if ( !velocity.HasLocal ) return;

			var local = root.AddComponent<ParticleInitialVelocity>();
			local.LocalSpace = true;
			local.Velocity = VectorRange( velocity.LocalMin, velocity.LocalMax );
		}

		void Emitter( PcfFile.Element op, float delay )
		{
			CheckStrengthFade( op );
			var name = Name( op );
			if ( name is not ("emit_continuously" or "emit_instantaneously") )
			{
				Skip( op );
				return;
			}

			var emitter = Shape();
			var continuous = name == "emit_continuously";
			var duration = op.Number( "emission_duration" );
			emitter.Loop = continuous && duration <= 0;
			emitter.Duration = emitter.Loop ? 86400 : MathF.Max( 0, duration );
			emitter.Delay = delay + op.Number( "emission_start_time" );
			emitter.Rate = continuous ? Math.Clamp( op.Number( "emission_rate", 100 ), 0, 100000 ) : 0;
			emitter.Burst = continuous ? system.Number( "initial_particles" ) : Math.Clamp( op.Number( "num_to_emit", 100 ), 0, effect.MaxParticles );
		}

		ParticleEmitter Shape()
		{
			if ( box is not null && sphere is null )
			{
				var min = Vector( box, "min" );
				var max = Vector( box, "max" );
				var shape = new GameObject( root, true, "Box" );
				shape.LocalPosition = (min + max) * 0.5f;
				var boxEmitter = shape.AddComponent<ParticleBoxEmitter>();
				boxEmitter.Size = max - min;
				return boxEmitter;
			}

			if ( box is not null ) Skip( box, "combined with a sphere" );

			var emitter = root.AddComponent<ParticleSphereEmitter>();
			emitter.Velocity = 0;
			emitter.Radius = sphere?.Number( "distance_max" ) ?? 0;
			emitter.MinimumRadius = sphere?.Number( "distance_min" ) ?? 0;
			emitter.NormalizeBias = true;
			if ( sphere is null ) return emitter;

			emitter.RadialVelocity = Range( sphere, "speed_min", "speed_max", 0, "speed_random_exponent" );
			emitter.DistanceBias = Vector( sphere, "distance_bias", Vector3.One );
			emitter.LocalDirection = sphere.Boolean( "bias in local system" );
			var absolute = Vector( sphere, "distance_bias_absolute_value" );
			emitter.OneSidedAxes = (absolute.x != 0 ? ParticleSphereEmitter.EmitAxes.X : 0)
				| (absolute.y != 0 ? ParticleSphereEmitter.EmitAxes.Y : 0)
				| (absolute.z != 0 ? ParticleSphereEmitter.EmitAxes.Z : 0);
			return emitter;
		}

		void Renderer( PcfFile.Element op )
		{
			CheckStrengthFade( op );
			var name = Name( op );
			if ( name is not "render_animated_sprites" and not "render_sprite_trail" )
			{
				Skip( op );
				return;
			}

			var material = system.Text( "material" );
			if ( string.IsNullOrWhiteSpace( material ) ) return;
			if ( !loader.TryMaterial( material, out var source, out var parameters, out var sourceTexture ) ) return;

			var trail = name == "render_sprite_trail";
			var rateAsFps = op.Boolean( "use animation rate as FPS" );
			if ( sourceTexture.Sheet is { } sheet )
			{
				sequenceMin = Math.Clamp( sequenceMin, 0, sheet.Sequences.Length - 1 );
				sequenceMax = Math.Clamp( sequenceMax, sequenceMin, sheet.Sequences.Length - 1 );
				effect.SheetSequence = true;
				effect.SequenceTime = 0;
				effect.SequenceId = new ParticleFloat( sequenceMin, sequenceMax + 0.999f );
				effect.SequenceFitToLifetime = op.Boolean( "animation_fit_lifetime" );
				var frames = sheet.Sequences[sequenceMin].Frames.Length;
				var rate = op.Number( "animation rate", trail ? 0.1f : 1 );
				effect.SequenceSpeed = effect.SequenceFitToLifetime ? 1 : rateAsFps ? rate / frames : rate;
				if ( sequenceLifetime is not null ) effect.Lifetime = frames / MathF.Max( 0.01f, sequenceLifetime.Number( "Frames Per Second", 30 ) );
				var mixedFrameCounts = Enumerable.Range( sequenceMin, sequenceMax - sequenceMin + 1 ).Any( i => sheet.Sequences[i].Frames.Length != frames );
				if ( mixedFrameCounts && (sequenceLifetime is not null || rateAsFps) ) Unsupported.Add( "sequences with different frame counts (timed from the first)" );
			}

			if ( source.ShaderName.Equals( "Refract", StringComparison.OrdinalIgnoreCase ) )
			{
				Unsupported.Add( $"Refract material, not drawn ({material})" );
				return;
			}

			var renderer = AddRenderer( source, parameters, sourceTexture );
			var overbright = parameters.Float( "$overbrightfactor", 1 );
			if ( overbright != 1 ) effect.Brightness = overbright;

			// SpriteCard $addself blends ONE, INVSRCALPHA: the alpha-blended sprite plus an additive copy of itself.
			ParticleSpriteRenderer additiveSelf = null;
			if ( parameters.Float( "$addself" ) != 0 && !renderer.Additive )
			{
				additiveSelf = AddRenderer( source, parameters, sourceTexture );
				additiveSelf.Additive = true;
			}

			if ( trail )
			{
				// Source draws a trail as wide as the radius, not the diameter.
				effect.InitialScale = Scaled( effect.InitialScale, 0.5f );
				foreach ( var r in new[] { renderer, additiveSelf }.Where( r => r is not null ) )
				{
					r.VelocityStretch = true;
					r.StretchTime = trailLength;
					r.StretchLengthLimits = new( op.Number( "min length" ), op.Number( "max length", 2000 ) );
					r.StretchFadeIn = op.Number( "length fade in time" );
				}
				return;
			}

			var orientation = (int)op.Number( "orientation_type" );
			var alignment = ParticleSpriteRenderer.BillboardAlignment.LookAtCamera;
			if ( orientation == 1 )
			{
				alignment = ParticleSpriteRenderer.BillboardAlignment.RotateToCamera;
			}
			else if ( orientation == 2 && op.Number( "orientation control point", -1 ) < 0 )
			{
				// Without an orientation control point Source uses the world XY plane.
				alignment = ParticleSpriteRenderer.BillboardAlignment.Particle;
				effect.InitialPitch = 90;
				if ( yawFlip is not null ) effect.InitialYaw = Flip( Math.Clamp( yawFlip.Number( "Flip Percentage", 0.5f ), 0, 1 ) );
			}
			else if ( orientation != 0 )
			{
				Unsupported.Add( $"{name} orientation {orientation}" );
			}

			renderer.Alignment = alignment;
			if ( additiveSelf is not null ) additiveSelf.Alignment = alignment;
		}

		ParticleSpriteRenderer AddRenderer( VmtFile source, VmtParameters parameters, SourceTexture texture )
		{
			var renderer = root.AddComponent<ParticleSpriteRenderer>();
			renderer.Sprite = Sprite.FromTexture( texture.GetFrame( 0 ) );
			renderer.Sprite.EmbeddedResource = new Sandbox.Resources.EmbeddedResource { ResourceCompiler = "embed" };
			renderer.Additive = parameters.Boolean( "$additive" ) || parameters.Integer( "$spriterendermode", 0 ) == 5;
			renderer.Lighting = source.ShaderName.Equals( "VertexLitGeneric", StringComparison.OrdinalIgnoreCase );
			renderer.DepthFeather = parameters.Float( "$depthblendscale" );
			return renderer;
		}
	}

	static ParticleFloat Range( PcfFile.Element op, string min, string max, float fallback, string exponent = null, float scale = 1 )
	{
		var a = op.Number( min, fallback ) * scale;
		var b = op.Number( max, fallback ) * scale;
		var power = exponent is null ? 1 : Math.Max( 0, op.Number( exponent, 1 ) );
		if ( power == 1 ) return new( a, b );

		return new ParticleFloat
		{
			Type = ParticleFloat.ValueType.Curve,
			Evaluation = ParticleFloat.EvaluationType.Seed,
			CurveA = SourceParticleMath.Sample( u => float.Lerp( a, b, MathF.Pow( u, power ) ) )
		};
	}

	static ParticleFloat Scaled( ParticleFloat value, float scale )
	{
		if ( value.Type is ParticleFloat.ValueType.Constant or ParticleFloat.ValueType.Range )
		{
			value.ConstantA *= scale;
			value.ConstantB *= scale;
			return value;
		}

		static Curve Scale( Curve curve, float scale ) => new( curve.Frames.Select( f => f.WithValue( f.Value * scale ) ) );
		value.CurveA = Scale( value.CurveA, scale );
		if ( value.Type == ParticleFloat.ValueType.CurveRange ) value.CurveB = Scale( value.CurveB, scale );
		return value;
	}

	// 180 degrees for the given fraction of particles, picked by each particle's seed.
	static ParticleFloat Flip( float chance ) => new()
	{
		Type = ParticleFloat.ValueType.Curve,
		Evaluation = ParticleFloat.EvaluationType.Seed,
		CurveA = new Curve( new Curve.Frame( 0, 0 ) { Mode = Curve.HandleMode.Stepped }, new Curve.Frame( 1 - chance, 180 ) { Mode = Curve.HandleMode.Stepped } )
	};

	static Vector3 Vector( PcfFile.Element op, string key, Vector3 fallback = default ) => op.Vector( key ) is { Length: 3 } v ? new( v[0], v[1], v[2] ) : fallback;

	static ParticleVector3 VectorRange( Vector3 a, Vector3 b, float scale = 1 )
	{
		a *= scale;
		b *= scale;
		return new() { X = new( a.x, b.x ), Y = new( a.y, b.y ), Z = new( a.z, b.z ) };
	}

	static Color ColorValue( PcfFile.Element op, string key, Color fallback ) => op.Attributes.GetValueOrDefault( key ) is byte[] { Length: 4 } c
		? new Color( c[0] / 255f, c[1] / 255f, c[2] / 255f, c[3] / 255f )
		: fallback;

	static ParticleFloat LifeCurve( params (float Time, float Value)[] points ) => new()
	{
		Type = ParticleFloat.ValueType.Curve,
		Evaluation = ParticleFloat.EvaluationType.Life,
		CurveA = new Curve( points.GroupBy( p => Math.Clamp( p.Time, 0, 1 ) ).OrderBy( g => g.Key )
			.Select( g => new Curve.Frame( g.Key, g.Last().Value ) { Mode = Curve.HandleMode.Linear } ) )
	};

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( prefab );
		prefab = null;
		base.Shutdown();
	}
}

/// <summary>Initial velocity contributions of Source initializers, split by the space they are applied in.</summary>
internal sealed class SourceParticleVelocity
{
	public Vector3 WorldMin { get; private set; }
	public Vector3 WorldMax { get; private set; }
	public Vector3 LocalMin { get; private set; }
	public Vector3 LocalMax { get; private set; }
	public bool HasWorld => WorldMin != Vector3.Zero || WorldMax != Vector3.Zero;
	public bool HasLocal => LocalMin != Vector3.Zero || LocalMax != Vector3.Zero;

	/// <summary>C_INIT_InitialVelocityNoise, with its noise treated as uniformly random within the output range.</summary>
	public void AddNoise( PcfFile.Element op )
	{
		var min = Read( op, "output minimum", Vector3.Zero );
		var max = Read( op, "output maximum", Vector3.One );
		if ( op.Boolean( "Apply Velocity in Local Space (0/1)" ) ) AddLocal( min, max );
		else AddWorld( min, max );
	}

	/// <summary>C_INIT_CreateWithinSphere's speed along the control point's forward, right and up vectors.</summary>
	public void AddSphere( PcfFile.Element op )
	{
		var min = Read( op, "speed_in_local_coordinate_system_min", Vector3.Zero );
		var max = Read( op, "speed_in_local_coordinate_system_max", Vector3.Zero );
		// Source's y here is the right vector; the engine's local y points left.
		AddLocal( new Vector3( min.x, -max.y, min.z ), new Vector3( max.x, -min.y, max.z ) );
	}

	void AddWorld( Vector3 min, Vector3 max )
	{
		WorldMin += min;
		WorldMax += max;
	}

	void AddLocal( Vector3 min, Vector3 max )
	{
		LocalMin += min;
		LocalMax += max;
	}

	static Vector3 Read( PcfFile.Element op, string key, Vector3 fallback ) => op.Vector( key ) is { Length: 3 } v ? new( v[0], v[1], v[2] ) : fallback;
}

/// <summary>Source particle operator math (Source SDK particles/builtin_particle_ops.cpp), sampled over normalized particle life.</summary>
internal static class SourceParticleMath
{
	public const int Samples = 64;

	/// <summary>
	/// C_OP_BasicMovement scales velocity by (1 - drag) every 1/30 s. The engine's damping removes
	/// damping * dt of the speed per step, which is the same exponential decay for speeds of 100 units/s and above.
	/// </summary>
	public static float DragToDamping( float drag )
	{
		if ( drag <= 0 ) return 0;
		if ( drag >= 1 ) return 1000;
		return -30 * MathF.Log( 1 - drag );
	}

	public static float SimpleSpline( float x ) => x * x * (3 - 2 * x);

	/// <summary>Source's RemapValClamped.</summary>
	public static float RemapClamped( float value, float inputMin, float inputMax, float outputMin, float outputMax )
	{
		if ( inputMin == inputMax ) return value >= inputMax ? outputMax : outputMin;
		var t = Math.Clamp( (value - inputMin) / (inputMax - inputMin), 0, 1 );
		return outputMin + (outputMax - outputMin) * t;
	}

	/// <summary>The rational bias approximation Source uses in its SIMD operators.</summary>
	public static float Bias( float x, float bias ) => x / ((1 / bias - 2) * (1 - x) + 1);

	/// <summary>Where life falls within [start, end) as 0-1, or null outside it.</summary>
	public static float? Window( float life, float start, float end )
	{
		if ( life < start || life >= end ) return null;
		return (life - start) / (end - start);
	}

	/// <summary>C_OP_FadeIn: alpha multiplier while the particle is younger than its fade time.</summary>
	public static Func<float, float?> FadeIn( float time ) => life => life < time ? SimpleSpline( Math.Clamp( life / time, 0, 1 ) ) : null;

	/// <summary>C_OP_FadeOut: alpha multiplier during the final <paramref name="time"/> of the particle's life.</summary>
	public static Func<float, float?> FadeOut( float time, bool easeInAndOut, float bias )
	{
		time = MathF.Max( time, float.Epsilon );
		var start = 1 - time;
		return life =>
		{
			if ( life <= start ) return null;
			var t = Math.Clamp( (life - start) / time, 0, 1 );
			if ( easeInAndOut ) return MathF.Max( 0, 1 - SimpleSpline( t ) );
			return 1 - Bias( t, bias == 0 ? 0.5f : bias );
		};
	}

	/// <summary>C_OP_FadeAndKill, including its parameter validation.</summary>
	public static Func<float, float?> FadeAndDecay( float startAlpha, float endAlpha, float startFadeIn, float endFadeIn, float startFadeOut, float endFadeOut )
	{
		endFadeIn = MathF.Max( endFadeIn, startFadeIn );
		endFadeOut = MathF.Max( endFadeOut, startFadeOut );
		if ( startFadeOut < startFadeIn ) (startFadeIn, startFadeOut) = (startFadeOut, startFadeIn);
		if ( endFadeOut < endFadeIn ) (endFadeIn, endFadeOut) = (endFadeOut, endFadeIn);

		return life =>
		{
			float? alpha = null;
			if ( Window( life, startFadeIn, endFadeIn ) is { } fadeIn ) alpha = float.Lerp( startAlpha, 1, SimpleSpline( fadeIn ) );
			if ( Window( life, startFadeOut, endFadeOut ) is { } fadeOut ) alpha = float.Lerp( 1, endAlpha, SimpleSpline( fadeOut ) );
			return alpha;
		};
	}

	/// <summary>C_OP_InterpolateRadius: radius multiplier within its window.</summary>
	public static Func<float, float?> RadiusScale( float start, float end, float startScale, float endScale, bool easeInAndOut, float bias )
	{
		if ( end <= start ) return _ => null;
		return life =>
		{
			if ( Window( life, start, end ) is not { } t ) return null;
			t = easeInAndOut ? SimpleSpline( t ) : bias == 0.5f ? t : Bias( t, bias );
			return float.Lerp( startScale, endScale, t );
		};
	}

	/// <summary>
	/// C_OP_Spin in degrees per second. Source converts the degrees to radians and then multiplies by 2 pi,
	/// slows linearly to zero over spin_stop_time of the life, and never turns slower than spin_rate_min.
	/// </summary>
	public static float SpinRate( float life, float rateDegrees, float stopTime, float minimumDegrees )
	{
		if ( rateDegrees == 0 ) return 0;
		var rate = rateDegrees * MathF.Tau;
		var minimum = MathF.Abs( minimumDegrees ) * MathF.Tau;
		var scale = stopTime == 0 ? 1 : MathF.Max( 0, 1 - life / stopTime );
		return MathF.Max( minimum, rate * scale );
	}

	/// <summary>C_OP_OscillateScalar on rotation (proportional): added rate, converted from radians to degrees per second.</summary>
	public static float OscillationRate( float life, float rate, float frequency, float multiplier, float phase, float start, float end )
	{
		if ( life < start || life >= end ) return 0;
		return rate.RadianToDegree() * MathF.Sin( MathF.Tau * (multiplier * life * frequency + phase) );
	}

	/// <summary>
	/// Runs operators in order over life, holding the last written value between their windows like Source's
	/// particle attributes do. Steps finer than the output so a held value matches its window's end.
	/// </summary>
	public static Curve Timeline( IReadOnlyList<Func<float, float?>> operators, float initial )
	{
		const int steps = 16;
		var held = 1f;
		var values = new float[Samples * steps + 1];
		for ( var i = 0; i < values.Length; i++ )
		{
			var life = i / (float)(values.Length - 1);
			foreach ( var op in operators )
			{
				if ( op( life ) is { } value ) held = value;
			}
			values[i] = initial * held;
		}
		return Sample( life => values[(int)MathF.Round( life * (values.Length - 1) )] );
	}

	public static Curve Sample( Func<float, float> value )
	{
		var frames = new Curve.Frame[Samples + 1];
		for ( var i = 0; i <= Samples; i++ )
		{
			var life = i / (float)Samples;
			frames[i] = new Curve.Frame( life, value( life ) ) { Mode = Curve.HandleMode.Linear };
		}
		return new Curve( frames );
	}
}
