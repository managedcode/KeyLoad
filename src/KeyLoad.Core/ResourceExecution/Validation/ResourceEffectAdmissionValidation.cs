using ManagedCode.Communication.Commands;

namespace KeyLoad.Core.ResourceExecution;

internal static class ResourceEffectAdmissionValidation
{
    internal static ResourceEffectAdmission Read(Command<DatabaseCommand> operation)
    {
        var payload = operation.Value!;
        if (payload.ResourceAdmission.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceEffectAdmissionProtocol.Unbound);
        }
        var value = OrleansSerialization.Deserialize<ResourceEffectAdmission>(payload.ResourceAdmission.Span);
        if (value is null || value.Version != ResourceEffectAdmissionProtocol.Version
            || value.Bindings.IsDefault
            || (value.Error is null) != (value.SafeDetail is null)
            || value.Error is { } error && (!Enum.IsDefined(error) || string.IsNullOrWhiteSpace(value.SafeDetail)
                || !value.Bindings.IsEmpty))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid);
        }
        var keys = new HashSet<ResourceLifetime>();
        foreach (var lifetime in value.Bindings)
        {
            ResourceLifetimeIdentity.Validate(lifetime);
            if (!keys.Add(lifetime))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid);
            }
        }
        return value;
    }
}
