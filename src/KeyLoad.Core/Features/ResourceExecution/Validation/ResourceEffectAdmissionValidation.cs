using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceEffectAdmissionValidation
{
    internal static ResourceEffectAdmissionV1 Read(ReplicatedOperation operation, DatabaseLimits limits)
    {
        var payload = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        if (payload.ResourceAdmission.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceEffectAdmissionProtocol.Unbound);
        }
        if (payload.ResourceAdmission.Length > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceEffectAdmissionProtocol.Capacity);
        }
        var value = NativeSerialization.Deserialize<ResourceEffectAdmissionV1>(payload.ResourceAdmission.Span);
        if (value is null || value.Version != ResourceEffectAdmissionProtocol.Version
            || value.OperationFingerprint != NativeOperationFingerprint.Compute(operation)
            || value.Bindings.IsDefault || value.Bindings.Length > limits.MaxBatchMutations
            || (value.Error is null) != (value.SafeDetail is null)
            || value.Error is { } error && (!Enum.IsDefined(error) || string.IsNullOrWhiteSpace(value.SafeDetail)
                || !value.Bindings.IsEmpty))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid);
        }
        var keys = new HashSet<ResourceLifetimeV1>();
        foreach (var lifetime in value.Bindings)
        {
            ResourceOwnerInventoryValidation.Lifetime(lifetime);
            if (!keys.Add(lifetime))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid);
            }
        }
        return value;
    }
}
