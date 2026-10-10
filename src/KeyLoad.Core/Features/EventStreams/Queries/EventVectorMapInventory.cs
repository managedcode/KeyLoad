using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorMapInventory
{
    private const int Version = 1;
    private const long EmptyBytes = 0;
    private const string InvalidInventory = "The complete native event vector map inventory is inconsistent.";

    internal static EventVectorOwnedInventory Read(IKeyValueView view, PartitionRef partition, Guid mapId,
        IOptions<DatabaseLimits> options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(options);
        options.Value.Validate();
        var admission = new EventVectorAdmissionPolicy(options);
        var budget = new EventVectorInventoryReadBudget(options);
        var key = EventVectorKeys.Map(partition, mapId);
        EventVectorEncodedRow? headerRow = null;
        view.ReadValue(key, value =>
        {
            admission.RequireEncodedBytes(checked(key.Length + value.Length));
            headerRow = new(key, value.ToArray());
        }, budget.Observe);
        var rows = ImmutableArray.CreateBuilder<EventVectorEncodedRow>();
        ReadRange(view, EventVectorKeys.Pages(partition, mapId), rows, budget, cancellationToken);
        ReadRange(view, EventVectorKeys.Phases(partition, mapId), rows, budget, cancellationToken);
        ReadRange(view, EventVectorKeys.Proofs(partition, mapId), rows, budget, cancellationToken);
        ReadOffer(view, partition, mapId, rows, budget, admission);
        ReadParent(view, partition, mapId, rows, budget, admission);
        ReadCleanup(view, partition, mapId, rows, budget, admission);
        var payload = rows.ToImmutable();
        var header = headerRow is { } actual
            ? NativeSerialization.Deserialize<EventVectorMap>(actual.Value.Span) : null;
        RequireHeader(header, partition, mapId, payload);
        return new(header, headerRow, payload);
    }

    private static void ReadRange(IKeyValueView view, byte[] prefix,
        ImmutableArray<EventVectorEncodedRow>.Builder rows, EventVectorInventoryReadBudget budget,
        CancellationToken cancellationToken)
    {
        var scan = view.VisitRange(prefix, budget.ScanRecords, (key, value) =>
        {
            rows.Add(new(key.ToArray(), value.ToArray()));
            return true;
        }, observer: budget.Observe, cancellationToken: cancellationToken);
        if (scan.HasMore)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidInventory); }
    }

    private static void ReadOffer(IKeyValueView view, PartitionRef partition, Guid mapId,
        ImmutableArray<EventVectorEncodedRow>.Builder rows, EventVectorInventoryReadBudget budget,
        EventVectorAdmissionPolicy admission)
    {
        var key = EventVectorKeys.Offer(partition, mapId);
        view.ReadValue(key, value =>
        {
            admission.RequireEncodedBytes(checked(key.Length + value.Length));
            rows.Add(new(key, value.ToArray()));
        }, budget.Observe);
    }

    private static void ReadParent(IKeyValueView view, PartitionRef partition, Guid mapId,
        ImmutableArray<EventVectorEncodedRow>.Builder rows, EventVectorInventoryReadBudget budget,
        EventVectorAdmissionPolicy admission)
    {
        var key = EventVectorKeys.ParentCall(partition, mapId);
        view.ReadValue(key, value =>
        {
            admission.RequireEncodedBytes(checked(key.Length + value.Length));
            rows.Add(new(key, value.ToArray()));
        }, budget.Observe);
    }

    private static void ReadCleanup(IKeyValueView view, PartitionRef partition, Guid mapId,
        ImmutableArray<EventVectorEncodedRow>.Builder rows, EventVectorInventoryReadBudget budget,
        EventVectorAdmissionPolicy admission)
    {
        var key = EventVectorKeys.CleanupCall(partition, mapId);
        view.ReadValue(key, value =>
        {
            admission.RequireEncodedBytes(checked(key.Length + value.Length));
            rows.Add(new(key, value.ToArray()));
        }, budget.Observe);
    }

    private static void RequireHeader(EventVectorMap? header, PartitionRef partition, Guid mapId,
        ImmutableArray<EventVectorEncodedRow> rows)
    {
        if (header is null)
        {
            if (!rows.IsEmpty)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidInventory); }
            return;
        }
        if (header.Version != Version || header.ControlPartition != partition || header.MapId != mapId
            || header.RetainedPayloadBytes < EmptyBytes
            || header.RetainedPayloadBytes != EventVectorRowAccounting.PayloadBytes(rows))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidInventory); }
    }
}
