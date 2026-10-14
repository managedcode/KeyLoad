using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class SqlTypedInsertMovementCancellation
{
    internal static Task RunAsync(TwoRf3MembershipWave wave, SqlTypedInsertMovementTls tls,
        PartitionMovementPublicParentRf3Seed seed, SqlTypedInsertMovementState state, CancellationToken token)
        => SqlTypedInsertMovementClients.RunAsync(wave, tls, state, TwoRf3MembershipProtocol.Node4, async (caller, wire) =>
    {
        var controls = wave.QueryControls;
        var discovery = await SqlTypedInsertMovementDiscovery.ReadAsync(wave, seed, state, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => OwnedAsync(wave, seed, state, wire, controls, discovery, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }, token);
    private static async Task OwnedAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        SqlTypedInsertMovementState state, PostgresRf3Caller wire, RequestCqrsProbeFixture controls,
        ReplicaSiloDiscovery[] discovery, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            await ServerFailureObserver.ObserveAsync(async () =>
        {
            var request = SqlTypedInsertMovementProtocol.Request(seed.Partition, SqlTypedInsertMovementProtocol.HealthyId,
                    SqlTypedInsertMovementProtocol.AfterSql, SqlTypedInsertMovementProtocol.PublishedEpoch);
            await SqlTypedInsertMovementClients.ArmAsync(wire, request, token);
            var arm = controls.WriteArm(state.Identity.Principal.Id, request.CommandId!.Value, null,
                    RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold);
            var original = wire.ExecuteAsync<CommitReceipt>(request.Sql, [], prepared: false, cancellation.Token);
            var operations = new List<Exception>();
            var joined = false;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await HeldAsync(controls, arm, request.CommandId.Value, discovery, original, cancellation, token);
                joined = true;
                await controls.RetireArmAsync(arm, token);
                await SqlCreateTableRf3Failures.WireAsync(wire, request.Sql, SqlTypedInsertMovementProtocol.ValidationState, token);
            }, operations);
            if (operations.Count != NoFailures && !controls.ArmFor(arm).Retired)
            {
                await PostgresRf3Cancellation.CleanupAsync(controls, arm, discovery, cancellation, original, joined, operations);
            }
            if (operations.Count != NoFailures)
            {
                wave.RetainRoots();
            }
            ServerFailureObserver.ThrowIfAny(operations);
        }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
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
            ?? throw new InvalidOperationException(SqlTypedInsertMovementProtocol.Missing);
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
    private const int NoFailures = 0;
}

