using KeyLoad.Storage.IO;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Inspects complete native redo frames only under an already acquired original stopped-store ownership lease.</summary>
internal static class ZoneTreeNativeFrameInspection
{
    private const long InitialPosition = 0;
    private const long NextFrame = 1;
    private const int NoValueBytes = 0;
    private const int NoObservedPayload = 0;
    private const string InvalidLease = "Native frame inspection requires the original stopped owner lease.";
    private const string MissingFrame = "The original native outcome frame is unavailable or was compacted.";
    private const string AmbiguousFrame = "The original native outcome occurs in multiple journal frames.";
    private const string ExhaustedScan = "Native frame inspection exceeds its original byte or record scan budget.";

    internal static ZoneTreeNativeFrameInspectionResult Read(ZoneTreeStoreOptions options, Guid expectedNodeId,
        FileStream ownership, ReadOnlyMemory<byte> outcomeKey, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(clock);
        ZoneTreeExistingStore.Validate(options, expectedNodeId);
        if (!ownership.CanRead || !ownership.CanWrite || ownership.Length != InitialPosition
            || !string.Equals(ownership.Name, Path.Combine(options.Directory, OwnerLockFileName), StringComparison.Ordinal)
            || outcomeKey.IsEmpty || options.Incarnation is null || options.EmbeddedPointCache is not null
            || options.FaultObserver is not null)
        { throw new ArgumentException(InvalidLease, nameof(ownership)); }
        OfflineRegularFile.RequireRegular(ownership.Name);
        var identity = ZoneTreeIdentityFile.OpenExisting(options, expectedNodeId);
        var failures = new List<Exception>();
        ZoneTreeNativeFrameInspectionResult? result = null;
        ZoneTreeExistingStoreCleanup.Capture(() =>
        {
            using var journal = OfflineRegularFile.OpenReadOnlyObservation(
                Path.Combine(options.Directory, JournalFileName), options.FileBufferBytes);
            ZoneTreeExistingStoreCleanup.Capture(() =>
                result = Scan(journal, options, identity, outcomeKey, clock, cancellationToken), failures);
        }, failures);
        ZoneTreeExistingStoreCleanup.ThrowFailures(failures);
        return result ?? throw Errors.Fail(ErrorCode.RecoveryRequired, MissingFrame);
    }

    private static ZoneTreeNativeFrameInspectionResult Scan(FileStream journal, ZoneTreeStoreOptions options,
        StoreIdentity identity, ReadOnlyMemory<byte> outcomeKey, TimeProvider clock, CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        var header = new byte[HeaderLength];
        var sequence = InitialPosition;
        var examinedRecords = InitialPosition;
        var examinedFrames = InitialPosition;
        var maximumPayload = NoObservedPayload;
        ZoneTreeNativeFrameInspectionResult? selected = null;
        while (journal.Position < journal.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.GetElapsedTime(started) > options.MaximumReadCutElapsed
                || HeaderLength > options.MaximumReadCutExaminedBytes - journal.Position)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, ExhaustedScan); }
            var fields = ZoneTreeJournalFrameReader.ReadHeader(journal, header, sequence,
                options.MaxFrameBytes, identity.FormatVersion)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, MissingFrame);
            if (fields.Length > options.MaximumReadCutExaminedBytes - journal.Position)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, ExhaustedScan); }
            var frame = ZoneTreeJournalFrameReader.ReadPayload(journal, header, fields.Length, fields.Sequence)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, MissingFrame);
            maximumPayload = Math.Max(maximumPayload, frame.Payload.Length);
            var mutations = ZoneTreeJournalCodec.Deserialize(frame.Payload);
            if (mutations.Length > options.MaximumReadCutRecords - examinedRecords)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, ExhaustedScan); }
            var rawMutationBytes = mutations.Sum(mutation => checked((long)mutation.Key.Length + (mutation.Value?.Length ?? NoValueBytes)));
            examinedRecords += mutations.Length;
            examinedFrames += NextFrame;
            foreach (var mutation in mutations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (clock.GetElapsedTime(started) > options.MaximumReadCutElapsed)
                { throw Errors.Fail(ErrorCode.BudgetExceeded, ExhaustedScan); }
                if (mutation.Key.Span.SequenceEqual(outcomeKey.Span))
                { selected = Select(selected, mutation, frame, identity, examinedFrames, journal.Position, mutations.Length, rawMutationBytes); }
            }
            sequence = frame.Sequence;
        }
        return selected is { } actual
            ? actual with { ExaminedFrames = examinedFrames, ExaminedBytes = journal.Position, MaximumObservedPayloadBytes = maximumPayload }
            : throw Errors.Fail(ErrorCode.RecoveryRequired, MissingFrame);
    }

    private static ZoneTreeNativeFrameInspectionResult Select(ZoneTreeNativeFrameInspectionResult? selected,
        StorageMutation mutation, (byte[] Payload, long Sequence, byte[] Checksum) frame, StoreIdentity identity,
        long frames, long bytes, int records, long rawMutationBytes)
    {
        if (selected is not null || mutation.Value is not { } outcome)
        { throw Errors.Fail(ErrorCode.Corruption, AmbiguousFrame); }
        return new(identity.NodeId, identity.Incarnation, identity.FormatVersion, frame.Sequence, frame.Payload.Length,
            Convert.ToHexString(frame.Checksum), records, rawMutationBytes, frames, bytes, frame.Payload.Length, outcome);
    }
}
