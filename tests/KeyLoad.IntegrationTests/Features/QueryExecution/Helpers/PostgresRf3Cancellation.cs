using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3Cancellation
{
    internal static async Task RunAsync(ClusterFixture fixture, PostgresRf3Selection selection,
        PostgresRf3State state, global::KeyLoad.Client.KeyLoadClient root, CancellationToken token)
    {
        var controls = selection.Controls ?? throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult);
        var discovery = await Task.WhenAll(Nodes.Select(node => fixture.ReadSnapshotDiscoveryAsync(node, token)));
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node2,
                state.Reader.Principal.Id, state.Reader.Secret, PostgresRf3Certificate.RootPath(fixture), token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                var before = await PostgresRf3Assertions.ImageAsync(root, token);
                var arm = controls.WriteArm(state.Reader.Principal.Id, Guid.Empty, GrainReadKind.Query,
                    RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold);
                var original = wire.ExecuteAsync<QueryPage>(state.Sql, [], prepared: false, originalCancellation.Token);
                var operationFailures = new List<Exception>();
                var originalJoined = false;
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    var held = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.AuthorizationReload,
                        RequestCqrsProbeOutcome.Observed, discovery, token);
                    await global::TUnit.Assertions.Assert.That(held.CommandId).IsEqualTo(Guid.Empty);
                    await global::TUnit.Assertions.Assert.That(held.ArmId).IsEqualTo(arm);
                    await global::TUnit.Assertions.Assert.That(held.SessionId).IsEqualTo(controls.SessionId);
                    await global::TUnit.Assertions.Assert.That(held.RequestId).IsNotEqualTo(Guid.Empty);
                    await global::TUnit.Assertions.Assert.That(original.IsCompleted).IsFalse();
                    await originalCancellation.CancelAsync();
                    var cancellation = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(() => original)
                        ?? throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult);
                    originalJoined = true;
                    await global::TUnit.Assertions.Assert.That(cancellation.CancellationToken).IsEqualTo(originalCancellation.Token);
                    var cancelled = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.AuthorizationReload,
                        RequestCqrsProbeOutcome.Cancelled, discovery, token);
                    var disposed = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ProducerDisposed,
                        RequestCqrsProbeOutcome.Observed, discovery, token);
                    await SqlRf3Protocol.EqualAsync(held with
                    { Outcome = cancelled.Outcome }, cancelled);
                    await SqlRf3Protocol.EqualAsync(held with
                    { Phase = RequestCqrsProbePhase.ProducerDisposed }, disposed);
                    await controls.RetireArmAsync(arm, token);
                    await global::TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root, token)).IsEqualTo(before);
                    await PostgresRf3Assertions.PageAsync(state,
                        await wire.ExecuteAsync<QueryPage>(state.Sql, [], prepared: true, token));
                }, operationFailures);
                if (operationFailures.Count != PostgresRf3Protocol.FirstColumn && !controls.ArmFor(arm).Retired)
                {
                    await CleanupAsync(controls, arm, discovery, originalCancellation, original,
                        originalJoined, operationFailures);
                }
                if (operationFailures.Count != PostgresRf3Protocol.FirstColumn)
                { selection.RetainEvidence(); }
                global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(operationFailures);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CleanupAsync(RequestCqrsProbeFixture controls, Guid arm,
        ReplicaSiloDiscovery[] discovery, CancellationTokenSource originalCancellation,
        Task original, bool originalJoined, List<Exception> operationFailures)
    {
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cleanup = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(originalCancellation.CancelAsync, operationFailures);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => controls.ReleaseOpenArmsAsync(discovery, cleanup.Token), operationFailures);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                try
                { if (!originalJoined) { await original; } }
                catch (OperationCanceledException error) when (error.CancellationToken == originalCancellation.Token) { }
            }, operationFailures);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                _ = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ProducerDisposed,
                    RequestCqrsProbeOutcome.Observed, discovery, cleanup.Token);
                if (!controls.ArmFor(arm).Settled)
                { controls.JoinDisposedGate(arm, original); }
                await controls.RetireArmAsync(arm, cleanup.Token);
            }, operationFailures);
        }, operationFailures);
    }

    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
}
