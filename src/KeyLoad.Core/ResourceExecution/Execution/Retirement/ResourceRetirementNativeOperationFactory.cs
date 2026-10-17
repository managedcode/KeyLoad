using System.Text.Json;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    // C1 invokes this only after its existing authenticated native source/receiver boundary.
    // A serialized source principal is a witness, never target administrator authority.
    internal ReplicatedOperation CreateResourceRetirementOperation(Guid commandId, string principalId,
        DateTimeOffset evaluatedAt, ResourceRetirementPhaseCommandV1 phase)
        => CreateResourceRetirementOperationCore(commandId, principalId, evaluatedAt, phase, null);

    internal ReplicatedOperation CreateResourceRetirementOperation(Guid commandId, string principalId,
        DateTimeOffset evaluatedAt, ResourceRetirementPhaseCommandV1 phase, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        return CreateResourceRetirementOperationCore(commandId, principalId, evaluatedAt, phase, work);
    }

    private ReplicatedOperation CreateResourceRetirementOperationCore(Guid commandId, string principalId,
        DateTimeOffset evaluatedAt, ResourceRetirementPhaseCommandV1 phase, ReadExecutionBudget? work)
    {
        if (commandId == Guid.Empty || phase is null || phase.Version != ResourceRetirementProtocol.Version
            || phase.RetirementId == Guid.Empty || !Enum.IsDefined(phase.Phase)
            || phase.Body.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        RequireNativeBudget(phase.Body.Length);
        phase = phase with
        {
            Body = phase.Body.ToArray()
        };
        if (ResourceRetirementPhaseBodyValidation.Require(phase, Limits) != commandId)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        if (phase.Phase == ResourceRetirementPhase.PreparedOperation)
        {
            var prepared = NativeSerialization.Deserialize<ResourceRetirementPreparedOperationBodyV1>(phase.Body.Span);
            if (prepared.Action == ResourceRetirementPreparedOperationAction.Persist)
            {
                var original = VerifyOperationAuthority(prepared.Original!);
                var originalPhase = NativeCommandPayload.Read<ResourceRetirementPhaseCommandV1>(original);
                var expectedPhase = ResourceRetirementCleanupAttemptScope.OriginalPhase(prepared.Context);
                if (originalPhase.Phase != expectedPhase
                    || ResourceRetirementPhaseBodyValidation.Require(originalPhase, Limits) != original.Id)
                {
                    throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
                }
            }
        }
        JsonData.Identifier(principalId);
        var payload = NativeSerialization.Serialize(phase);
        RequireNativeBudget(payload.Length);
        var identity = JsonSerializer.Serialize(phase, JsonDefaults.Options);
        var operation = new ReplicatedOperation(commandId, OperationKind.ResourceRetirementPhase,
            principalId, evaluatedAt, identity);
        return work is null ? IssueNativeOperation(operation, new NativeCommandPayload(payload))
            : IssueNativeOperation(operation, new NativeCommandPayload(payload), work);
    }
}
