using System.Collections.Immutable;
using System.Text;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class ReplicaNativeEnumHeaderTests
{
    private const string Sender = "voter-a";
    private const string Collection = "orders";
    private const string InvalidJson = "{";
    private const string NullRoot = "null";
    private const string BooleanJson = "true";
    private const int MaximumEntries = 1;
    private const int ControlBytes = 4_096;

    [Test]
    [Arguments(OperationKind.Batch)]
    [Arguments(OperationKind.SetDispatch)]
    public async Task GeneratedNativeOperationEnumsReachRealStrictAppendInspection(OperationKind kind)
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var id = Guid.NewGuid();
        var json = kind == OperationKind.SetDispatch ? BooleanJson : Encoding.UTF8.GetString(
            JsonDefaults.Serialize(new CommandRequest(id, database.Partition, [new PutDocument(Collection, Sender, "{}")])));
        var operation = database.Database.NormalizeOperation(new(id, kind, NativeAuthorityFixture.Root,
            database.Database.EvaluationClock.GetUtcNow(), json));
        ImmutableArray<ReplicaEntry> entries = [new(1, 1, operation)];
        var append = new AppendRequest(Sender, 1, 0, 0, 0, entries);
        var inspected = ReplicaNativeInspection.Inspect<AppendRequest>(ReplicaProtocolCodec.Serialize(append), MaximumEntries);
        var retained = inspected.Value.Entries[0].Operation!;
        var before = database.Store.Position;
        await Assert.That(retained.Kind).IsEqualTo(kind);
        await Assert.That(ReplicaNativeOperationAdmission.Validate(inspected, retained, database.Database, ControlBytes))
            .IsEqualTo(kind == OperationKind.SetDispatch);
        var wrapper = ReplicaNativeInspection.InspectNative<NativeCommandPayload>(retained.NativePayload, 0);
        await Assert.That(wrapper.Value.Error).IsNull();
        await Assert.That(inspected.MeasureEntries(inspected.Value.Entries)).IsEqualTo(ReplicaProtocolCodec.MeasureEntries(entries));
        await Assert.That(database.Store.Position).IsEqualTo(before);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GeneratedNativeNullableErrorEnumsPreserveSignedFailureValues(bool nullRoot)
    {
        using var database = new TestDatabase();
        var operation = database.Database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.Batch, NativeAuthorityFixture.Root,
            database.Database.EvaluationClock.GetUtcNow(), nullRoot ? NullRoot : InvalidJson));
        var inspected = ReplicaNativeInspection.Inspect<ReplicatedOperation>(ReplicaProtocolCodec.Serialize(operation), MaximumEntries);
        var before = database.Store.Position;
        await Assert.That(ReplicaNativeOperationAdmission.Validate(inspected, inspected.Value, database.Database, ControlBytes)).IsFalse();
        var wrapper = ReplicaNativeInspection.InspectNative<NativeCommandPayload>(operation.NativePayload, 0);
        await Assert.That(wrapper.Value.Error).IsEqualTo(nullRoot ? ErrorCode.Corruption : ErrorCode.Validation);
        await Assert.That(wrapper.Value.Value.IsEmpty).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(before);
    }

    [Test]
    [Arguments(ReplicaWrongEnumScalar.Int64)]
    [Arguments(ReplicaWrongEnumScalar.String)]
    public async Task WrongNativeOperationEnumScalarMetadataRemainsRejected(ReplicaWrongEnumScalar scalar)
    {
        using var database = new TestDatabase();
        var operation = NativeAuthorityFixture.Create(database);
        var bytes = ReplicaNativeFixtureWriter.Encode(operation, new ReplicaWrongOperationEnumCodec(scalar));
        var before = database.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ReplicaNativeInspection.Inspect<ReplicatedOperation>(bytes, MaximumEntries));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
        await Assert.That(OutcomeStoreOracle.Read(database.Store, operation)).IsNull();
    }

    [Test]
    [Arguments(ReplicaWrongEnumScalar.Int64)]
    [Arguments(ReplicaWrongEnumScalar.String)]
    public async Task WrongNativeNullableErrorScalarMetadataRemainsRejected(ReplicaWrongEnumScalar scalar)
    {
        using var database = new TestDatabase();
        var operation = database.Database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.Batch, NativeAuthorityFixture.Root,
            database.Database.EvaluationClock.GetUtcNow(), NullRoot));
        var payload = NativeAuthorityFixture.Read(operation);
        var bytes = ReplicaNativeFixtureWriter.Encode(payload, new ReplicaWrongErrorEnumCodec(scalar));
        var before = database.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ReplicaNativeInspection.InspectNative<NativeCommandPayload>(
            bytes.AsMemory(ReplicaProtocol.PayloadPrefixBytes), 0));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
        await Assert.That(OutcomeStoreOracle.Read(database.Store, operation)).IsNull();
    }
}
