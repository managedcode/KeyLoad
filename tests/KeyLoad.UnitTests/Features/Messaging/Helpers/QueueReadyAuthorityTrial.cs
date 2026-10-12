namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueReadyAuthorityTrial
{
    internal static async Task RunAsync(QueueReadyAuthorityFault fault, bool expired, CancellationToken token)
    {
        var owningFailures = new List<Exception>();
        try
        {
            token.ThrowIfCancellationRequested();
            using var fixture = new TestDatabase(nativeReplicaAdmission: true);
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                fixture.Configure("jobs", ResourceKind.WorkQueue);
                fixture.Configure("orders", ResourceKind.Collection);
                fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("worker", "tenant",
                    [new("database", "jobs", Capability.QueueConsume | Capability.QueueAck),
                        new("database", "orders", Capability.DocumentsWrite)], []))).Get<PrincipalRecord>();
                var now = QueueWholeFlowStorage.Clock(fixture.Store).AddSeconds(1);
                var producer = Guid.NewGuid();
                fixture.Submit(OperationKind.Batch, new CommandRequest(producer, fixture.Partition,
                    [new EnqueueMessage("jobs", "input", "{\"input\":1}", ExpiresAt: expired ? now.AddSeconds(1) : null)]),
                    id: producer, time: now).Get<CommitReceipt>();
                var lane = new QueueLaneRef(fixture.Partition, "jobs");
                var row = QueueReadyAuthorityFaultRow.Damage(fixture, lane, fault);
                var image = QueueWholeFlowStorage.Bytes(fixture.Store);
                var position = fixture.Store.Position;
                var identity = NativeSerialization.Serialize(fixture.Store.Identity);
                var id = Guid.NewGuid();
                var request = new ReceiveRequest(id, lane);
                var failures = new List<Exception>();
                var expectedError = QueueReadyAuthorityRefusalAssertions.ExpectedError(fault);
                var first = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Submit(OperationKind.Receive,
                    request, principal: "worker", id: id, time: now.AddSeconds(2)));
                failures.Add(first);
                await Assert.That(first.Code).IsEqualTo(ErrorCode.RecoveryRequired);
                var retained = fixture.ReadRetainedEntry(id);
                var original = retained.Operation ?? throw new InvalidOperationException("The failed native entry is absent.");
                await QueueReadyAuthorityRefusalAssertions.RequireAsync(fixture, original, image, position, identity);
                var cut = fixture.ReadJournalCut();
                var repeat = Assert.ThrowsExactly<KeyLoadException>(() => fixture.SubmitIssued(original));
                failures.Add(repeat);
                await Assert.That(repeat.Code).IsEqualTo(ErrorCode.RecoveryRequired);
                await Assert.That(fixture.ReadJournalCut()).IsEqualTo(cut);
                await QueueReadyAuthorityRefusalAssertions.RequireAsync(fixture, original, image, position, identity);
                var recovered = fixture.RecoverFaultOwnedRow(original,
                    () => QueueReadyAuthorityFaultRow.Restore(fixture, row.Key, row.Original), failures, token, expectedError);
                await Assert.That(recovered.Index).IsEqualTo(retained.Index);
                await Assert.That(recovered.Operation!.Id).IsEqualTo(id);
                await Assert.That(fixture.ReadJournalCut()).IsEqualTo(cut);
                await Assert.That(recovered.Operation!.NativePayload.ToArray()).IsEquivalentTo(original.NativePayload.ToArray(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
                await QueueReadyAuthorityRefusalAssertions.RequireOriginalFailuresAsync(failures, expectedError);
                var receive = fixture.SubmitIssued(original).Get<ReceiveResult>();
                await QueueReadyAuthorityRefusalAssertions.RequireRepairedReplayAsync(fixture, original, receive);
                await QueueReadyAuthorityHealthy.CompleteOriginalAsync(fixture, lane, receive, expired, now.AddSeconds(2));
                await QueueReadyAuthorityHealthy.RunAsync(fixture, lane, now.AddSeconds(2), token);
            }, owningFailures);
        }
        catch (Exception error) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { owningFailures.Add(error); }
        catch (Exception error) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { owningFailures.Add(error); }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(owningFailures);
    }
}
