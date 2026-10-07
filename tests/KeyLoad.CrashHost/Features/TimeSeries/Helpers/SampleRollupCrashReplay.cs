using KeyLoad.Core;

namespace KeyLoad.CrashHost;

internal static class SampleRollupCrashReplay
{
    private const long InitialRevision = 0;
    private const long ChangedRevision = 17;
    private const long ExpectedInflightRevision = 2;
    private const string ContentConflict = "The command ID was already used with different content.";
    private const int OneMutation = 1;
    private const int FirstMutation = 0;

    internal static void Verify(string directory, DatabaseEngine database, bool drop)
    {
        var before = SampleRollupCrashState.All(database);
        var position = database.Store.Position;
        var acknowledged = SampleRollupCrashOperations.Commit(database, SampleRollupCrashContract.AcknowledgedId,
            SampleRollupCrashContract.Refresh(InitialRevision)).Get<CommitReceipt>();
        RequireEqual(NativeSerialization.Serialize(acknowledged),
            File.ReadAllBytes(Path.Combine(directory, SampleRollupCrashContract.AcknowledgedReceiptFile)));
        var operation = NativeSerialization.Deserialize<ReplicatedOperation>(
            File.ReadAllBytes(Path.Combine(directory, SampleRollupCrashContract.OperationFile)));
        var first = database.ApplyEmbedded(operation, cancellationToken: default).Get<CommitReceipt>();
        var kind = drop ? SampleRollupProtocol.DropKind : SampleRollupProtocol.RefreshKind;
        if (first.CommandId != SampleRollupCrashContract.InflightId || first.Token.Position != position
            || first.Token.AtomicPartitionId != SampleRollupCrashContract.Partition.AtomicPartitionId
            || first.Durability != DurabilityProfile.ProcessDurable || first.Mutations.Length != OneMutation
            || !NativeSerialization.Serialize(first.Mutations[FirstMutation]).SequenceEqual(
                NativeSerialization.Serialize(new MutationReceipt(kind, SampleRollupCrashContract.Set,
                    SampleRollupCrashContract.Series, ExpectedInflightRevision))))
        { throw new InvalidOperationException(SampleRollupCrashContract.Invalid); }
        var retry = database.ApplyEmbedded(operation, cancellationToken: default).Get<CommitReceipt>();
        RequireEqual(NativeSerialization.Serialize(first), NativeSerialization.Serialize(retry));
        RequireUnchanged(database, before, position);
        VerifyChangedPayload(database, drop);
        RequireEqual(SampleRollupCrashState.Raw(database), File.ReadAllBytes(Path.Combine(directory, SampleRollupCrashContract.RawFile)));
    }

    private static void VerifyChangedPayload(DatabaseEngine database, bool drop)
    {
        var all = SampleRollupCrashState.All(database);
        var originalPosition = database.Store.Position;
        var raw = SampleRollupCrashState.Raw(database);
        var result = database.ReadSampleRollup(CrashFixtureValues.Principal, new(SampleRollupCrashContract.Partition,
            SampleRollupCrashContract.Set, SampleRollupCrashContract.Series, SampleRollupCrashContract.Start, SampleRollupCrashContract.End));
        Mutation changed = drop ? SampleRollupCrashContract.Drop(ChangedRevision) : SampleRollupCrashContract.Refresh(ChangedRevision);
        var failure = SampleRollupCrashOperations.Commit(database, SampleRollupCrashContract.InflightId, changed);
        if (failure.Error != ErrorCode.Conflict || failure.SafeDetail != ContentConflict || failure.Json is not null)
        { throw new InvalidOperationException(SampleRollupCrashContract.Invalid); }
        RequireUnchanged(database, all, originalPosition);
        var before = SampleRollupCrashState.All(database);
        var position = database.Store.Position;
        var retry = SampleRollupCrashOperations.Commit(database, SampleRollupCrashContract.InflightId, changed);
        RequireEqual(NativeSerialization.Serialize(failure), NativeSerialization.Serialize(retry));
        RequireUnchanged(database, before, position);
        RequireEqual(raw, SampleRollupCrashState.Raw(database));
        var after = database.ReadSampleRollup(CrashFixtureValues.Principal, new(SampleRollupCrashContract.Partition,
            SampleRollupCrashContract.Set, SampleRollupCrashContract.Series, SampleRollupCrashContract.Start, SampleRollupCrashContract.End));
        if (result != after)
        { throw new InvalidOperationException(SampleRollupCrashContract.Invalid); }
    }

    private static void RequireUnchanged(DatabaseEngine database, byte[] before, long position)
    {
        RequireEqual(before, SampleRollupCrashState.All(database));
        if (database.Store.Position != position)
        { throw new InvalidOperationException(SampleRollupCrashContract.Invalid); }
    }
    private static void RequireEqual(byte[] expected, byte[] actual)
    {
        if (!expected.AsSpan().SequenceEqual(actual))
        { throw new InvalidOperationException(SampleRollupCrashContract.Invalid); }
    }
}
