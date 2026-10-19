using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Server.Features.Search;

internal sealed partial class TextOnlineSession
{
    private readonly byte[] immutableRequest;
    private bool disposed;
    private bool commandsJoined;
    private byte[]? expectedPublication;

    internal TextOnlineSession(Guid id, OnlineTextIndexMaintenanceRequest request,
        string principalId, DateTimeOffset originalExpiry, MaintenanceSessionWork work)
    {
        Work = work;
        Budget = work.Budget;
        immutableRequest = TextOnlineSessionWork.Open(id, request, work);
        Id = id;
        Request = request;
        PrincipalId = principalId;
        OriginalExpiry = originalExpiry;
        Publication = new(id, request.CommandId);
    }

    internal Lock Gate { get; } = new();
    internal Guid Id
    {
        get;
    }
    internal OnlineTextIndexMaintenanceRequest Request
    {
        get;
    }
    internal string PrincipalId
    {
        get;
    }
    internal DateTimeOffset OriginalExpiry
    {
        get;
    }
    internal MaintenanceSessionWork Work { get; }
    internal ReadExecutionBudget Budget
    {
        get;
    }
    internal OnlineTextPublicationWork Publication
    {
        get;
    }
    internal TextOnlineSourcePin? SourcePin
    {
        get; set;
    }
    internal TextSeedCapture? Base
    {
        get; set;
    }
    internal TextSeedCapture? Current
    {
        get; set;
    }
    internal TextProjectionScope? Scope
    {
        get; set;
    }
    internal string? Leaf
    {
        get; set;
    }
    internal Guid GenerationId
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
    internal TextIncrementalManifest? Target
    {
        get; set;
    }
    internal TextIncrementalIntent? Intent
    {
        get; set;
    }
    internal CommitProjectionBatchRequest? CheckpointIntent
    {
        get; set;
    }
    internal ProjectionBatchResult? Checkpoint
    {
        get; set;
    }
    internal TextOnlineCheckpointWork? CheckpointWork
    {
        get; private set;
    }
    internal TextResourceReservation? OperationReservation
    {
        get; set;
    }
    internal TextResourceReservation? GenerationReservation
    {
        get; set;
    }
    internal Guid? ExpectedCurrentCommandId
    {
        get; set;
    }
    internal Command<DatabaseCommand>? Issued
    {
        get; set;
    }
    internal bool CatalogCommitted
    {
        get; set;
    }
    internal OnlineTextIndexMaintenanceResult? JoinedPublicationReceipt
    {
        get; private set;
    }

    internal void Require(Guid id, OnlineTextIndexMaintenanceRequest request, string principalId, DateTimeOffset expiry)
        => TextOnlineSessionWork.Require(this, disposed, immutableRequest, id, request, principalId, expiry);

    internal void DisposeAfterJoinedOwners() => DisposeWithJoinedCleanup(null);

    internal void DisposeWithJoinedCleanup(Action? cleanup)
    {
        if (disposed)
        {
            return;
        }
        if (!commandsJoined)
        {
            throw TextErrors.Ownership();
        }
        TextOnlineSessionWork.Cleanup(this, cleanup);
        disposed = true;
    }
}
