using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementTerminalOperationValidation
{
    internal static void Require(ResourceRetirementStateV1 state, DatabaseLimits limits)
    {
        var original = state.CompletionOperation;
        if (original is null || state.CompletionPolicyEpoch is null or < ResourceRetirementProtocol.Empty
            || original.Kind != OperationKind.ResourceRetirementPhase || original.NativePayload.IsEmpty
            || string.IsNullOrWhiteSpace(original.PrincipalId) || original.Id != state.CompletionReceipt!.CommandId)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        var phase = NativeCommandPayload.Read<ResourceRetirementPhaseCommandV1>(original);
        if (phase.Version != ResourceRetirementProtocol.Version || phase.Phase != ResourceRetirementPhase.Finish
            || phase.RetirementId != state.Control.Request.RetirementId || phase.Body.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        var body = NativeSerialization.Deserialize<ResourceRetirementFinishBodyV1>(phase.Body.Span);
        if (body is null || !ResourceRetirementValidation.Same(body.Control, state.Control))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        ResourceRetirementValidation.Bound(original, limits);
    }
}
