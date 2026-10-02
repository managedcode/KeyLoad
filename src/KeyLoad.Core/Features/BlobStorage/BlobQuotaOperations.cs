using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal static class BlobQuotaOperations
{
    private const string CatalogSpace = "catalog";
    private static readonly string[] FeatureSpaces = [BlobKeys.HeadSpace, BlobKeys.StateSpace, BlobKeys.PartSpace,
        BlobKeys.PartMetaSpace, BlobKeys.QuotaSpace];

    internal static void ValidatePolicy(ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Kind != ResourceKind.BlobStore)
        {
            if (definition.BlobPolicy is not null)
            { throw BlobErrors.Validation(); }
            return;
        }
        var policy = definition.BlobPolicy ?? new BlobPolicy();
        if (!definition.Indexes.IsEmpty || !definition.FieldPolicies.IsEmpty || !definition.HeaderPolicies.IsEmpty
            || definition.Authority != DocumentAuthority.Document || policy.MaxBlobBytes < 1
            || policy.MaxBlobBytes > BlobLimits.MaximumBlobBytes || policy.MaxReservedBytes < policy.MaxBlobBytes
            || policy.MaxReservedBytes > BlobLimits.StoreMaxReservedBytes || policy.MaxObjectKeys is < 1 or > BlobLimits.StoreMaxObjectKeys
            || policy.MaxVersions is < 1 or > BlobLimits.StoreMaxVersions || policy.MaxUploads is < 1 or > BlobLimits.StoreMaxUploads
            || policy.UploadTtlSeconds is < BlobLimits.MinimumUploadTtlSeconds or > BlobLimits.MaximumUploadTtlSeconds)
        { throw BlobErrors.Validation(); }
    }

    internal static void Configure(IAtomicTransaction tx, ConfigureResourceRequest request, bool isNew, Guid incarnation)
    {
        ValidatePolicy(request.Definition);
        if (request.Definition.Kind != ResourceKind.BlobStore)
        { return; }
        BlobRestoreFence.RequireOpen(tx);
        var global = BlobRecordReader.Get<BlobQuota>(tx, BlobKeys.Global);
        if (global is null)
        {
            ProveEmpty(tx);
            global = BlobQuota.Empty(incarnation);
            tx.PutRecord(BlobKeys.Global, global);
        }
        Validate(global, incarnation);
        var key = BlobKeys.Quota(request.TenantId, request.DatabaseId, request.Definition.TransactionDomainId, request.Definition.Name);
        var quota = BlobRecordReader.Get<BlobQuota>(tx, key);
        if (isNew && quota is null)
        { tx.PutRecord(key, BlobQuota.Empty(incarnation)); }
        else if (quota is null || isNew)
        { throw BlobErrors.Corruption(); }
        else
        { Validate(quota, incarnation); }
    }

    internal static void ProveEmpty(IKeyValueView view)
    {
        foreach (var space in FeatureSpaces)
        {
            if (view.VisitRange(KeyCodec.Encode(space), 1, static (_, _) => false).Records != 0)
            { throw BlobErrors.Corruption(); }
        }
        var existing = false;
        var scan = view.VisitRange(KeyCodec.Encode(CatalogSpace), BlobKeys.InitialCatalogProofRecords, (_, value) =>
        {
            if (BlobRecordReader.Decode<ResourceDefinition>(value).Kind == ResourceKind.BlobStore)
            { existing = true; return false; }
            return true;
        });
        if (existing)
        { throw BlobErrors.Corruption(); }
        if (scan.HasMore)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, BlobErrors.CatalogProofBudget); }
    }

    internal static BlobQuota Read(IKeyValueView view, byte[] key, Guid incarnation)
    {
        var quota = BlobRecordReader.Get<BlobQuota>(view, key) ?? throw BlobErrors.Corruption();
        Validate(quota, incarnation);
        return quota;
    }

    internal static void Validate(BlobQuota quota, Guid incarnation)
    {
        BlobRecordReader.Version(quota.FormatVersion);
        if (quota.Incarnation != incarnation)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, BlobErrors.Fenced); }
        if (quota.ReservedBytes is < 0 or > BlobLimits.StoreMaxReservedBytes
            || quota.ObjectKeys is < 0 or > BlobLimits.StoreMaxObjectKeys
            || quota.Versions is < 0 or > BlobLimits.StoreMaxVersions
            || quota.Uploads < 0 || quota.Uploads > quota.Versions || quota.Uploads > BlobLimits.StoreMaxUploads
            || quota.ObjectKeys == 0 && quota.Versions != 0 || quota.Versions == 0 && quota.ReservedBytes != 0)
        { throw BlobErrors.Corruption(); }
    }

    internal static void Change(IAtomicTransaction tx, BlobRef blob, Guid incarnation, BlobPolicy policy,
        long bytes, int keys, int versions, int uploads)
    {
        var resource = Read(tx, BlobKeys.Quota(blob), incarnation);
        var global = Read(tx, BlobKeys.Global, incarnation);
        if (resource.ReservedBytes > global.ReservedBytes || resource.ObjectKeys > global.ObjectKeys
            || resource.Versions > global.Versions || resource.Uploads > global.Uploads)
        { throw BlobErrors.Corruption(); }
        resource = Adjust(resource, bytes, keys, versions, uploads);
        global = Adjust(global, bytes, keys, versions, uploads);
        if (resource.ReservedBytes > policy.MaxReservedBytes || resource.ObjectKeys > policy.MaxObjectKeys
            || resource.Versions > policy.MaxVersions || resource.Uploads > policy.MaxUploads)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, BlobErrors.Capacity); }
        Validate(global, incarnation);
        tx.PutRecord(BlobKeys.Quota(blob), resource);
        tx.PutRecord(BlobKeys.Global, global);
    }

    internal static BlobQuota Adjust(BlobQuota quota, long bytes, int keys, int versions, int uploads)
    {
        var updated = quota with
        {
            ReservedBytes = checked(quota.ReservedBytes + bytes),
            ObjectKeys = checked(quota.ObjectKeys + keys),
            Versions = checked(quota.Versions + versions),
            Uploads = checked(quota.Uploads + uploads)
        };
        if (updated.ReservedBytes < 0 || updated.ObjectKeys < 0 || updated.Versions < 0 || updated.Uploads < 0)
        { throw BlobErrors.Corruption(); }
        if (updated.ReservedBytes > BlobLimits.StoreMaxReservedBytes || updated.ObjectKeys > BlobLimits.StoreMaxObjectKeys
            || updated.Versions > BlobLimits.StoreMaxVersions || updated.Uploads > BlobLimits.StoreMaxUploads)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, BlobErrors.Capacity); }
        return updated;
    }
}
