using System.Collections.Immutable;
using System.Runtime.InteropServices;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class ReplicaInspectionResolutionReferencesTests
{
    [Test]
    public async Task AcIs005GeneratedSharedOperationsKeepOneSessionReferencesAndExactNativeMeasure()
    {
        using var database = new TestDatabase();
        var operation = database.Database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.SetDispatch,
            "principal-Україна", DateTimeOffset.UnixEpoch, " true "));
        ImmutableArray<ReplicaEntry> entries = [new(1, 3, operation), new(2, 3, operation)];
        var bytes = ReplicaProtocolCodec.Serialize(new AppendRequest("leader", 3, 0, 0, 0, entries));
        var inspected = ReplicaNativeInspection.Inspect<AppendRequest>(bytes, 2);
        var first = inspected.Value.Entries[0].Operation!;
        var second = inspected.Value.Entries[1].Operation!;
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(inspected.Metadata(first.PrincipalId, 64)).IsEqualTo(operation.PrincipalId);
        await Assert.That(inspected.Utf8Length(first.PayloadJson)).IsEqualTo(6);
        await Assert.That(first.NativePayload.Span.SequenceEqual(operation.NativePayload.Span)).IsTrue();
        await Assert.That(MemoryMarshal.TryGetArray(first.NativePayload, out var segment)).IsTrue();
        await Assert.That(ReferenceEquals(segment.Array, bytes)).IsTrue();
        await Assert.That(inspected.MeasureEntries(inspected.Value.Entries)).IsEqualTo(ReplicaProtocolCodec.SerializeEntries(entries).LongLength);
    }
}
