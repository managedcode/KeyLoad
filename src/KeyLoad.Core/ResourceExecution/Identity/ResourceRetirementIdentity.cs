using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementIdentity
{
    private const int GuidByteCount = 16;
    private const string PreparedOperationIdentitySpace = "keyload.resource-retirement.prepared-operation-id.v1";
    private const string Invalid = "The resource retirement phase identity is invalid.";

    internal static Guid PhaseCommandId(ResourceRetirementRequestV1 request,
        ResourceRetirementPhase phase, Guid physicalShardId)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RetirementId == Guid.Empty || request.ExpectedIncarnation is null
            || !Enum.IsDefined(phase) || physicalShardId == Guid.Empty
            || phase is ResourceRetirementPhase.SealBarrier or ResourceRetirementPhase.PrepareAttempt
                or ResourceRetirementPhase.PreparedOperation or ResourceRetirementPhase.ReserveOwner
                or ResourceRetirementPhase.RecordReservation)
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        if (phase == ResourceRetirementPhase.PrepareRetirement)
        {
            return PreparationCommandId(request);
        }
        if (phase == ResourceRetirementPhase.Begin)
        {
            return request.RetirementId;
        }
        var identity = KeyCodec.Encode(ResourceRetirementContract.PhaseIdentitySpace,
            request.RetirementId, request.TenantId, request.DatabaseId, request.ResourceName,
            request.ExpectedIncarnation.Value, (int)phase, physicalShardId);
        return new Guid(SHA256.HashData(identity).AsSpan()[..GuidByteCount]);
    }

    internal static Guid AttemptCommandId(ResourceRetirementRequestV1 request,
        ResourceRetirementPhase phase, Guid attemptId, Guid physicalShardId)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RetirementId == Guid.Empty || request.ExpectedIncarnation is null
            || attemptId == Guid.Empty || physicalShardId == Guid.Empty
            || phase is not (ResourceRetirementPhase.PrepareAttempt or ResourceRetirementPhase.SealBarrier
                or ResourceRetirementPhase.PreparedOperation or ResourceRetirementPhase.ReserveOwner
                or ResourceRetirementPhase.RecordReservation))
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        var identity = KeyCodec.Encode(ResourceRetirementContract.PhaseIdentitySpace,
            request.RetirementId, request.TenantId, request.DatabaseId, request.ResourceName,
            request.ExpectedIncarnation.Value, (int)phase, physicalShardId, attemptId);
        return new Guid(SHA256.HashData(identity).AsSpan()[..GuidByteCount]);
    }

    internal static Guid PreparationCommandId(ResourceRetirementRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RetirementId == Guid.Empty || request.ExpectedIncarnation is null)
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        var identity = KeyCodec.Encode(ResourceRetirementContract.PhaseIdentitySpace,
            request.RetirementId, request.TenantId, request.DatabaseId, request.ResourceName,
            request.ExpectedIncarnation.Value, (int)ResourceRetirementPhase.PrepareRetirement);
        return new Guid(SHA256.HashData(identity).AsSpan()[..GuidByteCount]);
    }
    internal static Guid AttemptCommandId(ResourceRetirementAttemptContextV1 context,
        ResourceRetirementPhase phase, Guid attemptId)
    {
        if (context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage)
        {
            return ResourceRetirementCleanupAttemptIdentity.Command(context, phase, attemptId);
        }

        var request = ResourceRetirementAttemptContextValidation.Request(context);
        var owner = ResourceRetirementAttemptContextValidation.ExecutionOwner(context);
        return AttemptCommandId(request, phase, attemptId, owner.PhysicalShardId);
    }

    internal static Guid PreparedOperationCommandId(ResourceRetirementAttemptContextV1 context,
        Guid attemptId, ResourceRetirementPreparedOperationAction action)
    {
        if (attemptId == Guid.Empty || !Enum.IsDefined(action))
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        if (context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage)
        {
            return ResourceRetirementCleanupAttemptIdentity.Prepared(context, attemptId, action);
        }

        var request = ResourceRetirementAttemptContextValidation.Request(context);
        var owner = ResourceRetirementAttemptContextValidation.ExecutionOwner(context);
        var identity = KeyCodec.Encode(PreparedOperationIdentitySpace,
            request.RetirementId, request.TenantId, request.DatabaseId, request.ResourceName,
            request.ExpectedIncarnation.Value, (int)ResourceRetirementPhase.PreparedOperation,
            (int)context.Purpose, owner.PhysicalShardId, (int)action, attemptId);
        return new Guid(SHA256.HashData(identity).AsSpan()[..GuidByteCount]);
    }
}
