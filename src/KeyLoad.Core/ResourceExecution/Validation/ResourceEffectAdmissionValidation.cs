using KeyLoad.Core.Features.InternalSerialization;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceEffectAdmissionValidation
{
    internal static ResourceEffectAdmission Read(Command<DatabaseCommand> operation, DatabaseLimits limits)
    {
        var payload = operation.Value!;
        if (payload.ResourceAdmission.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceEffectAdmissionProtocol.Unbound);
        }
        if (payload.ResourceAdmission.Length > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceEffectAdmissionProtocol.Capacity);
        }
        var value = OrleansSerialization.Deserialize<ResourceEffectAdmission>(payload.ResourceAdmission.Span);
        if (value is null || value.Version != ResourceEffectAdmissionProtocol.Version
            || value.OperationFingerprint != DatabaseCommandFingerprint.Compute(operation)
            || value.Bindings.IsDefault
            || (value.Error is null) != (value.SafeDetail is null)
            || value.Error is { } error && (!Enum.IsDefined(error) || string.IsNullOrWhiteSpace(value.SafeDetail)
                || !value.Bindings.IsEmpty))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid);
        }
        var keys = new HashSet<ResourceLifetime>();
        long retainedBytes = ResourceEffectAdmissionProtocol.Zero;
        foreach (var lifetime in value.Bindings)
        {
            ResourceLifetimeIdentity.Validate(lifetime);
            var bytes = OrleansSerialization.Measure(lifetime);
            if (bytes <= ResourceEffectAdmissionProtocol.Zero || bytes > limits.MaxBatchBytes - retainedBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid);
            }
            if (!keys.Add(lifetime))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid);
            }
            retainedBytes += bytes;
        }
        return value;
    }
}
