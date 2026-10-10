using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

internal static class EventVectorSourceMutationValidation
{
    private const int Version = 1;
    private const string InvalidPhase = "The original event vector source phase is inconsistent.";

    internal static EventVectorSourceMutation ReadOriginal(EventVectorSourcePhase phase,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(admission);
        admission.RequireEncodedBytes(phase.OriginalNativeBody.Length);
        if (phase.Version != Version || phase.PhaseCommandId == Guid.Empty || phase.MapId == Guid.Empty
            || phase.OriginalNativeBody.IsEmpty || phase.SourceOriginalResult is not null
            || !phase.SourceObservationWitness.IsEmpty || !phase.SourceOriginalOutcomeDigest.IsEmpty
            || phase.Disposition != EventVectorSourcePhaseDisposition.Admitted
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(phase.OriginalNativeBody.Span),
                phase.OriginalBodyDigest.Span))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidPhase); }
        return ReadBoundBody(phase, admission);
    }

    internal static EventVectorSourceMutation ReadObserved(EventVectorSourcePhase phase,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(admission);
        admission.RequireEncodedBytes(phase.OriginalNativeBody.Length);
        if (phase.Version != Version || phase.Disposition != EventVectorSourcePhaseDisposition.Observed
            || phase.SourceOriginalResult is null || phase.SourceObservationWitness.IsEmpty
            || phase.SourceOriginalOutcomeDigest.Length != SHA256.HashSizeInBytes
            || phase.OriginalNativeBody.IsEmpty
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(phase.OriginalNativeBody.Span),
                phase.OriginalBodyDigest.Span))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidPhase); }
        return ReadBoundBody(phase, admission);
    }

    private static EventVectorSourceMutation ReadBoundBody(EventVectorSourcePhase phase,
        EventVectorAdmissionPolicy admission)
    {
        var mutation = NativeSerialization.Deserialize<EventVectorSourceMutation>(phase.OriginalNativeBody.Span);
        RequireExact(phase, mutation);
        if (EventVectorSourcePhaseIds.For(phase, admission) != phase.PhaseCommandId)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidPhase); }
        return mutation;
    }

    private static void RequireExact(EventVectorSourcePhase phase, EventVectorSourceMutation mutation)
    {
        if (mutation.Version != Version || mutation.MapId != phase.MapId
            || mutation.ControlPartition != phase.ControlPartition
            || mutation.ControlIncarnation != phase.ControlIncarnation || mutation.Source != phase.Source
            || mutation.PrincipalId != phase.PrincipalId || mutation.SourceOwner is null
            || mutation.ControlOwner is null || phase.SourceOwner is null || phase.ControlOwner is null
            || !PhysicalOwnerEntryValidation.SameOwner(mutation.SourceOwner, phase.SourceOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(mutation.ControlOwner, phase.ControlOwner)
            || mutation.Role != phase.Role || !Enum.IsDefined(mutation.Role)
            || mutation.CleanupGeneration != phase.CleanupGeneration || mutation.CleanupGeneration < EventVectorSourcePhaseIds.InitialCleanupGeneration
            || mutation.CleanupGeneration != EventVectorSourcePhaseIds.InitialCleanupGeneration && mutation.Role != EventVectorSourcePhaseRole.Release
            || mutation.Position != phase.TargetPosition || mutation.CoverageGeneration != phase.CoverageGeneration
            || mutation.SourcePolicyEpoch != phase.SourcePolicyEpoch
            || mutation.SourceSchemaVersion != phase.SourceSchemaVersion
            || mutation.LogicalPlacementRevision != phase.SourceLogicalPlacementRevision
            || mutation.DirectoryFence != phase.SourceDirectoryFence || mutation.Nonce != phase.Nonce
            || mutation.FirstExpiresAt != phase.FirstExpiresAt)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidPhase); }
    }
}
