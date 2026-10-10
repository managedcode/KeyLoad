using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class AggregateReplayAuthorizationContinuation
{
    private const long RepairedPolicyEpoch = 3;
    private const int RetainedTailMaximum = 1;
    private const int NoTailEvents = 0;
    private const long SnapshotVersionStep = 1;
    private const string SnapshotMutation = "storeAggregateSnapshot";

    internal static async Task RepairAndColdAsync(AggregateReplayFixture fixture, Guid oldCommandId,
        CommitReceipt oldReceipt, AggregateReplayPage oldPage, (byte[] Event, byte[] Outcome) originalRecords)
    {
        fixture.ConfigurePrincipal(AggregateReplayFixture.WorkerId,
            Capability.EventsReplay | Capability.EventsRead | Capability.EventsSnapshotsManage,
            [AggregateReplayFixture.PayloadReadGrant, AggregateReplayFixture.PayloadUseGrant,
                AggregateReplayFixture.HeaderReadGrant, AggregateReplayFixture.HeaderUseGrant],
            policyEpoch: RepairedPolicyEpoch);
        await OldDeniedAsync(fixture, oldCommandId, oldReceipt, oldPage, originalRecords);
        var fresh = await FreshSnapshotAsync(fixture, oldPage);
        var identity = fixture.Store.Identity;
        var applied = fixture.Store.Position;
        fixture.Reopen();
        await Assert.That(fixture.Store.Identity.NodeId).IsEqualTo(identity.NodeId);
        await Assert.That(fixture.Store.Identity.Incarnation).IsEqualTo(identity.Incarnation);
        await Assert.That(fixture.Store.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
        await Assert.That(fixture.Store.Position).IsGreaterThanOrEqualTo(applied);
        await OldDeniedAsync(fixture, oldCommandId, oldReceipt, fresh.Page, originalRecords);
        var replay = fixture.StoreSnapshot(fresh.Page.Snapshot!.SourceRevision,
            expectedVersion: oldPage.Snapshot!.SnapshotVersion, commandId: fresh.CommandId);
        await EqualAsync(fresh.Receipt, replay);
        await PageAsync(fixture.Read(maximumEvents: RetainedTailMaximum), fresh.Page);
    }

    private static async Task<(Guid CommandId, CommitReceipt Receipt, AggregateReplayPage Page)> FreshSnapshotAsync(
        AggregateReplayFixture fixture, AggregateReplayPage oldPage)
    {
        var previous = oldPage.Snapshot!;
        var id = Guid.NewGuid();
        var receipt = fixture.StoreSnapshot(previous.SourceRevision, expectedVersion: previous.SnapshotVersion,
            stateJson: previous.StateJson, commandId: id);
        await EqualAsync(new MutationReceipt(SnapshotMutation, AggregateReplayFixture.StreamSet,
            AggregateReplayFixture.StreamId, previous.SnapshotVersion + SnapshotVersionStep), receipt.Mutations.Single());
        var expected = previous with { SnapshotVersion = previous.SnapshotVersion + SnapshotVersionStep, Checksum = string.Empty };
        expected = expected with { Checksum = JsonData.Fingerprint(expected) };
        var outcome = fixture.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(
            fixture.Partition, AggregateReplayFixture.WorkerId, id)));
        await Assert.That(NativeSerialization.Deserialize<StoredOutcome>(outcome!).PolicyEpoch).IsEqualTo(RepairedPolicyEpoch);
        var page = fixture.Read(maximumEvents: RetainedTailMaximum);
        await PageAsync(page, oldPage with { Snapshot = expected });
        return (id, receipt, page);
    }

    private static async Task OldDeniedAsync(AggregateReplayFixture fixture, Guid oldCommandId,
        CommitReceipt oldReceipt, AggregateReplayPage expectedPage, (byte[] Event, byte[] Outcome) originalRecords)
    {
        var refusal = Assert.ThrowsExactly<KeyLoadException>(() => fixture.StoreSnapshot(
            expectedPage.Snapshot!.SourceRevision, commandId: oldCommandId));
        await Assert.That(refusal.Code).IsEqualTo(ErrorCode.PermissionDenied);
        var records = fixture.Store.Read(view => (
            Event: view.ReadOwnedValue(fixture.EventKey(expectedPage.Snapshot!.SourceRevision)),
            Outcome: view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(fixture.Partition,
                AggregateReplayFixture.WorkerId, oldCommandId))));
        await Assert.That(records.Event!.AsSpan().SequenceEqual(originalRecords.Event)).IsTrue();
        await Assert.That(records.Outcome!.AsSpan().SequenceEqual(originalRecords.Outcome)).IsTrue();
        await EqualAsync(oldReceipt, OutcomeStoreOracle.ReadPartition(fixture.Store, fixture.Partition,
            AggregateReplayFixture.WorkerId, oldCommandId)!.Get<CommitReceipt>());
        await PageAsync(fixture.Read(maximumEvents: RetainedTailMaximum), expectedPage);
    }

    private static async Task PageAsync(AggregateReplayPage actual, AggregateReplayPage expected)
    {
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(expected.CutPosition);
        await EqualAsync(expected, actual with { CutPosition = expected.CutPosition });
        await Assert.That(actual.Snapshot!.StateJson).IsEqualTo(AggregateReplayFixture.StateJson);
        await Assert.That(actual.Events.Length).IsEqualTo(NoTailEvents);
    }

    private static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
