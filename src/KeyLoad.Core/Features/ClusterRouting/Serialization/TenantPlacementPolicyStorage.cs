using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

internal static class TenantPlacementPolicyStorage
{
    private const string IntentIdentityFormat = "N";
    internal static byte[] PolicyKey(string tenant, string database)
        => KeyCodec.Encode(TenantPlacementPolicyProtocol.PolicySpace, tenant, database);

    internal static byte[] IntentKey(string tenant, string database, Guid intent)
        => KeyCodec.Encode(TenantPlacementPolicyProtocol.IntentSpace, tenant, database, intent.ToString(IntentIdentityFormat));

    internal static TenantPlacementPolicyV1? ReadPolicy(IKeyValueView view, string tenant,
        string database, int maximumBytes)
    {
        TenantPlacementPolicyV1? result = null;
        view.ReadValue(PolicyKey(tenant, database), bytes =>
        {
            if (bytes.Length > maximumBytes)
            { throw Errors.Fail(ErrorCode.Corruption, TenantPlacementPolicyProtocol.Malformed); }
            result = NativeSerialization.Deserialize<TenantPlacementPolicyV1>(bytes);
            TenantPlacementPolicyValidation.Require(result, ErrorCode.Corruption);
            if (result.TenantId != tenant || result.DatabaseId != database)
            { throw Errors.Fail(ErrorCode.Corruption, TenantPlacementPolicyProtocol.Malformed); }
        });
        return result;
    }

    internal static void WritePolicy(IAtomicTransaction transaction, TenantPlacementPolicyV1 value,
        int maximumBytes)
    {
        TenantPlacementPolicyValidation.Require(value, ErrorCode.Validation);
        transaction.Put(PolicyKey(value.TenantId, value.DatabaseId), Encode(value, maximumBytes));
    }

    internal static byte[] Encode<T>(T value, int maximumBytes)
    {
        var bytes = NativeSerialization.Serialize(value);
        if (bytes.Length > maximumBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, TenantPlacementPolicyProtocol.Invalid); }
        return bytes;
    }
}
