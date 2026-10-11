using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorMapWriter
{
    private const long NewSlot = 1;
    private const long ExistingSlot = 0;
    private const long EmptyBytes = 0;
    private const string InvalidWrite = "The native event vector map write is inconsistent.";

    internal static EventVectorMap Write(IAtomicTransaction transaction, EventVectorMap proposed,
        ImmutableArray<EventVectorEncodedRow> payload, IOptions<DatabaseLimits> options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        var admission = new EventVectorAdmissionPolicy(options);
        var current = EventVectorMapInventory.Read(transaction, proposed.ControlPartition, proposed.MapId,
            options, cancellationToken);
        if (proposed.Released)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidWrite); }
        if (current.Header is { } original)
        {
            EventVectorInventoryValidation.Require(original, current.PayloadRows, admission);
            RequireOriginal(original, proposed);
        }
        var changed = proposed with { RetainedPayloadBytes = EventVectorRowAccounting.PayloadBytes(payload) };
        EventVectorInventoryValidation.Require(changed, payload, admission);
        EventVectorPhaseRetention.Require(changed, current.PayloadRows, payload, admission);
        var header = EventVectorRowAccounting.Header(changed, payload, admission);
        var priorBytes = checked((current.HeaderRow?.EncodedBytes ?? EmptyBytes)
            + EventVectorRowAccounting.PayloadBytes(current.PayloadRows));
        var nextBytes = checked(header.EncodedBytes + changed.RetainedPayloadBytes);
        var capacity = EventVectorCapacityLedger.Prepare(transaction, changed.ControlPartition,
            current.Header is null ? NewSlot : ExistingSlot, checked(nextBytes - priorBytes), admission);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var row in current.PayloadRows)
        { transaction.Delete(row.Key.ToArray()); }
        foreach (var row in payload)
        { EventVectorRowStorage.Write(transaction, row); }
        EventVectorRowStorage.Write(transaction, header);
        EventVectorRowStorage.Write(transaction, capacity);
        return changed;
    }

    private static void RequireOriginal(EventVectorMap original, EventVectorMap changed)
    {
        if (original.ControlPartition != changed.ControlPartition || original.MapId != changed.MapId
            || original.ControlIncarnation != changed.ControlIncarnation || original.PrincipalId != changed.PrincipalId
            || original.OriginalOpenCommandId != changed.OriginalOpenCommandId
            || original.StartPolicy != changed.StartPolicy || original.ExpiresAt != changed.ExpiresAt
            || !original.OriginalOptionsDigest.Span.SequenceEqual(changed.OriginalOptionsDigest.Span)
            || !original.OriginalSourceManifestDigest.Span.SequenceEqual(changed.OriginalSourceManifestDigest.Span))
        { throw Errors.Fail(ErrorCode.Conflict, InvalidWrite); }
    }
}
