using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDueDatabaseScopeClusterFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var subject = QueueDueDatabaseScopeClusterProtocol.SubjectPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new QueueDeadlineClusterSelection(subject));
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
                await ExecuteAsync(fixture, subject, lifetime.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(ClusterFixture fixture, string subject, CancellationToken token)
    {
        var state = await PrepareAsync(fixture, subject, token);
        await WithAsync(fixture, state, async (admin, worker) =>
        {
            await QueueDueDatabaseScopeClusterActions.PublishAsync(admin, state, state.Denied, false, token);
            await QueueDueDatabaseScopeClusterActions.PublishAsync(admin, state, state.Allowed, false, token);
            await Assert.That(TimeProvider.System.GetUtcNow() < state.Due).IsTrue();
            await DueRecurringClusterAssertions.WaitForDueTimeAsync(state.Due, token);
            await ReadyAsync(admin, state, state.Allowed, token);
            await QueueDueDatabaseScopeClusterActions.RefuseAsync(admin, worker, state, false, token);
        }, token);
        await QueueLeaseClusterCold.RestartAsync(fixture, token);
        await WithAsync(fixture, state, async (admin, worker) =>
        {
            await QueueDueDatabaseScopeClusterCurrent.ObserveAsync(admin, state, token);
            await QueueDueDatabaseScopeClusterActions.RefuseAsync(admin, worker, state, false, token);
            await QueueDueDatabaseScopeClusterDelivery.SettleAsync(admin, worker, state, state.Allowed, false, token);
            await QueueDueDatabaseScopeClusterActions.ReplayAsync(admin, worker, state, token);
        }, token);
        await RepairAsync(fixture, state, token);
        await QueueLeaseClusterCold.RestartAsync(fixture, token);
        await WithAsync(fixture, state, async (admin, worker) =>
        {
            await QueueDueDatabaseScopeClusterCurrent.ObserveAsync(admin, state, token);
            await QueueDueDatabaseScopeClusterActions.RefuseAsync(admin, worker, state, true, token);
            await QueueDueDatabaseScopeClusterActions.ReplayAsync(admin, worker, state, token);
            await QueueDueDatabaseScopeClusterActions.PublishAsync(admin, state, state.Allowed, true, token);
            await QueueDueDatabaseScopeClusterDelivery.SettleAsync(admin, worker, state, state.Allowed, true, token);
            await QueueDueDatabaseScopeClusterActions.ReplayAsync(admin, worker, state, token);
        }, token);
    }

    private static async Task RepairAsync(ClusterFixture fixture, QueueDueDatabaseScopeClusterState state,
        CancellationToken token)
    {
        var repaired = MessagingClusterIdentity.WithCapability(state.Identity.Principal, state.Denied.Lane,
            QueueDeadlineClusterProtocol.Worker);
        await Assert.That(repaired.PolicyEpoch).IsEqualTo(2L);
        await MessagingClusterIdentity.UpdateAsync(fixture, repaired, token);
        state.Identity = state.Identity with
        {
            Principal = repaired
        };
        await WithAsync(fixture, state, async (admin, worker) =>
        {
            await ReadyAsync(admin, state, state.Denied, token);
            await QueueDueDatabaseScopeClusterActions.RefuseAsync(admin, worker, state, true, token);
            await QueueDueDatabaseScopeClusterDelivery.SettleAsync(admin, worker, state, state.Denied, false, token);
            await QueueDueDatabaseScopeClusterActions.ReplayAsync(admin, worker, state, token);
        }, token);
    }

    private static async Task ReadyAsync(RequestCqrsClusterCallers admin, QueueDueDatabaseScopeClusterState state,
        QueueDueDatabaseScopeClusterLane lane, CancellationToken token)
    {
        var ready = QueueDueDatabaseScopeClusterExpected.Ready(lane, false);
        await QueueDeadlineClusterAssertions.WaitReadyAsync(admin, lane.Lane, ready, token);
        lane.Message = ready;
        await QueueDueDatabaseScopeClusterCurrent.ObserveAsync(admin, state, token);
    }

    private static async Task<QueueDueDatabaseScopeClusterState> PrepareAsync(ClusterFixture fixture,
        string subject, CancellationToken token)
    {
        var tenant = QueueDueDatabaseScopeClusterProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        return await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var allowed = await LaneAsync(admin, tenant, QueueDueDatabaseScopeClusterProtocol.AllowedDatabase,
                QueueDueDatabaseScopeClusterProtocol.AllowedPayload, token);
            var denied = await LaneAsync(admin, tenant, QueueDueDatabaseScopeClusterProtocol.DeniedDatabase,
                QueueDueDatabaseScopeClusterProtocol.DeniedPayload, token);
            var identity = await MessagingClusterIdentity.CreateSelectedAsync(fixture, subject, tenant,
                [new(allowed.Lane.Partition.DatabaseId, allowed.Lane.Queue, QueueDeadlineClusterProtocol.Worker),
                 new(denied.Lane.Partition.DatabaseId, denied.Lane.Queue, Capability.QueueInspect | Capability.QueueAck)],
                [], false, token);
            var due = TimeProvider.System.GetUtcNow().AddSeconds(QueueDeadlineClusterProtocol.LeadSeconds);
            var state = new QueueDueDatabaseScopeClusterState(identity, due, allowed, denied);
            await QueueDueDatabaseScopeClusterCurrent.ObserveAsync(admin, state, token);
            return state;
        }, token);
    }

    private static async Task<QueueDueDatabaseScopeClusterLane> LaneAsync(RequestCqrsClusterCallers admin,
        string tenant, string database, string payload, CancellationToken token)
    {
        var partition = new PartitionRef(tenant, database, QueueDueDatabaseScopeClusterProtocol.Domain,
            Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var queue = new QueueLaneRef(partition, QueueDueDatabaseScopeClusterProtocol.Queue);
        var definition = new ResourceDefinition(queue.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId);
        await QueueDueDatabaseScopeClusterExpected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(tenant, database, definition), token)), definition);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(partition, token));
        return new(queue, payload, outbox);
    }

    private static Task<bool> WithAsync(ClusterFixture fixture, QueueDueDatabaseScopeClusterState state,
        Func<RequestCqrsClusterCallers, RequestCqrsClusterCallers, Task> operation, CancellationToken token)
        => QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey,
            admin => QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node2, state.Identity.Secret,
                async worker => { await operation(admin, worker); return true; }, token), token);
}
