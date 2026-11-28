using System;
using Button = Sandbox.Mapping.Button;

namespace Source1;

partial class MapLoader
{
	private sealed partial class EntityBuilder
	{
		private readonly Dictionary<MapEntity, Component> ioEntities = [];

		public void RegisterEntityIO( MapEntity entity, Component component ) => ioEntities.Add( entity, component );

		private void WireEntityIO()
		{
			WireFilters();
			foreach ( var (entity, component) in ioEntities )
			{
				if ( component is MovingDoor moving )
				{
					moving.OnOpen = BuildOutput( entity, "OnOpen", true );
					moving.OnClose = BuildOutput( entity, "OnClose", true );
					moving.FullyOpen = BuildOutput( entity, "OnFullyOpen", true );
					moving.FullyClosed = BuildOutput( entity, "OnFullyClosed", true );
					moving.LockedUse = BuildOutput( entity, "OnLockedUse", true );
					moving.BlockedOpening = BuildOutput( entity, "OnBlockedOpening", true );
					moving.BlockedClosing = BuildOutput( entity, "OnBlockedClosing", true );
					moving.UnblockedOpening = BuildOutput( entity, "OnUnblockedOpening", true );
					moving.UnblockedClosing = BuildOutput( entity, "OnUnblockedClosing", true );
				}
				switch ( component )
				{
					case IMapOutputs outputs:
						outputs.Wire( (name, hasUser) => BuildOutput( entity, name, hasUser ) );
						break;
					case PlayerTrigger trigger:
						WireTrigger( entity, trigger );
						break;
					case MapRelay relay:
						BuildOutput( entity, "OnTrigger", true, relay.OnTrigger );
						break;
					case SourceGlassPane pane:
						pane.OnBreak = BuildOutput( entity, "OnBreak", true );
						break;
					case PropAnimation animation:
						if ( animation.GetComponent<PropDamage>( true ) is {} damageableProp )
							damageableProp.OnBreak = BuildOutput( entity, "OnBreak", true );
						break;
					case PropDamage damageable:
						damageable.OnBreak = BuildOutput( entity, "OnBreak", true );
						break;
					case LinearMover mover:
						mover.OnFullyOpen = BuildOutput( entity, "OnFullyOpen", true );
						mover.OnFullyClosed = BuildOutput( entity, "OnFullyClosed", true );
						break;
					case PropDoor prop:
						if ( prop.GetComponent<DoorDamage>( true ) is { } damage ) damage.OnBreak = BuildOutput( entity, "OnBreak", true );
						prop.RotationDone = BuildOutput( entity, "OnRotationDone", true );
						break;
					case Button button:
						button.OnPressed = BuildOutput( entity, "OnPressed", true );
						break;
				}
			}
		}

		private Doo BuildOutput( MapEntity source, string output, bool hasUser, List<MapOutputConnection> relayOutputs = null )
		{
			var actions = new List<(float Delay, Doo.Block Block)>();
			foreach ( var (_, value) in source.KeyValues.Where( entry => entry.Key.Equals( output, StringComparison.OrdinalIgnoreCase ) ) )
			{
				var connection = BspEntityConnection.Read( output, value );
				var targets = ResolveTargets( source, connection.Target ).ToArray();
				if ( targets.Length == 0 )
					Log.Trace( $"Output '{output}' target '{connection.Target}' has no imported native receiver; omitted." );
				var blocks = new List<Doo.Block>();
				foreach ( var target in targets )
				{
					var action = BuildInput( target, connection.Input, hasUser, connection.Parameter, ioEntities[source] );
					if ( action is not null ) blocks.Add( action );
					else Log.Trace( $"Input '{connection.Input}' on '{target.GetType().Name}' is not supported by the engine's public Doo methods; omitted." );
				}
				if ( blocks.Count == 0 ) continue;
				if ( relayOutputs is null && connection.FireCount < 0 )
				{
					foreach ( var block in blocks ) actions.Add( (connection.Delay, block) );
					continue;
				}

				if ( connection.Delay > 0 )
					blocks.Insert( 0, new Doo.DelayBlock { Seconds = new Doo.LiteralExpression { LiteralValue = connection.Delay } } );
				var outputConnection = new MapOutputConnection { Remaining = connection.FireCount, Delay = connection.Delay, Action = new Doo { Body = blocks } };
				if ( relayOutputs is not null )
				{
					relayOutputs.Add( outputConnection );
					continue;
				}

				var limited = ioEntities[source].GameObject.GetOrAddComponent<LimitedMapOutputs>();
				var index = limited.Connections.Count;
				limited.Connections.Add( outputConnection );
				actions.Add( (0, new Doo.InvokeBlock
				{
					InvokeType = Doo.InvokeType.Member,
					TargetComponent = new Doo.TargetComponent { Type = Doo.TargetComponent.TargetType.Direct, ComponentValue = limited },
					Member = $"{typeof( LimitedMapOutputs ).FullName}.{nameof( LimitedMapOutputs.Fire )}",
					Arguments = [new Doo.LiteralExpression { LiteralValue = index }, MapInputs.User( hasUser )]
				}) );
			}
			if ( actions.Count == 0 ) return null;
			var body = new List<Doo.Block>();
			var elapsed = 0f;
			foreach ( var (delay, block) in actions.OrderBy( action => action.Delay ) )
			{
				if ( delay > elapsed )
					body.Add( new Doo.DelayBlock { Seconds = new Doo.LiteralExpression { LiteralValue = delay - elapsed } } );
				elapsed = delay;
				body.Add( block );
			}
			return new Doo { Body = body };
		}

