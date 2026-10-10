using System.Collections.Immutable;
using KeyLoad.Core.Features.BackupRestore.Serialization;
using KeyLoad.Core.Features.BackupRestore.Validation;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorRosterRead
{
    private const int ComponentCount = 7;
    private const int NoOrigins = 0;
    private const int TenantComponent = 3;
    private const int DatabaseComponent = 4;
    private const int DomainComponent = 5;
    private const int PartitionComponent = 6;
    private const string InvalidRoster = "The complete authorized native event vector partition roster is inconsistent.";

    internal static EventVectorRosterReadResult Read(IKeyValueView boundedView, PartitionRef control,
        string domain, Guid currentIncarnation, long storeCut, long appliedCut,
        IOptions<DatabaseLimits> options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(boundedView);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(options);
        options.Value.Validate();
        var budget = new EventVectorInventoryReadBudget(options);
        var entries = ImmutableArray.CreateBuilder<AtomicPartitionCatalogEntryV1>();
        var rows = ImmutableArray.CreateBuilder<EventVectorCoverageRow>();
        var origins = ImmutableArray.CreateBuilder<EventVectorCoverageRow>();
        var seen = new HashSet<PartitionRef>();
        var scan = boundedView.VisitRange(AtomicPartitionRosterRestoreOriginSerialization.EntryPrefix(),
            budget.ScanRecords, (key, value) =>
            {
                var parts = KeyCodec.Decode(key);
                if (parts.Length != ComponentCount
                    || parts[TenantComponent] is not string || parts[DatabaseComponent] is not string
                    || parts[DomainComponent] is not string || parts[PartitionComponent] is not string)
                { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
                if (!Equals(parts[TenantComponent], control.TenantId)
                    || !Equals(parts[DatabaseComponent], control.DatabaseId) || !Equals(parts[DomainComponent], domain))
                { return true; }
                var entry = NativeSerialization.Deserialize<AtomicPartitionCatalogEntryV1>(value);
                RequireEntry(boundedView, entry, key, value, currentIncarnation, storeCut, appliedCut);
                if (!seen.Add(entry.Partition))
                { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
                entries.Add(entry);
                rows.Add(new EventVectorCoverageRow { Key = key.ToArray(), Value = value.ToArray() });
                ReadOrigin(boundedView, entry.Partition, origins, budget);
                return true;
            }, observer: budget.Observe, cancellationToken: cancellationToken);
        if (scan.HasMore || scan.StoppedByVisitor)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidRoster); }
        var identity = ReadIdentity(boundedView, origins.Count, budget);
        return new(entries.ToImmutable(), rows.ToImmutable(), origins.ToImmutable(), identity);
    }

    private static void RequireEntry(IKeyValueView view, AtomicPartitionCatalogEntryV1 entry,
        ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, Guid incarnation, long storeCut, long appliedCut)
    {
        if (entry is null || entry.Partition is null
            || !key.SequenceEqual(AtomicPartitionRosterKeys.Partition(entry.Partition)))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
        var historical = AtomicPartitionRosterOriginValidation.ReadBound(view, entry, value.ToArray(), incarnation);
        AtomicPartitionRosterEntryValidation.Validate(entry, entry.Partition, storeCut, Math.Max(appliedCut, historical));
    }

    private static void ReadOrigin(IKeyValueView view, PartitionRef partition,
        ImmutableArray<EventVectorCoverageRow>.Builder origins, EventVectorInventoryReadBudget budget)
    {
        var key = AtomicPartitionRosterRestoreOriginSerialization.OriginKey(partition);
        view.ReadValue(key, value => origins.Add(new EventVectorCoverageRow
        { Key = key, Value = value.ToArray() }), budget.Observe);
    }

    private static EventVectorCoverageRow? ReadIdentity(IKeyValueView view, int originCount,
        EventVectorInventoryReadBudget budget)
    {
        if (originCount == NoOrigins)
        { return null; }
        var key = AtomicPartitionRosterRestoreOriginSerialization.IdentityKey();
        EventVectorCoverageRow? result = null;
        view.ReadValue(key, value => result = new EventVectorCoverageRow
        { Key = key, Value = value.ToArray() }, budget.Observe);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, InvalidRoster);
    }
}
