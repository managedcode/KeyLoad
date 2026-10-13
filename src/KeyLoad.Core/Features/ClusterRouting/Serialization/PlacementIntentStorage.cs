using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

internal static class PlacementIntentStorage
{
    internal static PlacementIntentV1? Read(IKeyValueView view, string tenant, string database,
        Guid intentId, int maximumBytes)
    {
        PlacementIntentV1? result = null;
        view.ReadValue(TenantPlacementPolicyStorage.IntentKey(tenant, database, intentId), bytes =>
        {
            if (bytes.Length > maximumBytes)
            { throw Errors.Fail(ErrorCode.Corruption, TenantPlacementPolicyProtocol.Malformed); }
            result = NativeSerialization.Deserialize<PlacementIntentV1>(bytes);
            PlacementIntentValidation.Require(result, ErrorCode.Corruption);
            if (result.IntentId != intentId || result.Plan.TenantId != tenant || result.Plan.DatabaseId != database)
            { throw Errors.Fail(ErrorCode.Corruption, TenantPlacementPolicyProtocol.Malformed); }
        });
        return result;
    }

    internal static void Write(IAtomicTransaction transaction, PlacementIntentV1 value, int maximumBytes)
    {
        PlacementIntentValidation.Require(value, ErrorCode.Validation);
        transaction.Put(TenantPlacementPolicyStorage.IntentKey(value.Plan.TenantId, value.Plan.DatabaseId,
            value.IntentId), TenantPlacementPolicyStorage.Encode(value, maximumBytes));
    }
}
