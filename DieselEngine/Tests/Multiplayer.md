# Packaged multiplayer checks

Use disposable game clients, with no DieselEngine source project loaded. Keep the editor's
scene intact. Install a freshly built package before launching either process.

1. Launch the host with `-mcp +net_local_port 55437`. If startup stalls during compilation,
   setting `DOTNET_PROCESSOR_COUNT=4` for that process helped on the development machine.
2. Run `game facepunch.sandbox`, wait for the game to finish loading, then
   `map mount://payday/levels/bank/world.world.scene`.
3. Run `VerifyNetworkMapReady.csx` on the host. It asserts the nested relay is actually
   network-spawned and its door snapshots match the map.
4. If the installed engine does not open a loopback listener for platform-game hosts, run
   `EnableLocalTestListener.csx` once. This is test transport setup, not mount behavior.
5. Launch a second client with `-mcp -joinlocal +net_local_port 55437 +instanceid 2`.
   `connect local` reconnects through this TCP listener; an IP address uses a different transport.
6. Run `VerifyNetworkMapReady.csx` and `VerifyClientMap.csx` on the client.
7. Run `VerifyRemoteDoor.csx` on the client. It temporarily moves the owned player to a usable
   door, sends a real remote press, checks the returned state, then attempts a request naming
   the host's player. It restores the player's position afterwards. The foreign-player check
   is a rejection smoke test; it does not independently isolate ownership from distance checks.
   `VerifyRemotePhone.csx` similarly tests an actual remote phone press.
8. Disconnect/reconnect the client and repeat `VerifyNetworkMapReady.csx`; compare the changed
   door against the host using `InspectNetworkMap.csx` or its deterministic ID.
9. Restart the map on the host and repeat the ready checks on both processes. The `map` command
   recreates the session in this engine: reopen the test listener and reconnect the client.
   This checks restart/reconnect, not seamless reload with a continuously connected client.

`VerifyNetworkMapReady.csx` compares door, phone, camera and applied breakable states against
the received relay values. For damage coverage, apply `Component.IDamageable.OnDamage` to a
host camera or breakable unit, then run this check on the client, reconnect, and run it again.

Run scripts with `sbox-cli --connect <pid> --request-timeout 180 --call run_csharp
'{"path":"<absolute script path>"}'`. `InspectNetworkMap.csx` is read-only diagnostic output;
successful rendering alone does not establish that the live relay exists or that joining completed.
