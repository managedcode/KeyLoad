using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDueDatabaseScopeRf3Flow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var subject = QueueDueDatabaseScopeRf3Protocol.SubjectPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new QueueDeadlineRf3Selection(subject));
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
            await QueueDueDatabaseScopeRf3Actions.PublishAsync(admin, state, state.Denied, false, token);
            await QueueDueDatabaseScopeRf3Actions.PublishAsync(admin, state, state.Allowed, false, token);
            await Assert.That(TimeProvider.System.GetUtcNow() < state.Due).IsTrue();
            await DueRecurringRf3Assertions.WaitForDueTimeAsync(state.Due, token);
            await ReadyAsync(admin, state, state.Allowed, token);
            await QueueDueDatabaseScopeRf3Actions.RefuseAsync(admin, worker, state, false, token);
        }, token);
        await QueueLeaseRf3Cold.RestartAsync(fixture, token);
        await WithAsync(fixture, state, async (admin, worker) =>
        {
            await QueueDueDatabaseScopeRf3Current.ObserveAsync(admin, state, token);
            await QueueDueDatabaseScopeRf3Actions.RefuseAsync(admin, worker, state, false, token);
            await QueueDueDatabaseScopeRf3Delivery.SettleAsync(admin, worker, state, state.Allowed, false, token);
            await QueueDueDatabaseScopeRf3Actions.ReplayAsync(admin, worker, state, token);
        }, token);
        await RepairAsync(fixture, state, token);
        await QueueLeaseRf3Cold.RestartAsync(fixture, token);
        await WithAsync(fixture, state, async (admin, worker) =>
        {
            await QueueDueDatabaseScopeRf3Current.ObserveAsync(admin, state, token);
            await QueueDueDatabaseScopeRf3Actions.RefuseAsync(admin, worker, state, true, token);
            await QueueDueDatabaseScopeRf3Actions.ReplayAsync(admin, worker, state, token);
            await QueueDueDatabaseScopeRf3Actions.PublishAsync(admin, state, state.Allowed, true, token);
            await QueueDueDatabaseScopeRf3Delivery.SettleAsync(admin, worker, state, state.Allowed, true, token);
            await QueueDueDatabaseScopeRf3Actions.ReplayAsync(admin, worker, state, token);
        }, token);
    }

    private static async Task RepairAsync(ClusterFixture fixture, QueueDueDatabaseScopeRf3State state,
        CancellationToken token)
    {
        var repaired = MessagingRf3Identity.WithCapability(state.Identity.Principal, state.Denied.Lane,
            QueueDeadlineRf3Protocol.Worker);
        await Assert.That(repaired.PolicyEpoch).IsEqualTo(2L);
        await MessagingRf3Identity.UpdateAsync(fixture, repaired, token);
        state.Identity = state.Identity with
        {
            Principal = repaired
        };
        await WithAsync(fixture, state, async (admin, worker) =>
        {
            await ReadyAsync(admin, state, state.Denied, token);
            await QueueDueDatabaseScopeRf3Actions.RefuseAsync(admin, worker, state, true, token);
            await QueueDueDatabaseScopeRf3Delivery.SettleAsync(admin, worker, state, state.Denied, false, token);
            await QueueDueDatabaseScopeRf3Actions.ReplayAsync(admin, worker, state, token);
        }, token);
    }

    private static async Task ReadyAsync(RequestCqrsRf3Callers admin, QueueDueDatabaseScopeRf3State state,
        QueueDueDatabaseScopeRf3Lane lane, CancellationToken token)
    {
        var ready = QueueDueDatabaseScopeRf3Expected.Ready(lane, false);
        await QueueDeadlineRf3Assertions.WaitReadyAsync(admin, lane.Lane, ready, token);
        lane.Message = ready;
        await QueueDueDatabaseScopeRf3Current.ObserveAsync(admin, state, token);
    }

    private static async Task<QueueDueDatabaseScopeRf3State> PrepareAsync(ClusterFixture fixture,
        string subject, CancellationToken token)
    {
        var tenant = QueueDueDatabaseScopeRf3Protocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        return await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var allowed = await LaneAsync(admin, tenant, QueueDueDatabaseScopeRf3Protocol.AllowedDatabase,
                QueueDueDatabaseScopeRf3Protocol.AllowedPayload, token);
            var denied = await LaneAsync(admin, tenant, QueueDueDatabaseScopeRf3Protocol.DeniedDatabase,
                QueueDueDatabaseScopeRf3Protocol.DeniedPayload, token);
            var identity = await MessagingRf3Identity.CreateSelectedAsync(fixture, subject, tenant,
                [new(allowed.Lane.Partition.DatabaseId, allowed.Lane.Queue, QueueDeadlineRf3Protocol.Worker),
                 new(denied.Lane.Partition.DatabaseId, denied.Lane.Queue, Capability.QueueInspect | Capability.QueueAck)],
                [], false, token);
            var due = TimeProvider.System.GetUtcNow().AddSeconds(QueueDeadlineRf3Protocol.LeadSeconds);
            var state = new QueueDueDatabaseScopeRf3State(identity, due, allowed, denied);
            await QueueDueDatabaseScopeRf3Current.ObserveAsync(admin, state, token);
            return state;
        }, token);
    }

    private static async Task<QueueDueDatabaseScopeRf3Lane> LaneAsync(RequestCqrsRf3Callers admin,
        string tenant, string database, string payload, CancellationToken token)
    {
        var partition = new PartitionRef(tenant, database, QueueDueDatabaseScopeRf3Protocol.Domain,
            Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var queue = new QueueLaneRef(partition, QueueDueDatabaseScopeRf3Protocol.Queue);
        var definition = new ResourceDefinition(queue.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId);
        await QueueDueDatabaseScopeRf3Expected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(tenant, database, definition), token)), definition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadAtomicPartitionPlacementAsync(new(1, partition), token));
        await QueueDueDatabaseScopeRf3Expected.EqualAsync((await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await admin.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead,
                new AtomicPartitionPlacementReadRequest(1, partition), token))).Value, placement);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(partition, token));
        return new(queue, payload, placement, outbox);
    }

    private static Task<bool> WithAsync(ClusterFixture fixture, QueueDueDatabaseScopeRf3State state,
        Func<RequestCqrsRf3Callers, RequestCqrsRf3Callers, Task> operation, CancellationToken token)
        => QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey,
            admin => QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node2, state.Identity.Secret,
                async worker => { await operation(admin, worker); return true; }, token), token);
}
