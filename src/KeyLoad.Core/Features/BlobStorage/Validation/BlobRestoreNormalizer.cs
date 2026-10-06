using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal sealed class BlobRestoreNormalizer(DatabaseEngine database)
{
    internal void Normalize()
    {
        var marker = Initialize();
        while (marker is not null)
        {
            if (marker.Phase == BlobRestorePhase.Complete)
            {
                Finish(marker);
                return;
            }
            var expected = marker;
            var page = database.Store.Read(view => Collect(view, expected));
            marker = database.Store.Commit((tx, _) => CommitPage(tx, expected, page));
        }
    }

    private BlobRestoreMarker? Initialize()
    {
        const int ReservedBytesEmptyCount = 0;
        const int ObjectKeysEmptyCount = 0;
        const int VersionsEmptyCount = 0;
        const int ActiveUploadsEmptyCount = 0;
        const int ResourcesEmptyCount = 0;

        return database.Store.Commit((tx, _) =>
        {
            var current = BlobRecordReader.Get<BlobRestoreMarker>(tx, BlobRestoreFence.MarkerKey);
            if (current is not null)
            {
                BlobRestoreFence.Validate(current);
                if (current.TargetIncarnation != database.Store.Identity.Incarnation)
                { throw Errors.Fail(ErrorCode.RecoveryRequired, BlobErrors.Fenced); }
                return current;
            }
            BlobRestoreFence.RequireOpen(tx);
            var global = BlobRecordReader.Get<BlobQuota>(tx, BlobKeys.Global);
            if (global is null)
            {
                BlobQuotaOperations.ProveEmpty(tx, database.BlobExecution.InitialCatalogProofRecords);
                return null;
            }
            if (global.Incarnation == Guid.Empty)
            { throw BlobErrors.Corruption(); }
            BlobQuotaOperations.Validate(global, global.Incarnation);
            if (global.Incarnation == database.Store.Identity.Incarnation)
            { return null; }
            var marker = new BlobRestoreMarker(BlobKeys.FormatVersion, global.Incarnation,
                database.Store.Identity.Incarnation, BlobRestorePhase.States, null, ReservedBytesEmptyCount, ObjectKeysEmptyCount, VersionsEmptyCount, ActiveUploadsEmptyCount, ResourcesEmptyCount);
            tx.PutRecord(BlobRestoreFence.MarkerKey, marker);
            return marker;
        });
    }

    private List<KeyValueRecord> Collect(IKeyValueView view, BlobRestoreMarker marker)
    {
        const long BytesInitialValue = 0L;
        const int EmptyCollectedRecordCount = 0;

        var policy = database.BlobExecution;
        var records = new List<KeyValueRecord>(policy.MetadataPageSize);
        var prefix = KeyCodec.Encode(BlobRestoreFence.Space(marker.Phase));
        var bytes = BytesInitialValue;
        view.VisitRange(prefix, policy.MetadataPageSize, (key, value) =>
        {
            if (marker.Phase is BlobRestorePhase.States or BlobRestorePhase.Heads
                or BlobRestorePhase.VerifyStates or BlobRestorePhase.VerifyHeads)
            { BlobMetadataRules.EncodedLength(value.Length); }
            var candidate = checked((long)key.Length + value.Length);
            if (candidate > policy.RestorePageBytes - bytes && records.Count > EmptyCollectedRecordCount)
            { return false; }
            if (candidate > policy.RestorePageBytes)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, BlobErrors.RestoreWorkBudget); }
            records.Add(new(key.ToArray(), value.ToArray()));
            bytes = checked(bytes + candidate);
            return records.Count < policy.MetadataPageSize && bytes < policy.RestorePageBytes;
        }, marker.ExclusiveCursor?.ToArray());
        return records;
    }

    private BlobRestoreMarker CommitPage(IAtomicTransaction tx, BlobRestoreMarker expected, List<KeyValueRecord> page)
    {
        const int EmptyPageCount = 0;

        RequireMarker(tx, expected);
        var marker = expected;
        foreach (var record in page)
        {
            var key = record.Key.ToArray();
            var matches = false;
            var found = tx.ReadValue(key, current =>
            {
                if (marker.Phase is BlobRestorePhase.States or BlobRestorePhase.Heads
                    or BlobRestorePhase.VerifyStates or BlobRestorePhase.VerifyHeads)
                { BlobMetadataRules.EncodedLength(current.Length); }
                matches = current.SequenceEqual(record.Value.Span);
            });
            if (!found || !matches)
            { throw BlobErrors.Corruption(); }
            marker = ApplyRecord(tx, marker, record);
            marker = marker with { ExclusiveCursor = record.Key };
        }
        if (page.Count == EmptyPageCount)
        { marker = marker with { Phase = Next(marker.Phase), ExclusiveCursor = null }; }
        BlobRestoreFence.Validate(marker);
        tx.PutRecord(BlobRestoreFence.MarkerKey, marker);
        return marker;
    }

    private BlobRestoreMarker ApplyRecord(IAtomicTransaction tx, BlobRestoreMarker marker, KeyValueRecord record)
    {
        const int ResourcesStep = 1;

        switch (marker.Phase)
        {
            case BlobRestorePhase.States:
                return new BlobRestoreStatePage(database, marker).Process(tx, record, false);
            case BlobRestorePhase.Heads:
                return new BlobRestoreHeadPage(database, marker).Process(tx, record, false);
            case BlobRestorePhase.Quotas:
                new BlobRestoreAccounting(database, marker).VerifyQuota(tx, record);
                return marker with { Resources = checked(marker.Resources + ResourcesStep) };
            case BlobRestorePhase.VerifyStates:
                return new BlobRestoreStatePage(database, marker).Process(tx, record, true);
            case BlobRestorePhase.VerifyHeads:
                return new BlobRestoreHeadPage(database, marker).Process(tx, record, true);
            case BlobRestorePhase.VerifyResources:
                return VerifyResource(tx, marker, record);
            default:
                throw BlobErrors.Corruption();
        }
    }

    private static BlobRestorePhase Next(BlobRestorePhase phase) => phase switch
    {
        BlobRestorePhase.States => BlobRestorePhase.Heads,
        BlobRestorePhase.Heads => BlobRestorePhase.Quotas,
        BlobRestorePhase.Quotas => BlobRestorePhase.VerifyStates,
        BlobRestorePhase.VerifyStates => BlobRestorePhase.VerifyHeads,
        BlobRestorePhase.VerifyHeads => BlobRestorePhase.VerifyResources,
        BlobRestorePhase.VerifyResources => BlobRestorePhase.Complete,
        _ => throw BlobErrors.Corruption()
    };

    private static BlobRestoreMarker VerifyResource(IKeyValueView view, BlobRestoreMarker marker, KeyValueRecord record)
    {
        const int QuotaAccountSuffixComponents = 1;
        const int TenantComponentIndex = 1;
        const int DatabaseComponentIndex = 2;
        const int ResourceComponentIndex = 3;
        const int ResourcesValidationBoundary = 1;
        const int ResourcesStep = 1;

        var definition = BlobRecordReader.Decode<ResourceDefinition>(record.Value.Span);
        if (definition.Kind != ResourceKind.BlobStore)
        { return marker; }
        var parts = KeyCodec.Decode(record.Key.Span);
        if (parts.Length != BlobRestoreFence.QuotaComponents - QuotaAccountSuffixComponents || parts[TenantComponentIndex] is not string tenant
            || parts[DatabaseComponentIndex] is not string database || parts[ResourceComponentIndex] is not string resource || resource != definition.Name)
        { throw BlobErrors.Corruption(); }
        BlobQuotaOperations.ValidatePolicy(definition);
        _ = BlobQuotaOperations.Read(view, BlobKeys.Quota(tenant, database, definition.TransactionDomainId, resource), marker.TargetIncarnation);
        if (marker.Resources < ResourcesValidationBoundary)
        { throw BlobErrors.Corruption(); }
        return marker with { Resources = marker.Resources - ResourcesStep };
    }

    private void Finish(BlobRestoreMarker marker)
    {
        const int EmptyRestoreAccountingCount = 0;
        const int MaxRecordsSingleItemCount = 1;

        database.Store.Commit((tx, _) =>
        {
            RequireMarker(tx, marker);
            if (marker.Resources != EmptyRestoreAccountingCount || marker.ActiveUploads != EmptyRestoreAccountingCount
                || tx.VisitRange(BlobRestoreFence.Accounts, MaxRecordsSingleItemCount, static (_, _) => false).Records != EmptyRestoreAccountingCount)
            { throw BlobErrors.Corruption(); }
            var global = new BlobRestoreAccounting(database, marker).SourceQuota(tx, BlobKeys.Global);
            var aggregate = new BlobQuota(BlobKeys.FormatVersion, marker.TargetIncarnation,
                marker.ReservedBytes, marker.ObjectKeys, marker.Versions, marker.ActiveUploads);
            BlobQuotaOperations.Validate(aggregate, marker.TargetIncarnation);
            if (!BlobRestoreAccounting.EqualCounters(global, aggregate))
            { throw BlobErrors.Corruption(); }
            tx.PutRecord(BlobKeys.Global, global with { Incarnation = marker.TargetIncarnation });
            tx.Delete(BlobRestoreFence.MarkerKey);
            return true;
        });
    }

    private static void RequireMarker(IKeyValueView view, BlobRestoreMarker expected)
    {
        var current = BlobRecordReader.Get<BlobRestoreMarker>(view, BlobRestoreFence.MarkerKey)
            ?? throw BlobErrors.Corruption();
        BlobRestoreFence.Validate(current);
        if (!NativeSerialization.Serialize(current).AsSpan().SequenceEqual(NativeSerialization.Serialize(expected)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, BlobErrors.Fenced); }
    }
}
