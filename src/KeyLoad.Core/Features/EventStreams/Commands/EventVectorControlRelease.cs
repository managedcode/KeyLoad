using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorControlRelease
{
    private const long ReleasedSlot = -1;
    private const string IncompleteRelease = "The original event vector release has unsettled source ownership.";

    internal static EventVectorMap Execute(IAtomicTransaction transaction, EventVectorControlPhase phase,
        IOptions<DatabaseLimits> options, CancellationToken cancellationToken)
    {
        if (!phase.OriginalCleanupFrontierBytes.IsEmpty)
        { return EventVectorAbandonmentComplete.Execute(transaction, phase, options, cancellationToken); }
        var admission = new EventVectorAdmissionPolicy(options);
        var request = phase.OriginalRequest;
        var inventory = EventVectorMapInventory.Read(transaction, request.ControlPartition,
            request.MapId, options, cancellationToken);
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, IncompleteRelease);
        EventVectorInventoryValidation.Require(map, inventory.PayloadRows, admission);
        var entries = EventVectorControlPages.Entries(inventory, admission);
        var supplied = EventVectorEntryEncoding.Decode(phase.OriginalEncodedEntries, phase.OriginalEntryChecksum, admission);
        if (phase.Action != EventVectorControlAction.CompleteRelease || entries.Length != supplied.Length)
        { throw Errors.Fail(ErrorCode.Corruption, IncompleteRelease); }
        RequireEntries(entries, supplied);
        var observed = EventVectorControlPublication.Phases(inventory, EventVectorSourcePhaseRole.Release, admission);
        if (observed.Count != entries.Length)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, IncompleteRelease); }
        if (entries.IsEmpty && map.PendingPhaseId is null
            && map.State is EventVectorMapState.Active or EventVectorMapState.Opening)
        { map = map with { State = EventVectorMapState.Releasing }; }
        var complete = ImmutableArray.CreateBuilder<EventVectorSourcePhase>(entries.Length);
        for (var ordinal = EventVectorControlPages.FirstOrdinal; ordinal < entries.Length; ordinal++)
        {
            if (!observed.TryGetValue(ordinal, out var current))
            { throw Errors.Fail(ErrorCode.RecoveryRequired, IncompleteRelease); }
            complete.Add(current);
        }
        var key = EventVectorKeys.Proof(map.ControlPartition, map.MapId, map.CoverageGeneration);
        var proof = inventory.PayloadRows.Single(row => row.Key.Span.SequenceEqual(key));
        var changed = EventVectorReleasePass.Complete(map, entries, complete.MoveToImmutable(), proof, admission);
        if (!changed.Released)
        { return EventVectorMapWriter.Write(transaction, changed, inventory.PayloadRows, options, cancellationToken); }
        DeleteSettled(transaction, inventory, changed, admission, cancellationToken);
        return changed;
    }

    private static void RequireEntries(ImmutableArray<EventVectorEntry> original, ImmutableArray<EventVectorEntry> supplied)
    {
        for (var ordinal = EventVectorControlPages.FirstOrdinal; ordinal < original.Length; ordinal++)
        {
            if (original[ordinal] != supplied[ordinal])
            { throw Errors.Fail(ErrorCode.Conflict, IncompleteRelease); }
        }
    }

    internal static void DeleteSettled(IAtomicTransaction transaction, EventVectorOwnedInventory inventory,
        EventVectorMap released, EventVectorAdmissionPolicy admission, CancellationToken cancellationToken)
    {
        var header = inventory.HeaderRow ?? throw Errors.Fail(ErrorCode.Corruption, IncompleteRelease);
        var bytes = checked(header.EncodedBytes + EventVectorRowAccounting.PayloadBytes(inventory.PayloadRows));
        var capacity = EventVectorCapacityLedger.Prepare(transaction, released.ControlPartition, ReleasedSlot, -bytes, admission);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var row in inventory.PayloadRows)
        { transaction.Delete(row.Key.ToArray()); }
        transaction.Delete(header.Key.ToArray());
        EventVectorRowStorage.Write(transaction, capacity);
    }
}
