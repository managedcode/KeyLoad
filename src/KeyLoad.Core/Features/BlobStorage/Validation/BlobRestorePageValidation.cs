using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal static class BlobRestorePageValidation
{
    internal static BlobRestoreMarker Verify(IKeyValueView view, BlobRestoreMarker marker, KeyValueRecord record)
    {
        const int QuotaAccountSuffixComponents = 1;
        const int TenantComponentIndex = 1;
        const int DatabaseComponentIndex = 2;
        const int ResourceComponentIndex = 3;
        const int ResourcesValidationBoundary = 1;
        const int ResourcesStep = 1;

        var definition = BlobRecordReader.Decode<ResourceDefinition>(record.Value.Span);
        if (definition.Kind != ResourceKind.BlobStore)
        {
            return marker;
        }
        var parts = KeyCodec.Decode(record.Key.Span);
        if (parts.Length != BlobRestoreFence.QuotaComponents - QuotaAccountSuffixComponents || parts[TenantComponentIndex] is not string tenant
            || parts[DatabaseComponentIndex] is not string database || parts[ResourceComponentIndex] is not string resource || resource != definition.Name)
        {
            throw BlobErrors.Corruption();
        }
        BlobQuotaOperations.ValidatePolicy(definition);
        _ = BlobQuotaOperations.Read(view, BlobKeys.Quota(tenant, database, definition.TransactionDomainId, resource), marker.TargetIncarnation);
        if (marker.Resources < ResourcesValidationBoundary)
        {
            throw BlobErrors.Corruption();
        }
        return marker with
        {
            Resources = marker.Resources - ResourcesStep
        };
    }
    internal static BlobRestorePhase Next(BlobRestorePhase phase) => phase switch
    {
        BlobRestorePhase.States => BlobRestorePhase.Heads,
        BlobRestorePhase.Heads => BlobRestorePhase.Quotas,
        BlobRestorePhase.Quotas => BlobRestorePhase.VerifyStates,
        BlobRestorePhase.VerifyStates => BlobRestorePhase.VerifyHeads,
        BlobRestorePhase.VerifyHeads => BlobRestorePhase.VerifyResources,
        BlobRestorePhase.VerifyResources => BlobRestorePhase.Complete,
        _ => throw BlobErrors.Corruption()
    };
}
