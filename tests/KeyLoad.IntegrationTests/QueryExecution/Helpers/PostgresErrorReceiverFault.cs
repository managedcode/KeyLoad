using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresErrorReceiverFault
{
    internal static async Task RunAsync(ClusterFixture fixture, PostgresRf3State state,
        PostgresErrorReceiverControl control, global::KeyLoad.Client.KeyLoadClient root, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var wire = await PostgresRf3WireOwner.OpenAsync(fixture, token);
            Task? original = null;
            var originalJoined = false;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await PostgresRf3WireAuthentication.AuthenticateAsync(wire, state.Reader, token);
                await PostgresPortalOwnershipCommands.ParseAsync(wire.Stream, PostgresErrorReceiverProtocol.Statement, state.Sql, token);
                await PostgresPortalOwnershipCommands.BindAsync(wire.Stream, PostgresErrorReceiverProtocol.Portal,
                    PostgresErrorReceiverProtocol.Statement, token);
                await PostgresErrorReceiverPolicy.SetAsync(root, state, denied: true, token);
                var before = await PostgresPortalOwnershipAssertions.ImageAsync(root, state, token);
                var discovery = await fixture.ReadSnapshotDiscoveryAsync(McpCallerProtocol.Node3, token);
                await global::TUnit.Assertions.Assert.That(discovery.TransportReady).IsTrue();
                await global::TUnit.Assertions.Assert.That(discovery.VoterId).IsEqualTo(
                    RequestCqrsProbeFileNames.OriginForNode(McpCallerProtocol.Node3));
                var arm = control.Arm(McpCallerProtocol.Node3, state.Reader.Principal.Id);
                original = ReceiveAsync(wire, token);
                var buffered = await control.WaitAsync(McpCallerProtocol.Node3, arm, PostgresErrorDeliveryPhase.Buffered, token);
                await global::TUnit.Assertions.Assert.That(original.IsCompleted).IsFalse();
                wire.AbortReceiver();
                try
                {
                    await original;
                }
                finally { originalJoined = true; }
                await PostgresErrorReceiverAssertions.TerminalAsync(control, arm, buffered, token);
                await global::TUnit.Assertions.Assert.That(token.IsCancellationRequested).IsFalse();
                await global::TUnit.Assertions.Assert.That(await PostgresPortalOwnershipAssertions.ImageAsync(root, state, token)).IsEqualTo(before);
            }, failures);
            ServerFailureObserver.Observe(wire.AbortReceiver, failures);
            if (original is not null && !originalJoined)
            {
                await ServerFailureObserver.ObserveAsync(() => original, failures);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ReceiveAsync(PostgresRf3WireOwner wire, CancellationToken token)
    {
        await PostgresPortalOwnershipReplies.SendAsync(wire.Stream, PostgresPortalOwnershipProtocol.Execute,
            PostgresPortalOwnershipFrames.Execute(PostgresErrorReceiverProtocol.Portal), token);
        try
        {
            await PostgresRf3WireMessage.ReadAsync(wire.Stream, token);
            throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
        }
        catch (IOException) when (wire.ReceiverAborted && !token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (wire.ReceiverAborted && !token.IsCancellationRequested) { }
    }
}
