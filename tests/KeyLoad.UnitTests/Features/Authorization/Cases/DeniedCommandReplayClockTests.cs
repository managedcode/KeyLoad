using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class DeniedCommandReplayClockTests
{
    private const string Reader = "denied-replay-reader";
    private const string Collection = "denied-replay-documents";
    private const string Healthy = "healthy";
    private const string Json = "{\"healthy\":true}";
    private const string ScopeDenied = "The principal cannot perform this operation in this scope.";
    private const string AdministrationRequired = "Cluster administration is required.";
    private const int MaximumRecords = 4_096;
    private const string Overflow = "The denied-command replay fixture exceeded its fixed native inventory.";
    private const string Separator = ":";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcAuthReplay001DeniedSameIdRetainsOneClockCutAndPositiveReplicaApplyRemainsOrdered(bool laterTime)
    {
        using var fixture = new TestDatabase();
        fixture.Configure(Collection, ResourceKind.Collection);
        fixture.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(new(Reader, fixture.Partition.TenantId, [], []))).Get<PrincipalRecord>();
        var time = fixture.Database.EvaluationClock.GetUtcNow();
        var original = new ReplicatedOperation(Guid.NewGuid(), OperationKind.SetDispatch, Reader,
            time, JsonSerializer.Serialize(false, JsonDefaults.Options));
        var before = fixture.Store.Position;
        var first = fixture.Database.Apply(original);
        await Assert.That(first.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(first.SafeDetail).IsEqualTo(AdministrationRequired);
        await Assert.That(fixture.Store.Position).IsEqualTo(before + 1);
        var retained = Bytes(fixture.Store);
        var retry = original with { EvaluatedAt = laterTime ? time.AddSeconds(1) : time };
        await SameResultAsync(first, fixture.Database.Apply(retry));
        await Assert.That(fixture.Store.Position).IsEqualTo(before + 1);
        await Assert.That(Bytes(fixture.Store)).IsEquivalentTo(retained, CollectionOrdering.Matching);
        var stored = fixture.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.GlobalKey(Reader, original.Id)))!;
        var lastTime = await VerifyReplicaApplyAsync(fixture, first, retry, stored);
        var id = Guid.NewGuid();
        fixture.Submit(OperationKind.Batch, new CommandRequest(id, fixture.Partition,
            [new PutDocument(Collection, Healthy, Json, 0)]), id: id, time: lastTime.AddSeconds(1)).Get<CommitReceipt>();
        var document = fixture.Database.GetDocument("root", new(fixture.Partition, Collection, Healthy));
        await Assert.That(document!.Revision).IsEqualTo(1L);
        await Assert.That(document.Json).IsEqualTo(Json);
    }

    [Test]
    public async Task AcAuthReplay001RemovedGrantRejectsCachedSuccessWithoutChangingItsReceiptOrClock()
    {
        using var fixture = new TestDatabase();
        fixture.Configure(Collection, ResourceKind.Collection);
        var writer = new PrincipalRecord(Reader, fixture.Partition.TenantId,
            [new ScopeGrant(fixture.Partition.DatabaseId, Collection, Capability.DocumentsWrite)], []);
        fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(writer)).Get<PrincipalRecord>();
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, fixture.Partition, [new PutDocument(Collection, Healthy, Json, 0)]);
        var original = new ReplicatedOperation(id, OperationKind.Batch, Reader,
            fixture.Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(request, JsonDefaults.Options));
        fixture.Database.Apply(original).Get<CommitReceipt>();
        fixture.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(writer with { Grants = [], PolicyEpoch = writer.PolicyEpoch + 1 })).Get<PrincipalRecord>();
        var position = fixture.Store.Position;
        var retained = Bytes(fixture.Store);
        var outcome = fixture.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(fixture.Partition, Reader, id)))!;
        var failure = fixture.Database.Apply(original with { EvaluatedAt = original.EvaluatedAt.AddSeconds(1) });
        await Assert.That(failure.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(failure.SafeDetail).IsEqualTo(ScopeDenied);
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(Bytes(fixture.Store)).IsEquivalentTo(retained, CollectionOrdering.Matching);
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(fixture.Partition, Reader, id))))
            .IsEquivalentTo(outcome, CollectionOrdering.Matching);
        var document = fixture.Database.GetDocument("root", new(fixture.Partition, Collection, Healthy));
        await Assert.That(document!.Revision).IsEqualTo(1L);
        await Assert.That(document.Json).IsEqualTo(Json);
        fixture.Commit(new PutDocument(Collection, Healthy, Json, 1));
        await Assert.That(fixture.Database.GetDocument("root", new(fixture.Partition, Collection, Healthy))!.Revision)
            .IsEqualTo(2L);
    }

    private static async Task<DateTimeOffset> VerifyReplicaApplyAsync(TestDatabase fixture, OperationResult expected,
        ReplicatedOperation original, byte[] stored)
    {
        var operation = original with { EvaluatedAt = original.EvaluatedAt.AddSeconds(1) };
        var position = fixture.Store.Position;
        await SameResultAsync(expected, fixture.Database.Apply(operation, replicationIndex: 1));
        await Assert.That(fixture.Store.Position).IsEqualTo(position + 1);
        await AssertReplicaMetadataAsync(fixture, 1, operation.EvaluatedAt);
        var bytes = Bytes(fixture.Store);
        await SameResultAsync(expected, fixture.Database.Apply(operation, replicationIndex: 1));
        await Assert.That(fixture.Store.Position).IsEqualTo(position + 1);
        await Assert.That(Bytes(fixture.Store)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
        operation = operation with { EvaluatedAt = operation.EvaluatedAt.AddSeconds(1) };
        await SameResultAsync(expected, fixture.Database.Apply(operation, replicationIndex: 2));
        await Assert.That(fixture.Store.Position).IsEqualTo(position + 2);
        await AssertReplicaMetadataAsync(fixture, 2, operation.EvaluatedAt);
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.GlobalKey(Reader, original.Id))))
            .IsEquivalentTo(stored, CollectionOrdering.Matching);
        return operation.EvaluatedAt;
    }

    private static async Task AssertReplicaMetadataAsync(TestDatabase fixture, long index, DateTimeOffset time)
    {
        await Assert.That(fixture.Store.Read(view => NativeSerialization.Deserialize<long>(view.ReadOwnedValue(KeySpace.AppliedBytes)!)))
            .IsEqualTo(index);
        await Assert.That(fixture.Store.Read(view => NativeSerialization.Deserialize<DateTimeOffset>(view.ReadOwnedValue(KeySpace.ClockBytes)!)))
            .IsEqualTo(time);
    }
    private static async Task SameResultAsync(OperationResult expected, OperationResult actual)
        => await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(expected)));
    private static string[] Bytes(ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], MaximumRecords);
        if (page.HasMore)
        { throw new InvalidOperationException(Overflow); }
        return page.Records.Select(item => Convert.ToHexString(item.Key.Span) + Separator + Convert.ToHexString(item.Value.Span)).ToArray();
    });
}
