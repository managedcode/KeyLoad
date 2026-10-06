using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal static class BlobKeys
{
    internal const string HeadSpace = "blob-head-v1";
    internal const string StateSpace = "blob-state-v1";
    internal const string PartSpace = "blob-part-v1";
    internal const string PartMetaSpace = "blob-partmeta-v1";
    internal const string QuotaSpace = "blob-quota-v1";
    internal const string GlobalSpace = "blob-global-v1";
    internal const string GuidFormat = "N";
    internal const int FormatVersion = 1;
    internal const int ScopeComponents = 7;
    internal const int StateComponents = ScopeComponents + 1;
    internal const int PartComponents = StateComponents + 1;
    internal static byte[] Global => KeyCodec.Encode(GlobalSpace);
    internal static byte[] Head(BlobRef blob) => KeySpace.Partition(HeadSpace, blob.Partition, blob.Resource, blob.Id);
    internal static byte[] Heads(PartitionRef partition, string resource) => KeySpace.Partition(HeadSpace, partition, resource);
    internal static byte[] State(BlobRef blob, Guid upload) => KeySpace.Partition(StateSpace, blob.Partition, blob.Resource, blob.Id, upload.ToString(GuidFormat));
    internal static byte[] Part(BlobRef blob, Guid upload, int ordinal) => KeySpace.Partition(PartSpace, blob.Partition, blob.Resource, blob.Id, upload.ToString(GuidFormat), (long)ordinal);
    internal static byte[] PartMeta(BlobRef blob, Guid upload, int ordinal) => KeySpace.Partition(PartMetaSpace, blob.Partition, blob.Resource, blob.Id, upload.ToString(GuidFormat), (long)ordinal);
    internal static byte[] Quota(BlobRef blob) => Quota(blob.Partition.TenantId, blob.Partition.DatabaseId, blob.Partition.TransactionDomainId, blob.Resource);
    internal static byte[] Quota(string tenant, string database, string domain, string resource) => KeyCodec.Encode(QuotaSpace, tenant, database, domain, resource);

    internal static void Validate(BlobRef blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        DatabaseEngine.ValidatePartition(blob.Partition);
        JsonData.Identifier(blob.Resource);
        JsonData.Identifier(blob.Id);
    }

    internal static BlobRef DecodeScope(ReadOnlySpan<byte> key, string space, int expected)
    {
        const int PrefixComponentIndex = 0;
        const int TenantComponentIndex = 1;
        const int DatabaseComponentIndex = 2;
        const int DomainComponentIndex = 3;
        const int PartitionComponentIndex = 4;
        const int ResourceComponentIndex = 5;
        const int IdComponentIndex = 6;

        var components = KeyCodec.Decode(key);
        if (components.Length != expected || components[PrefixComponentIndex] is not string prefix || prefix != space
            || components[TenantComponentIndex] is not string tenant || components[DatabaseComponentIndex] is not string database
            || components[DomainComponentIndex] is not string domain || components[PartitionComponentIndex] is not string partition
            || components[ResourceComponentIndex] is not string resource || components[IdComponentIndex] is not string id)
        { throw BlobErrors.Corruption(); }
        var blob = new BlobRef(new(tenant, database, domain, partition), resource, id);
        Validate(blob);
        return blob;
    }
}

internal static class BlobErrors
{
    internal const string Invalid = "The blob request or policy is invalid.";
    internal const string Damaged = "The persisted blob records are inconsistent.";
    internal const string Unsupported = "The blob record format is unsupported.";
    internal const string Fenced = "Offline blob restore normalization must complete.";
    internal const string Denied = "The persisted principal cannot access this blob.";
    internal const string Lifetime = "The selected upload lifetime is unavailable.";
    internal const string Revision = "The blob head revision changed.";
    internal const string Capacity = "The blob reservation quota is exhausted.";
    internal const string Conflict = "The blob upload conflicts with its current state.";
    internal const string Missing = "The published blob is unavailable.";
    internal const string CatalogProofBudget = "The initial blob catalog proof exceeds its bounded work limit.";
    internal const string RestoreWorkBudget = "The blob restore record exceeds the configured page byte budget.";
    internal static KeyLoadException Corruption() => Errors.Fail(ErrorCode.Corruption, Damaged);
    internal static KeyLoadException Validation() => Errors.Fail(ErrorCode.Validation, Invalid);
    internal static KeyLoadException Token() => Errors.Fail(ErrorCode.TokenInvalidated, Lifetime);
    internal static KeyLoadException StateConflict() => Errors.Fail(ErrorCode.Conflict, Conflict);
}
