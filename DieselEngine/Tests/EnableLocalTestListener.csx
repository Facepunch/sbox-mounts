// Test harness only: this installed engine does not automatically listen for local
// connections from a platform-game host. Run once after hosting, before "connect local".
if ( !Networking.IsActive || !Networking.IsHost ) throw new Exception( "Host a game first." );
var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var net = typeof(Networking).GetField( "System", flags ).GetValue( null );
var socketType = typeof(Networking).Assembly.GetType( "Sandbox.Network.TcpSocket" );
var socket = Activator.CreateInstance( socketType, new object[] { "127.0.0.1", 55437 } );
net.GetType().GetMethod( "AddSocket", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic )
	.Invoke( net, new[] { socket } );
return "Local test listener: 55437";