		private IEnumerable<Component> ResolveTargets( MapEntity source, string name )
		{
			return BspEntityConnection.ResolveTargets( source, name, ioEntities.Keys ).Select( entity => ioEntities[entity] );
		}


		private static Doo.Block BuildInput( Component target, string input, bool hasUser, string parameter, Component caller )
		{
			if ( input.Equals( "OpenAwayFrom", StringComparison.OrdinalIgnoreCase ) && target is PropDoor handle )
			{
				return new Doo.InvokeBlock
				{
					InvokeType = Doo.InvokeType.Member,
					TargetComponent = new Doo.TargetComponent { Type = Doo.TargetComponent.TargetType.Direct, ComponentValue = handle },
					Member = $"{typeof( PropDoor ).FullName}.{nameof( PropDoor.OpenAwayFrom )}",
					Arguments = [new Doo.LiteralExpression { LiteralValue = parameter }, MapInputs.User( hasUser ), new Doo.LiteralExpression { LiteralValue = caller.GameObject }]
				};
			}
			if ( target is IMapInput receiver && receiver.BuildInput( input, parameter, hasUser ) is { } action ) return action;
			if ( target is MovingBrush && DoorInputs.Build( target, input, parameter ) is { } doorAction ) return doorAction;
			if ( parameter.Length > 0 && !input.Equals( "Shatter", StringComparison.OrdinalIgnoreCase ) ) return null;
			if ( input.Equals( "Kill", StringComparison.OrdinalIgnoreCase ) )
			{
				return new Doo.InvokeBlock
				{
					InvokeType = Doo.InvokeType.Static,
					Member = $"{typeof( Doo.Methods ).FullName}.{nameof( Doo.Methods.GameObjectDestroy )}",
					Arguments = [new Doo.LiteralExpression { LiteralValue = target.GameObject }]
				};
			}
			if ( target is SourceGlassPane )
			{
				var restore = input.Equals( "Restore", StringComparison.OrdinalIgnoreCase );
				Vector3 area = default;
				if ( !restore && !(input.Equals( "Shatter", StringComparison.OrdinalIgnoreCase ) && Vector3.TryParse( parameter, out area )) )
					return null;
				return new Doo.InvokeBlock
				{
					InvokeType = Doo.InvokeType.Member,
					TargetComponent = new Doo.TargetComponent { Type = Doo.TargetComponent.TargetType.Direct, ComponentValue = target },
					Member = $"{typeof( SourceGlassPane ).FullName}.{(restore ? nameof( SourceGlassPane.Restore ) : nameof( SourceGlassPane.ShatterAt ))}",
					Arguments = restore ? null : [new Doo.LiteralExpression { LiteralValue = area }]
				};
			}
			if ( target is not MovingDoor ) return null;
			var method = input.ToLowerInvariant() switch
			{
				"open" => nameof( BrushDoor.Open ),
				"close" => nameof( BrushDoor.Close ),
				"toggle" => nameof( BrushDoor.Toggle ),
				_ => null
			};
			if ( method is null ) return null;
			return new Doo.InvokeBlock
			{
				InvokeType = Doo.InvokeType.Member,
				TargetComponent = new Doo.TargetComponent { Type = Doo.TargetComponent.TargetType.Direct, ComponentValue = target },
				Member = $"{target.GetType().FullName}.{method}",
				Arguments = method == nameof( BrushDoor.Close ) ? null : [MapInputs.User( hasUser )]
			};
		}
	}
}

internal interface IMapInput
{
	Doo.Block BuildInput( string input, string parameter, bool hasUser );
}

internal interface IMapOutputs
{
	void Wire( Func<string, bool, Doo> output );
}

internal static class MapInputs
{
	internal static Doo.Expression User( bool hasUser ) => hasUser
		? new Doo.VariableExpression { VariableName = "user" }
		: new Doo.LiteralExpression { LiteralValue = default };

	internal static Doo.Block Call( this Component component, string method, params List<Doo.Expression> arguments ) => new Doo.InvokeBlock
	{
		InvokeType = Doo.InvokeType.Member,
		TargetComponent = new Doo.TargetComponent { Type = Doo.TargetComponent.TargetType.Direct, ComponentValue = component },
		Member = $"{component.GetType().FullName}.{method}",
		Arguments = arguments
	};
}
