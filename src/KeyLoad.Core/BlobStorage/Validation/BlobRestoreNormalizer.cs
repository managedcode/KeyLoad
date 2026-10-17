using KeyLoad.Core.Features.ResourceExecution;
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
            var page = database.Store.Read(view => BlobRestorePageReader.Collect(view, expected, database.BlobExecution));
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
                {
                    throw Errors.Fail(ErrorCode.RecoveryRequired, BlobErrors.Fenced);
                }
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
            {
                throw BlobErrors.Corruption();
            }
            BlobQuotaOperations.Validate(global, global.Incarnation);
            if (global.Incarnation == database.Store.Identity.Incarnation)
            {
                return null;
            }
            ResourceRetirementStorage.RequireNoRetirement(tx, database.Limits);
            ResourceRetirementPreparationStorage.RequireNoPreparation(tx, database.Limits);
            ResourceRetirementPreparedOperationStorage.RequireNoReplacement(tx, database.Limits);
            var marker = new BlobRestoreMarker(BlobKeys.FormatVersion, global.Incarnation,
                database.Store.Identity.Incarnation, BlobRestorePhase.States, null, ReservedBytesEmptyCount, ObjectKeysEmptyCount, VersionsEmptyCount, ActiveUploadsEmptyCount, ResourcesEmptyCount);
            tx.PutRecord(BlobRestoreFence.MarkerKey, marker);
            return marker;
        });
    }

    private BlobRestoreMarker CommitPage(IAtomicTransaction tx, BlobRestoreMarker expected, List<KeyValueRecord> page)
    {
        const int EmptyPageCount = 0;

        ResourceRetirementStorage.RequireNoRetirement(tx, database.Limits);
        ResourceRetirementPreparationStorage.RequireNoPreparation(tx, database.Limits);
        ResourceRetirementPreparedOperationStorage.RequireNoReplacement(tx, database.Limits);
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
                {
                    BlobMetadataRules.EncodedLength(current.Length);
                }
                matches = current.SequenceEqual(record.Value.Span);
            });
            if (!found || !matches)
            {
                throw BlobErrors.Corruption();
            }
            marker = ApplyRecord(tx, marker, record);
            marker = marker with
            {
                ExclusiveCursor = record.Key
            };
        }
        if (page.Count == EmptyPageCount)
        {
            marker = marker with
            {
                Phase = BlobRestorePageValidation.Next(marker.Phase),
                ExclusiveCursor = null
            };
        }
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
                return marker with
                {
                    Resources = checked(marker.Resources + ResourcesStep)
                };
            case BlobRestorePhase.VerifyStates:
                return new BlobRestoreStatePage(database, marker).Process(tx, record, true);
            case BlobRestorePhase.VerifyHeads:
                return new BlobRestoreHeadPage(database, marker).Process(tx, record, true);
            case BlobRestorePhase.VerifyResources:
                return BlobRestorePageValidation.Verify(tx, marker, record);
            default:
                throw BlobErrors.Corruption();
        }
    }

    private void Finish(BlobRestoreMarker marker)
    {
        const int EmptyRestoreAccountingCount = 0;
        const int MaxRecordsSingleItemCount = 1;

        database.Store.Commit((tx, _) =>
        {
            ResourceRetirementStorage.RequireNoRetirement(tx, database.Limits);
            ResourceRetirementPreparationStorage.RequireNoPreparation(tx, database.Limits);
            ResourceRetirementPreparedOperationStorage.RequireNoReplacement(tx, database.Limits);
            RequireMarker(tx, marker);
            if (marker.Resources != EmptyRestoreAccountingCount || marker.ActiveUploads != EmptyRestoreAccountingCount
                || tx.VisitRange(BlobRestoreFence.Accounts, MaxRecordsSingleItemCount, static (_, _) => false).Records != EmptyRestoreAccountingCount)
            {
                throw BlobErrors.Corruption();
            }
            var global = new BlobRestoreAccounting(database, marker).SourceQuota(tx, BlobKeys.Global);
            var aggregate = new BlobQuota(BlobKeys.FormatVersion, marker.TargetIncarnation,
                marker.ReservedBytes, marker.ObjectKeys, marker.Versions, marker.ActiveUploads);
            BlobQuotaOperations.Validate(aggregate, marker.TargetIncarnation);
            if (!BlobRestoreAccounting.EqualCounters(global, aggregate))
            {
                throw BlobErrors.Corruption();
            }
            tx.PutRecord(BlobKeys.Global, global with
            {
                Incarnation = marker.TargetIncarnation
            });
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
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, BlobErrors.Fenced);
        }
    }
}
