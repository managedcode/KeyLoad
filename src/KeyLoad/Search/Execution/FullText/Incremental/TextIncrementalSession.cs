using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Search;
using KeyLoad.Query.Search;

namespace KeyLoad.Server.Search;

internal sealed class TextIncrementalSession
{
    private readonly byte[] immutableRequest;

    internal TextIncrementalSession(Guid id, TextIndexMaintenanceRequest request,
        TextSeedCapture admitted, long replayUpperSequence, MaintenanceSessionWork work,
        TextProjectionScope? retainedScope = null)
    {
        var budget = work.Budget;
        Work = work;
        if (id == Guid.Empty || request.CommandId == Guid.Empty
            || replayUpperSequence < admitted.Checkpoint || replayUpperSequence > admitted.UpperSequence)
        {
            throw TextErrors.Corrupt();
        }
        budget.Check();
        immutableRequest = SHA256.HashData(OrleansSerialization.Serialize(request));
        Id = id;
        Request = request;
        Upper = admitted;
        ReplayUpperSequence = replayUpperSequence;
        Budget = budget;
        Checkpoint = admitted.Checkpoint;
        Scope = retainedScope ?? new(request.NodeId, admitted.Incarnation, admitted.DataEpoch, admitted.ReadGeneration,
            admitted.Position, request.Consumer.Partition, request.Collection, request.Field,
            admitted.PrincipalId, admitted.PolicyEpoch, admitted.SchemaVersion);
        TextValidation.ValidateScope(Scope, request.NodeId);
        if (Scope.Incarnation != admitted.Incarnation || Scope.DataEpoch != admitted.DataEpoch
            || Scope.ReadGeneration != admitted.ReadGeneration || Scope.Position > admitted.Position
            || Scope.PrincipalId != admitted.PrincipalId || Scope.PolicyEpoch != admitted.PolicyEpoch
            || Scope.SchemaVersion != admitted.SchemaVersion || Scope.Partition != request.Consumer.Partition
            || Scope.Collection != request.Collection || Scope.Field != request.Field)
        {
            throw TextErrors.Mismatch();
        }
        budget.Check();
    }

    internal TextResourceReservation? OperationReservation
    {
        get; set;
    }
    internal Guid Id
    {
        get;
    }
    internal TextIndexMaintenanceRequest Request
    {
        get;
    }
    internal TextSeedCapture Upper
    {
        get;
    }
    internal MaintenanceSessionWork Work { get; }
    internal ReadExecutionBudget Budget { get; }
    internal long ReplayUpperSequence
    {
        get;
    }
    internal long CurrentReplayUpperSequence => Manifest is { Bootstrap: true } bootstrap
        ? bootstrap.ThroughSequence : ReplayUpperSequence;
    internal TextProjectionScope Scope
    {
        get;
    }
    internal long Checkpoint
    {
        get; set;
    }
    internal bool OriginalBuild
    {
        get; set;
    }
    internal string? GenerationLeaf
    {
        get; set;
    }
    internal TextIncrementalOwner? RuntimeOwner
    {
        get; set;
    }
    internal TextIncrementalManifest? Manifest
    {
        get; set;
    }
    internal TextIncrementalIntent? Intent
    {
        get; set;
    }
    internal TextIncrementalManifest? Target
    {
        get; set;
    }

    internal void Require(Guid id, TextIndexMaintenanceRequest request, string principalId)
    {
        Budget.Check();
        if (id != Id || principalId != Upper.PrincipalId)
        {
            throw TextErrors.Mismatch();
        }
        var actual = SHA256.HashData(OrleansSerialization.Serialize(request));
        Budget.Check();
        if (!CryptographicOperations.FixedTimeEquals(immutableRequest, actual))
        {
            throw TextErrors.Mismatch();
        }
    }

    internal void RequireUpper(TextSeedCapture current)
    {
        Budget.Check();
        if (current.Incarnation != Upper.Incarnation || current.DataEpoch != Upper.DataEpoch
            || current.ReadGeneration != Upper.ReadGeneration || current.PrincipalId != Upper.PrincipalId
            || current.PolicyEpoch != Upper.PolicyEpoch || current.SchemaVersion != Upper.SchemaVersion
            || current.ResourceSha256 != Upper.ResourceSha256
            || current.UpperSequence != Upper.UpperSequence || current.AppliedPosition < Upper.AppliedPosition)
        {
            throw TextErrors.Mismatch();
        }
    }
}
