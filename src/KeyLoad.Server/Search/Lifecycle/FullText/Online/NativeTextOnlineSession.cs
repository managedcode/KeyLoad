using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextOnlineSession
{
    private readonly byte[] immutableRequest;
    private readonly CancellationTokenSource deadline;
    private readonly CancellationTokenSource lifetime;
    private bool disposed;
    private bool commandsJoined;
    private byte[]? expectedPublication;

    internal NativeTextOnlineSession(Guid id, OnlineTextIndexMaintenanceRequest request,
        string principalId, DateTimeOffset originalExpiry, ServerRuntimeOptions options,
        TimeProvider clock, CancellationToken shutdown, ReadExecutionBudget? originalBudget = null)
    {
        (deadline, lifetime, Budget, immutableRequest) = NativeTextOnlineSessionWork.Open(id, request, originalExpiry, options, clock, shutdown, originalBudget);
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
    internal ReadExecutionBudget Budget
    {
        get;
    }
    internal OnlineTextPublicationWork Publication
    {
        get;
    }
    internal NativeTextOnlineSourcePin? SourcePin
    {
        get; set;
    }
    internal NativeTextSeedCapture? Base
    {
        get; set;
    }
    internal NativeTextSeedCapture? Current
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
    internal NativeTextIncrementalNativeOwner? NativeOwner
    {
        get; set;
    }
    internal NativeTextIncrementalManifest? Manifest
    {
        get; set;
    }
    internal NativeTextIncrementalManifest? Target
    {
        get; set;
    }
    internal NativeTextIncrementalIntent? Intent
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
    internal NativeTextOnlineCheckpointWork? CheckpointWork
    {
        get; private set;
    }
    internal NativeTextResourceReservation? OperationReservation
    {
        get; set;
    }
    internal NativeTextResourceReservation? GenerationReservation
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

    internal CancellationTokenSource LinkStageCancellation(CancellationToken token)
        => CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);

    internal void Require(Guid id, OnlineTextIndexMaintenanceRequest request, string principalId, DateTimeOffset expiry)
        => NativeTextOnlineSessionWork.Require(this, disposed, immutableRequest, id, request, principalId, expiry);

    internal void DisposeAfterJoinedNativeOwners() => DisposeWithJoinedNativeCleanup(null);

    internal void DisposeWithJoinedNativeCleanup(Action? cleanup)
    {
        if (disposed)
        {
            return;
        }
        if (!commandsJoined)
        {
            throw NativeTextErrors.Ownership();
        }
        NativeTextOnlineSessionWork.Cleanup(this, lifetime, deadline, cleanup);
        disposed = true;
    }
}
