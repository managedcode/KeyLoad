using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPureReadRf3Cancellation
{
    internal static async Task RunAsync(ClusterFixture fixture, PostgresRf3Selection selection,
        PostgresPureReadRf3State state, global::KeyLoad.Client.KeyLoadClient root, CancellationToken token)
    {
        var controls = selection.Controls ?? throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult);
        var discovery = await Task.WhenAll(Nodes.Select(node => fixture.ReadSnapshotDiscoveryAsync(node, token)));
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node2,
                state.Identity.Principal.Id, state.Identity.Secret, PostgresRf3Certificate.RootPath(fixture), token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                var before = await PostgresCompositionRf3Models.ReadAsync(root, token);
                var arm = controls.WriteArm(state.Identity.Principal.Id, Guid.Empty, GrainReadKind.Document,
                    RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold);
                var original = wire.ExecuteAsync<DocumentReadResult>(PostgresPureReadRf3Protocol.DocumentSql,
                    PostgresPureReadRf3Protocol.Parameters(new GetDocumentRequest(PostgresRf3State.Left)),
                    prepared: false, originalCancellation.Token);
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
                    await SqlRf3Protocol.EqualAsync(before, await PostgresCompositionRf3Models.ReadAsync(root, token));
                    await SqlRf3Protocol.EqualAsync(new DocumentReadResult(state.Image.Left),
                        await wire.ExecuteAsync<DocumentReadResult>(PostgresPureReadRf3Protocol.DocumentSql,
                            PostgresPureReadRf3Protocol.Parameters(new GetDocumentRequest(PostgresRf3State.Left)),
                            prepared: true, token));
                }, operationFailures);
                if (operationFailures.Count != PostgresRf3Protocol.FirstColumn && !controls.ArmFor(arm).Retired)
                {
                    await PostgresRf3Cancellation.CleanupAsync(controls, arm, discovery, originalCancellation, original,
                        originalJoined, operationFailures);
                }
                if (operationFailures.Count != PostgresRf3Protocol.FirstColumn)
                { selection.RetainEvidence(); }
                global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(operationFailures);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
}
