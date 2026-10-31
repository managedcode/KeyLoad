using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueScheduledAuthorityTrial
{
    internal static async Task RunAsync()
    {
        using var fixture = new TestDatabase();
        QueueLifecycleProvisioning.Seed(fixture, byteSublimit: false);
        var flow = new QueueLifecycleTestState(fixture.Partition, QueueWholeFlowStorage.Clock(fixture.Store));
        var due = flow.Time.AddSeconds(1);
        flow.Batch(fixture.Database, new EnqueueMessage(flow.Lane.Queue, QueueLifecycleTestProtocol.Pending,
            QueueLifecycleTestProtocol.Payload, QueueLifecycleTestProtocol.Headers, NotBefore: due)).Get<CommitReceipt>();
        var key = KeySpace.Partition("message-meta", flow.Partition, flow.Lane.Queue, QueueLifecycleTestProtocol.Pending);
        var exact = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        flow = flow with
        {
            Time = due.AddSeconds(1)
        };
        var request = new ReceiveRequest(Guid.NewGuid(), flow.Lane, LeaseSeconds: QueueLifecycleTestProtocol.LeaseSeconds);
        var receive = fixture.Database.CreateCommand(OperationKind.Receive, request.RequestId,
            QueueLifecycleTestProtocol.Administrator, flow.Time, request);
        var state = new QueueScheduledAuthorityState(flow, due, key, exact, receive, fixture.ExpectedReference(
            new(flow.Partition, QueueLifecycleTestProtocol.Collection, QueueLifecycleTestProtocol.Pending)).Lifetime!);
        await QueueScheduledAuthorityImage.ScheduledAsync(fixture.Database, fixture.Store, state);
        var original = QueuePendingAuthorityHistory.Capture(fixture.Store);
        var damaged = await DamageAndRefuseAsync(fixture, state, original, exact, due, key);
        fixture.Store.Dispose();
        QueuePendingAuthorityCut terminal;
        using (var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            var owner = fixture.CreateConfiguredDatabase(cold);
            await QueuePendingAuthorityHistory.SameAsync(cold, damaged);
            await QueueScheduledAuthorityHistory.RefuseAsync(owner, cold, state, damaged);
            Put(cold, key, exact);
            await QueuePendingAuthorityHistory.SameAsync(cold, original with
            {
                Position = original.Position + 2
            });
            await QueueScheduledAuthorityImage.ScheduledAsync(owner, cold, state);
            var claimed = owner.Apply(receive);
            flow.Successes.Add((receive, claimed));
            var delivery = await Assert.That(claimed.Get<ReceiveResult>().Deliveries).HasSingleItem();
            await QueueScheduledAuthorityImage.LeasedAsync(owner, cold, state, delivery);
            await QueueScheduledAuthorityOperations.ProcessAsync(owner, cold, state, delivery, 4);
            await QueueScheduledAuthorityImage.TerminalAsync(owner, cold, state, healthy: false);
            await QueueScheduledAuthorityHistory.ReplayAsync(owner, cold, state);
            flow.Batch(owner, QueueLifecycleHealthy.Enqueue(QueuePendingAuthorityProtocol.Healthy)).Get<CommitReceipt>();
            var healthy = await QueueLifecycleOperations.ClaimAsync(owner, flow, QueuePendingAuthorityProtocol.Healthy);
            await Assert.That(healthy.Id).IsEqualTo(QueuePendingAuthorityProtocol.Healthy);
            await QueueScheduledAuthorityOperations.ProcessAsync(owner, cold, state, healthy, 3);
            await QueueScheduledAuthorityImage.TerminalAsync(owner, cold, state, healthy: true);
            await QueueScheduledAuthorityHistory.ReplayAsync(owner, cold, state);
            terminal = QueuePendingAuthorityHistory.Capture(cold);
        }
        using var second = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        var reopened = fixture.CreateConfiguredDatabase(second);
        await QueuePendingAuthorityHistory.SameAsync(second, terminal);
        await QueueScheduledAuthorityImage.TerminalAsync(reopened, second, state, healthy: true);
        await QueueScheduledAuthorityHistory.ReplayAsync(reopened, second, state);
        await QueuePendingAuthorityHistory.SameAsync(second, terminal);
    }

    private static void Put(ZoneTreeStore store, byte[] key, byte[] value)
        => store.Commit((tx, _) => { tx.Put(key, value); return true; });

    private static async Task<QueuePendingAuthorityCut> DamageAndRefuseAsync(TestDatabase fixture,
        QueueScheduledAuthorityState state, QueuePendingAuthorityCut original, byte[] exact,
        DateTimeOffset due, byte[] key)
    {
        var malformed = OrleansSerialization.Serialize(OrleansSerialization.Deserialize<MessageMetadata>(exact)
            with
        {
            NotBefore = due.AddTicks(1)
        });
        Put(fixture.Store, key, malformed);
        var damaged = QueuePendingAuthorityHistory.Capture(fixture.Store);
        await QueueScheduledAuthorityHistory.OneRowAsync(original, damaged, key, malformed);
        await QueueScheduledAuthorityHistory.RefuseAsync(fixture.Database, fixture.Store, state, damaged);
        await QueueScheduledAuthorityHistory.RefuseAsync(fixture.Database, fixture.Store, state, damaged);
        return damaged;
    }
}
