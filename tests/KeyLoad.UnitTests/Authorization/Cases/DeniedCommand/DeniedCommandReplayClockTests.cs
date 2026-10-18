using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
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
        var original = fixture.Database.CreateCommand(OperationKind.SetDispatch, Guid.NewGuid(), Reader, time, JsonSerializer.Serialize(false, JsonDefaults.Options));
        var before = fixture.Store.Position;
        var first = fixture.Database.Apply(original);
        await Assert.That(Errors.Code(first.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(first.Problem?.Detail).IsEqualTo(AdministrationRequired);
        await Assert.That(fixture.Store.Position).IsEqualTo(before + 1);
        var retained = Bytes(fixture.Store);
        var retry = fixture.Database.OwnCommand(original);
        retry.Timestamp = (laterTime ? time.AddSeconds(1) : time).UtcDateTime;
        await SameResultAsync(first, fixture.Database.Apply(retry));
        await Assert.That(fixture.Store.Position).IsEqualTo(before + 1);
        await Assert.That(Bytes(fixture.Store)).IsEquivalentTo(retained, CollectionOrdering.Matching);
        var stored = fixture.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.GlobalKey(Reader, original.CommandId)))!;
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
        var expectedReference = DeniedCommandRepairContinuation.Configure(fixture);
        var writer = new PrincipalRecord(Reader, fixture.Partition.TenantId,
            [new ScopeGrant(fixture.Partition.DatabaseId, Collection, Capability.DocumentsWrite)], []);
        fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(writer)).Get<PrincipalRecord>();
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, fixture.Partition, [new PutDocument(Collection, Healthy, Json, 0)]);
        var original = fixture.Database.CreateCommand(OperationKind.Batch, id, Reader, fixture.Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(request, JsonDefaults.Options));
        var originalPosition = fixture.Store.Position;
        var originalReceipt = fixture.Database.Apply(original).Get<CommitReceipt>();
        await DeniedCommandRepairContinuation.OriginalReceiptAsync(fixture, originalReceipt, id, originalPosition);
        fixture.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(writer with
            {
                Grants = [],
                PolicyEpoch = writer.PolicyEpoch + 1
            })).Get<PrincipalRecord>();
        var position = fixture.Store.Position;
        var retained = Bytes(fixture.Store);
        var outcome = fixture.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(fixture.Partition, Reader, id)))!;
        var failure = fixture.Database.Apply(fixture.Database.OwnCommand(new Command<DatabaseCommand> { CommandId = original.CommandId, UserId = original.UserId, Timestamp = (new DateTimeOffset(original.Timestamp).AddSeconds(1)).UtcDateTime, Value = original.Value }));
        await Assert.That(Errors.Code(failure.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(failure.Problem?.Detail).IsEqualTo(ScopeDenied);
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
        await DeniedCommandRepairContinuation.RunAsync(fixture, writer, original, outcome, expectedReference);
    }

    private static async Task<DateTimeOffset> VerifyReplicaApplyAsync(TestDatabase fixture, Result<object> expected,
        Command<DatabaseCommand> original, byte[] stored)
    {
        var operation = fixture.Database.OwnCommand(new Command<DatabaseCommand> { CommandId = original.CommandId, UserId = original.UserId, Timestamp = (new DateTimeOffset(original.Timestamp).AddSeconds(1)).UtcDateTime, Value = original.Value });
        var position = fixture.Store.Position;
        await SameResultAsync(expected, fixture.Database.Apply(operation, replicationIndex: 1));
        await Assert.That(fixture.Store.Position).IsEqualTo(position + 1);
        await AssertReplicaMetadataAsync(fixture, 1, new DateTimeOffset(operation.Timestamp));
        var bytes = Bytes(fixture.Store);
        await SameResultAsync(expected, fixture.Database.Apply(operation, replicationIndex: 1));
        await Assert.That(fixture.Store.Position).IsEqualTo(position + 1);
        await Assert.That(Bytes(fixture.Store)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
        operation = fixture.Database.OwnCommand(new Command<DatabaseCommand> { CommandId = operation.CommandId, UserId = operation.UserId, Timestamp = (new DateTimeOffset(operation.Timestamp).AddSeconds(1)).UtcDateTime, Value = operation.Value });
        await SameResultAsync(expected, fixture.Database.Apply(operation, replicationIndex: 2));
        await Assert.That(fixture.Store.Position).IsEqualTo(position + 2);
        await AssertReplicaMetadataAsync(fixture, 2, new DateTimeOffset(operation.Timestamp));
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.GlobalKey(Reader, original.CommandId))))
            .IsEquivalentTo(stored, CollectionOrdering.Matching);
        return new DateTimeOffset(operation.Timestamp);
    }

    private static async Task AssertReplicaMetadataAsync(TestDatabase fixture, long index, DateTimeOffset time)
    {
        await Assert.That(fixture.Store.Read(view => NativeSerialization.Deserialize<long>(view.ReadOwnedValue(KeySpace.AppliedBytes)!)))
            .IsEqualTo(index);
        await Assert.That(fixture.Store.Read(view => NativeSerialization.Deserialize<DateTimeOffset>(view.ReadOwnedValue(KeySpace.ClockBytes)!)))
            .IsEqualTo(time);
    }
    private static async Task SameResultAsync(Result<object> expected, Result<object> actual)
        => await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(expected)));
    private static string[] Bytes(ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], MaximumRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException(Overflow);
        }
        return page.Records.Select(item => Convert.ToHexString(item.Key.Span) + Separator + Convert.ToHexString(item.Value.Span)).ToArray();
    });
}
