using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests;

/// <summary>AC-IS-004/005: operation authority is canonical, owned and required before durable mutation.</summary>
internal sealed class ReplicaNativeAuthorityTests
{
    private const string Principal = "principal";
    private const string BooleanPayload = " true ";
    private const string CanonicalDirectory = "canonical";
    private const byte CorruptByte = byte.MaxValue;

    [Test]
    public async Task VerifiedReplicaEntryOwnsPayloadAfterCallerMutation()
    {
        using var files = new ReplicaNativeFiles();
        using var canonical = files.Open(CanonicalDirectory);
        var database = new DatabaseEngine(canonical, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.TimeSeriesExecution());
        var original = database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.SetDispatch, Principal,
            DateTimeOffset.UnixEpoch, BooleanPayload));
        var callerBytes = original.NativePayload.ToArray();
        var callerOperation = original with { NativePayload = callerBytes };
        var owned = ReplicaOperationAuthority.Own(new(1, 1, callerOperation), database);
        Array.Fill(callerBytes, CorruptByte);
        await Assert.That(database.NativeOperationsEqual(owned.Operation!, original)).IsTrue();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => database.VerifyOperationAuthority(callerOperation)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        using var store = files.Open();
        using var log = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(files.Configuration), canonicalDatabase: database);
        log.SaveTermAndVote(1, null);
        log.Append([owned]);
        await Assert.That(database.NativeOperationsEqual(log.ReadEntry(1)!.Operation!, original)).IsTrue();
    }

    [Test]
    public async Task CopiedNativeOperationCanRetryItsCommittedEntryWithoutMemoryIdentityConflict()
    {
        using var files = new ReplicaNativeFiles();
        using var canonical = files.Open(CanonicalDirectory);
        var database = new DatabaseEngine(canonical, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.TimeSeriesExecution());
        var operation = database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.SetDispatch, Principal,
            DateTimeOffset.UnixEpoch, BooleanPayload));
        var decoded = ReplicaProtocolCodec.Deserialize<ReplicatedOperation>(ReplicaProtocolCodec.Serialize(operation));
        await Assert.That(decoded.NativePayload.Span.SequenceEqual(operation.NativePayload.Span)).IsTrue();
        using var store = files.Open();
        using var log = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(files.Configuration), canonicalDatabase: database);
        log.SaveTermAndVote(1, null);
        log.Append([new(1, 1, operation)]);
        log.Commit(1);
        log.Append([new(1, 1, decoded)]);
        await Assert.That(log.State.CommittedIndex).IsEqualTo(1);
        await Assert.That(log.ReadEntry(1)!.Operation!.PayloadJson).IsEqualTo(BooleanPayload);
        await Assert.That(log.ReadEntry(1)!.Operation!.NativePayload.Span.SequenceEqual(operation.NativePayload.Span)).IsTrue();
    }

    [Test]
    public async Task OperationsRequireCanonicalAuthorityBeforeMutationAndBeforeReopen()
    {
        using var files = new ReplicaNativeFiles();
        using var canonical = files.Open(CanonicalDirectory);
        var database = new DatabaseEngine(canonical, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.TimeSeriesExecution());
        var operation = database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.SetDispatch, Principal,
            DateTimeOffset.UnixEpoch, BooleanPayload));
        using var store = files.Open();
        using (var metadataOnly = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(files.Configuration)))
        {
            metadataOnly.SaveTermAndVote(1, null);
            var journal = await File.ReadAllBytesAsync(files.JournalPath);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => metadataOnly.Append([new(1, 1, operation)])).Code)
                .IsEqualTo(ErrorCode.RecoveryRequired);
            await Assert.That(metadataOnly.State.LastIndex).IsEqualTo(0);
            await Assert.That(metadataOnly.ReadEntry(1)).IsNull();
            await Assert.That(await File.ReadAllBytesAsync(files.JournalPath)).IsEquivalentTo(journal, CollectionOrdering.Matching);
        }
        using (var accepted = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(files.Configuration), canonicalDatabase: database))
        {
            accepted.Append([new(1, 1, operation)]);
            accepted.Commit(1);
        }
        var persisted = await File.ReadAllBytesAsync(files.JournalPath);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var rejected = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(files.Configuration));
        }).Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(await File.ReadAllBytesAsync(files.JournalPath)).IsEquivalentTo(persisted, CollectionOrdering.Matching);
    }

    [Test]
    public async Task JsonOnlyOperationCannotInvokeRuntimeFallbackWithCanonicalAuthority()
    {
        using var files = new ReplicaNativeFiles();
        using var canonical = files.Open(CanonicalDirectory);
        var database = new DatabaseEngine(canonical, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.TimeSeriesExecution());
        using var store = files.Open();
        using var log = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(files.Configuration), canonicalDatabase: database);
        log.SaveTermAndVote(1, null);
        var journal = await File.ReadAllBytesAsync(files.JournalPath);
        var operation = new ReplicatedOperation(Guid.NewGuid(), OperationKind.SetDispatch, Principal,
            DateTimeOffset.UnixEpoch, BooleanPayload);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => log.Append([new(1, 1, operation)])).Code)
            .IsEqualTo(ErrorCode.Corruption);
        await Assert.That(log.State.LastIndex).IsEqualTo(0);
        await Assert.That(log.ReadEntry(1)).IsNull();
        await Assert.That(await File.ReadAllBytesAsync(files.JournalPath)).IsEquivalentTo(journal, CollectionOrdering.Matching);
    }
}
