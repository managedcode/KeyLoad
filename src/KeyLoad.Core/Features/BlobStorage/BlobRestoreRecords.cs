using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal enum BlobRestorePhase
{
    States,
    Heads,
    Quotas,
    VerifyStates,
    VerifyHeads,
    VerifyResources,
    Complete
}

internal sealed record BlobRestoreMarker(int FormatVersion, Guid SourceIncarnation, Guid TargetIncarnation,
    BlobRestorePhase Phase, ReadOnlyMemory<byte>? ExclusiveCursor, long ReservedBytes, int ObjectKeys,
    int Versions, int ActiveUploads, int Resources);

internal static class BlobRestoreFence
{
    internal const string MarkerSpace = "blob-restore-v1";
    internal const string AccountSpace = "blob-restore-account-v1";
    internal const string CatalogSpace = "catalog";
    internal const int QuotaComponents = 5;
    internal static byte[] MarkerKey => KeyCodec.Encode(MarkerSpace);
    internal static byte[] Accounts => KeyCodec.Encode(AccountSpace);
    internal static byte[] Account(BlobRef blob) => KeyCodec.Encode(AccountSpace, blob.Partition.TenantId,
        blob.Partition.DatabaseId, blob.Partition.TransactionDomainId, blob.Resource);

    internal static void RequireOpen(IKeyValueView view)
    {
        var marker = BlobRecordReader.Get<BlobRestoreMarker>(view, MarkerKey);
        if (marker is not null)
        {
            Validate(marker);
            throw Errors.Fail(ErrorCode.RecoveryRequired, BlobErrors.Fenced);
        }
        if (view.VisitRange(Accounts, 1, static (_, _) => false).Records != 0)
        { throw BlobErrors.Corruption(); }
    }

    internal static void Validate(BlobRestoreMarker marker)
    {
        BlobRecordReader.Version(marker.FormatVersion);
        if (!Enum.IsDefined(marker.Phase) || marker.SourceIncarnation == Guid.Empty
            || marker.TargetIncarnation == Guid.Empty || marker.SourceIncarnation == marker.TargetIncarnation
            || marker.ReservedBytes is < 0 or > BlobLimits.StoreMaxReservedBytes
            || marker.ObjectKeys is < 0 or > BlobLimits.StoreMaxObjectKeys
            || marker.Versions is < 0 or > BlobLimits.StoreMaxVersions
            || marker.ActiveUploads is < 0 or > BlobLimits.StoreMaxUploads || marker.Resources < 0)
        { throw BlobErrors.Corruption(); }
        if (marker.ExclusiveCursor is { } cursor)
        {
            if (marker.Phase == BlobRestorePhase.Complete
                || !cursor.Span.StartsWith(KeyCodec.Encode(Space(marker.Phase))))
            { throw BlobErrors.Corruption(); }
            _ = KeyCodec.Decode(cursor.Span);
        }
    }

    internal static string Space(BlobRestorePhase phase) => phase switch
    {
        BlobRestorePhase.States or BlobRestorePhase.VerifyStates => BlobKeys.StateSpace,
        BlobRestorePhase.Heads or BlobRestorePhase.VerifyHeads => BlobKeys.HeadSpace,
        BlobRestorePhase.Quotas => BlobKeys.QuotaSpace,
        BlobRestorePhase.VerifyResources => CatalogSpace,
        _ => throw BlobErrors.Corruption()
    };

    internal static BlobRef QuotaScope(ReadOnlySpan<byte> key, string space)
    {
        var parts = KeyCodec.Decode(key);
        if (parts.Length != QuotaComponents || parts[0] is not string prefix || prefix != space
            || parts[1] is not string tenant || parts[2] is not string database
            || parts[3] is not string domain || parts[4] is not string resource)
        { throw BlobErrors.Corruption(); }
        var blob = new BlobRef(new(tenant, database, domain, BlobListAccumulator.ScopeId), resource, BlobListAccumulator.ScopeId);
        BlobKeys.Validate(blob);
        return blob;
    }
}
