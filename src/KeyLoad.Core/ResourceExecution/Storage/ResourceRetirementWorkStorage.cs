using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Persists only actual owner replicated work acknowledgements, independently of volatile join handles.</summary>
internal static class ResourceRetirementWorkStorage
{
    private const string OwnerSpace = "resource-retirement-owner-work-v1";

    internal static byte[] OwnerKey(ResourceLifetimeV1 lifetime)
        => KeyCodec.Encode(OwnerSpace, lifetime.TenantId, lifetime.DatabaseId,
            lifetime.ResourceName, lifetime.Incarnation.Value);

    internal static ResourceRetirementWorkOwnerReceiptV1? ReadOwner(IKeyValueView view,
        ResourceRetirementControlV1 control, DatabaseLimits limits)
    {
        var receipt = ResourceRetirementStorage.Read<ResourceRetirementWorkOwnerReceiptV1>(
            view, OwnerKey(control.Lifetime), limits);
        if (receipt is not null)
        {
            ResourceRetirementWorkValidation.Owner(receipt, control, limits);
        }
        return receipt;
    }

    internal static void WriteOwner(IAtomicTransaction transaction,
        ResourceRetirementWorkOwnerReceiptV1 receipt, DatabaseLimits limits)
    {
        ResourceRetirementWorkValidation.Owner(receipt, receipt.Control, limits);
        transaction.PutRecord(OwnerKey(receipt.Control.Lifetime), receipt);
    }
}
