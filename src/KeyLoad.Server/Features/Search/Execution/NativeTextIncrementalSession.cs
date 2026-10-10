using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextIncrementalSession
{
    private readonly byte[] immutableRequest;

    internal NativeTextIncrementalSession(Guid id, TextIndexMaintenanceRequest request,
        NativeTextSeedCapture admitted, long replayUpperSequence, ReadExecutionBudget budget,
        TextProjectionScope? retainedScope = null)
    {
        if (id == Guid.Empty || request.CommandId == Guid.Empty
            || replayUpperSequence < admitted.Checkpoint || replayUpperSequence > admitted.UpperSequence)
        { throw NativeTextErrors.Corrupt(); }
        budget.Check();
        budget.ChargeBytes(NativeSerialization.Measure(request));
        budget.ChargeBytes(SHA256.HashSizeInBytes);
        immutableRequest = SHA256.HashData(NativeSerialization.Serialize(request));
        Id = id;
        Request = request;
        Upper = admitted;
        ReplayUpperSequence = replayUpperSequence;
        Budget = budget;
        Checkpoint = admitted.Checkpoint;
        Scope = retainedScope ?? new(request.NodeId, admitted.Incarnation, admitted.DataEpoch, admitted.ReadGeneration,
            admitted.Position, request.Consumer.Partition, request.Collection, request.Field,
            admitted.PrincipalId, admitted.PolicyEpoch, admitted.SchemaVersion);
        NativeTextValidation.ValidateScope(Scope, request.NodeId);
        if (Scope.Incarnation != admitted.Incarnation || Scope.DataEpoch != admitted.DataEpoch
            || Scope.ReadGeneration != admitted.ReadGeneration || Scope.Position > admitted.Position
            || Scope.PrincipalId != admitted.PrincipalId || Scope.PolicyEpoch != admitted.PolicyEpoch
            || Scope.SchemaVersion != admitted.SchemaVersion || Scope.Partition != request.Consumer.Partition
            || Scope.Collection != request.Collection || Scope.Field != request.Field)
        { throw NativeTextErrors.Mismatch(); }
        budget.Check();
    }

    internal NativeTextResourceReservation? OperationReservation { get; set; }
    internal Guid Id { get; }
    internal TextIndexMaintenanceRequest Request { get; }
    internal NativeTextSeedCapture Upper { get; }
    internal ReadExecutionBudget Budget { get; }
    internal long ReplayUpperSequence { get; }
    internal long CurrentReplayUpperSequence => Manifest is { Bootstrap: true } bootstrap
        ? bootstrap.ThroughSequence : ReplayUpperSequence;
    internal TextProjectionScope Scope { get; }
    internal long Checkpoint { get; set; }
    internal bool OriginalBuild { get; set; }
    internal string? GenerationLeaf { get; set; }
    internal NativeTextIncrementalNativeOwner? NativeOwner { get; set; }
    internal NativeTextIncrementalManifest? Manifest { get; set; }
    internal NativeTextIncrementalIntent? Intent { get; set; }
    internal NativeTextIncrementalManifest? Target { get; set; }

    internal void Require(Guid id, TextIndexMaintenanceRequest request, string principalId)
    {
        Budget.Check();
        if (id != Id || principalId != Upper.PrincipalId)
        { throw NativeTextErrors.Mismatch(); }
        Budget.ChargeBytes(NativeSerialization.Measure(request));
        Budget.ChargeBytes(SHA256.HashSizeInBytes);
        var actual = SHA256.HashData(NativeSerialization.Serialize(request));
        Budget.Check();
        if (!CryptographicOperations.FixedTimeEquals(immutableRequest, actual))
        { throw NativeTextErrors.Mismatch(); }
    }

    internal void RequireUpper(NativeTextSeedCapture current)
    {
        Budget.Check();
        if (current.Incarnation != Upper.Incarnation || current.DataEpoch != Upper.DataEpoch
            || current.ReadGeneration != Upper.ReadGeneration || current.PrincipalId != Upper.PrincipalId
            || current.PolicyEpoch != Upper.PolicyEpoch || current.SchemaVersion != Upper.SchemaVersion
            || current.ResourceSha256 != Upper.ResourceSha256
            || current.UpperSequence != Upper.UpperSequence || current.AppliedPosition < Upper.AppliedPosition)
        { throw NativeTextErrors.Mismatch(); }
    }
}
