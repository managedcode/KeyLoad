using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Orleans;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextOnlineSession
{
    private const int SuccessfulCleanupFailureCount = 0;
    private readonly byte[] immutableRequest;
    private readonly CancellationTokenSource deadline;
    private readonly CancellationTokenSource lifetime;
    private bool disposed;
    private bool commandsJoined;
    private byte[]? expectedPublication;

    internal NativeTextOnlineSession(Guid id, OnlineTextIndexMaintenanceRequest request,
        string principalId, DateTimeOffset originalExpiry, ServerRuntimeOptions options,
        TimeProvider clock, CancellationToken shutdown)
    {
        var remaining = originalExpiry - clock.GetUtcNow();
        if (id == Guid.Empty || request.CommandId == Guid.Empty || remaining <= TimeSpan.Zero)
        { throw NativeTextErrors.Ownership(); }
        CancellationTokenSource? openedDeadline = null;
        CancellationTokenSource? openedLifetime = null;
        ReadExecutionBudget admitted;
        byte[] fingerprint;
        try
        {
            var originalBound = TimeSpan.FromSeconds(options.Core.DatabaseLimits.Value.QueryDeadlineSeconds);
            openedDeadline = new(remaining < originalBound ? remaining : originalBound, clock);
            openedLifetime = CancellationTokenSource.CreateLinkedTokenSource(shutdown, openedDeadline.Token);
            admitted = new(options.Core.DatabaseLimits, clock, openedLifetime.Token);
            admitted.ConstrainLifetime(originalExpiry);
            admitted.ChargeBytes(NativeSerialization.Measure(request));
            admitted.ChargeBytes(SHA256.HashSizeInBytes);
            fingerprint = SHA256.HashData(NativeSerialization.Serialize(request));
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (openedLifetime is not null)
            { ServerFailureObserver.Observe(openedLifetime.Dispose, failures); }
            if (openedDeadline is not null)
            { ServerFailureObserver.Observe(openedDeadline.Dispose, failures); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        deadline = openedDeadline;
        lifetime = openedLifetime;
        Budget = admitted;
        immutableRequest = fingerprint;
        Id = id;
        Request = request;
        PrincipalId = principalId;
        OriginalExpiry = originalExpiry;
        Publication = new(id, request.CommandId);
    }

    internal Lock Gate { get; } = new();
    internal Guid Id { get; }
    internal OnlineTextIndexMaintenanceRequest Request { get; }
    internal string PrincipalId { get; }
    internal DateTimeOffset OriginalExpiry { get; }
    internal ReadExecutionBudget Budget { get; }
    internal OnlineTextPublicationWork Publication { get; }
    internal NativeTextOnlineSourcePin? SourcePin { get; set; }
    internal NativeTextSeedCapture? Base { get; set; }
    internal NativeTextSeedCapture? Current { get; set; }
    internal TextProjectionScope? Scope { get; set; }
    internal string? Leaf { get; set; }
    internal Guid GenerationId { get; set; }
    internal NativeTextIncrementalNativeOwner? NativeOwner { get; set; }
    internal NativeTextIncrementalManifest? Manifest { get; set; }
    internal NativeTextIncrementalManifest? Target { get; set; }
    internal NativeTextIncrementalIntent? Intent { get; set; }
    internal CommitProjectionBatchRequest? CheckpointIntent { get; set; }
    internal ProjectionBatchResult? Checkpoint { get; set; }
    internal NativeTextOnlineCheckpointWork? CheckpointWork { get; private set; }
    internal NativeTextResourceReservation? OperationReservation { get; set; }
    internal NativeTextResourceReservation? GenerationReservation { get; set; }
    internal Guid? ExpectedCurrentCommandId { get; set; }
    internal ReplicatedOperation? Issued { get; set; }
    internal bool CatalogCommitted { get; set; }
    internal OnlineTextIndexMaintenanceResult? JoinedPublicationReceipt { get; private set; }

    internal void Require(Guid id, OnlineTextIndexMaintenanceRequest request, string principalId,
        DateTimeOffset expiry)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Budget.Check();
        if (id != Id || principalId != PrincipalId || expiry != OriginalExpiry)
        { throw NativeTextErrors.Mismatch(); }
        ChargeRetainedBytes(NativeSerialization.Measure(request));
        ChargeRetainedBytes(SHA256.HashSizeInBytes);
        if (!CryptographicOperations.FixedTimeEquals(immutableRequest,
            SHA256.HashData(NativeSerialization.Serialize(request))))
        { throw NativeTextErrors.Mismatch(); }
    }

    private void ChargeRetainedBytes(long bytes)
    {
        if (SourcePin is { } original)
        { original.AdmitRetainedBytes(bytes); }
        else
        { Budget.ChargeBytes(bytes); }
    }

    internal void DisposeAfterJoinedNativeOwners() => DisposeWithJoinedNativeCleanup(null);

    internal void DisposeWithJoinedNativeCleanup(Action? cleanup)
    {
        if (disposed)
        { return; }
        if (!commandsJoined)
        { throw NativeTextErrors.Ownership(); }
        var failures = new List<Exception>();
        if (SourcePin is { } pin)
        { ServerFailureObserver.Observe(pin.Dispose, failures); }
        if (NativeOwner is { } native)
        { ServerFailureObserver.Observe(native.Dispose, failures); }
        if (failures.Count == SuccessfulCleanupFailureCount && cleanup is not null)
        { ServerFailureObserver.Observe(cleanup, failures); }
        if (failures.Count == SuccessfulCleanupFailureCount)
        {
            ServerFailureObserver.Observe(lifetime.Dispose, failures);
            ServerFailureObserver.Observe(deadline.Dispose, failures);
        }
        if (failures.Count == SuccessfulCleanupFailureCount)
        { ServerFailureObserver.Observe(() => OperationReservation?.CompleteAfterJoinedCleanup(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        disposed = true;
    }
}
