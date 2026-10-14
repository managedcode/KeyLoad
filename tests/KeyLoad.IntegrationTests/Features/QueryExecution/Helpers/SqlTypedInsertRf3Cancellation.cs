using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlTypedInsertRf3Cancellation
{
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
    internal static async Task<SqlTypedInsertRf3State> RunAsync(ClusterFixture fixture, PostgresRf3Selection selection,
        SqlTypedInsertRf3State state, KeyLoadClient root, CancellationToken token)
    {
        var controls = selection.Controls ?? throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult);
        var discovery = await Task.WhenAll(Nodes.Select(node => fixture.ReadSnapshotDiscoveryAsync(node, token)));
        var failures = new List<Exception>();
        SqlTypedInsertRf3State? next = null;
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node2,
                state.Identity.Principal.Id, state.Identity.Secret, PostgresRf3Certificate.RootPath(fixture), token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                next = await OriginalAsync(selection, state, root, wire, controls, discovery, token);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return next ?? throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult);
    }
    private static async Task<SqlTypedInsertRf3State> OriginalAsync(PostgresRf3Selection selection, SqlTypedInsertRf3State state,
        KeyLoadClient root, PostgresRf3Caller wire, RequestCqrsProbeFixture controls, ReplicaSiloDiscovery[] discovery, CancellationToken token)
    {
        var failures = new List<Exception>();
        SqlTypedInsertRf3State? next = null;
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var id = Guid.NewGuid();
                var before = await SqlTypedInsertRf3Images.ReadAsync(root, token);
                await SqlCreateTableRf3Wire.ArmAsync(wire, id, token);
                var arm = controls.WriteArm(state.Identity.Principal.Id, id, null,
                    RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold);
                var original = wire.ExecuteAsync<CommitReceipt>(SqlTypedInsertRf3Literal.CancelSql, [], prepared: false, originalCancellation.Token);
                var operationFailures = new List<Exception>();
                var joined = false;
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await HeldAsync(controls, arm, id, discovery, original, originalCancellation, token);
                    joined = true;
                    await controls.RetireArmAsync(arm, token);
                    await SqlTypedInsertRf3Images.RequireAsync(root, before, token);
                    await SqlCreateTableRf3Failures.WireAsync(wire, SqlTypedInsertRf3Literal.CancelSql, SqlCreateTableRf3Literal.ValidationState, token);
                    await SqlCreateTableRf3Wire.ArmAsync(wire, id, token);
                    var receipt = await wire.ExecuteAsync<CommitReceipt>(SqlTypedInsertRf3Literal.CancelSql, [], prepared: true, token);
                    var request = SqlTypedInsertRf3Literal.Request(id, SqlTypedInsertRf3Literal.CancelSql);
                    await SqlTypedInsertRf3Hot.ReceiptAsync(request, receipt, state.Owner,
                        new MutationReceipt(SqlTypedInsertRf3Literal.PutKind, SqlTypedInsertRf3Literal.Table, SqlTypedInsertRf3Literal.Cancelled, SqlTypedInsertRf3Literal.InitialRevision));
                    next = state with
                    {
                        Cancelled = request,
                        CancelledReceipt = receipt
                    };
                }, operationFailures);
                if (operationFailures.Count != PostgresRf3Protocol.FirstColumn && !controls.ArmFor(arm).Retired)
                {
                    await PostgresRf3Cancellation.CleanupAsync(controls, arm, discovery, originalCancellation, original, joined, operationFailures);
                }
                if (operationFailures.Count != PostgresRf3Protocol.FirstColumn)
                {
                    selection.RetainEvidence();
                }
                global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(operationFailures);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return next ?? throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult);
    }
    private static async Task HeldAsync(RequestCqrsProbeFixture controls, Guid arm, Guid id, ReplicaSiloDiscovery[] discovery,
        Task<CommitReceipt> original, CancellationTokenSource cancellation, CancellationToken token)
    {
        var held = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeOutcome.Observed, discovery, token);
        await Assert.That(held.CommandId).IsEqualTo(id);
        await Assert.That(held.ArmId).IsEqualTo(arm);
        await Assert.That(held.SessionId).IsEqualTo(controls.SessionId);
        await Assert.That(held.RequestId).IsNotEqualTo(Guid.Empty);
        await Assert.That(original.IsCompleted).IsFalse();
        await cancellation.CancelAsync();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => original)
            ?? throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult);
        await Assert.That(error.CancellationToken).IsEqualTo(cancellation.Token);
        var cancelled = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeOutcome.Cancelled, discovery, token);
        var disposed = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ProducerDisposed, RequestCqrsProbeOutcome.Observed, discovery, token);
        await SqlRf3Protocol.EqualAsync(held with
        {
            Outcome = cancelled.Outcome
        }, cancelled);
        await SqlRf3Protocol.EqualAsync(held with
        {
            Phase = RequestCqrsProbePhase.ProducerDisposed
        }, disposed);
    }
}
