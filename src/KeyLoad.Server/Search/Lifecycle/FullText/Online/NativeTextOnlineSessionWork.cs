using System.Security.Cryptography;
using KeyLoad.Core;
using ManagedCode.Communication;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineSessionWork
{
    private const int SuccessfulCleanupFailureCount = 0;

    internal static (CancellationTokenSource Deadline, CancellationTokenSource Lifetime, ReadExecutionBudget Budget, byte[] Fingerprint)
        Open(Guid id, OnlineTextIndexMaintenanceRequest request, DateTimeOffset originalExpiry, ServerRuntimeOptions options, TimeProvider clock, CancellationToken shutdown, ReadExecutionBudget? originalBudget = null)
    {
        var remaining = originalExpiry - clock.GetUtcNow();
        if (id == Guid.Empty || request.CommandId == Guid.Empty || remaining <= TimeSpan.Zero)
        {
            throw NativeTextErrors.Ownership();
        }
        CancellationTokenSource? openedDeadline = null;
        CancellationTokenSource? openedLifetime = null;
        ReadExecutionBudget admitted;
        byte[] fingerprint;
        try
        {
            var originalBound = TimeSpan.FromSeconds(options.Core.DatabaseLimits.Value.QueryDeadlineSeconds);
            openedDeadline = new(remaining < originalBound ? remaining : originalBound, clock);
            openedLifetime = CancellationTokenSource.CreateLinkedTokenSource(shutdown, openedDeadline.Token);
            admitted = originalBudget ?? new(options.Core.DatabaseLimits, clock, openedLifetime.Token);
            admitted.ConstrainLifetime(originalExpiry);
            admitted.ChargeBytes(NativeSerialization.Measure(request));
            admitted.ChargeBytes(SHA256.HashSizeInBytes);
            fingerprint = SHA256.HashData(NativeSerialization.Serialize(request));
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (openedLifetime is not null)
            {
                ServerFailureObserver.Observe(openedLifetime.Dispose, failures);
            }
            if (openedDeadline is not null)
            {
                ServerFailureObserver.Observe(openedDeadline.Dispose, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        return (openedDeadline, openedLifetime, admitted, fingerprint);
    }

    internal static void Require(NativeTextOnlineSession session, bool disposed, byte[] immutableRequest, Guid id,
        OnlineTextIndexMaintenanceRequest request, string principalId, DateTimeOffset expiry)
    {
        ObjectDisposedException.ThrowIf(disposed, session);
        session.Budget.Check();
        if (id != session.Id || principalId != session.PrincipalId || expiry != session.OriginalExpiry)
        {
            throw NativeTextErrors.Mismatch();
        }
        ChargeRetainedBytes(session, NativeSerialization.Measure(request));
        ChargeRetainedBytes(session, SHA256.HashSizeInBytes);
        if (!CryptographicOperations.FixedTimeEquals(immutableRequest,
            SHA256.HashData(NativeSerialization.Serialize(request))))
        {
            throw NativeTextErrors.Mismatch();
        }
    }

    private static void ChargeRetainedBytes(NativeTextOnlineSession session, long bytes)
    {
        if (session.SourcePin is { } original)
        {
            original.AdmitRetainedBytes(bytes);
        }
        else
        {
            session.Budget.ChargeBytes(bytes);
        }
    }

    internal static void Cleanup(NativeTextOnlineSession session, CancellationTokenSource lifetime, CancellationTokenSource deadline, Action? cleanup)
    {
        var failures = new List<Exception>();
        if (session.SourcePin is { } pin)
        {
            ServerFailureObserver.Observe(pin.Dispose, failures);
        }
        if (session.NativeOwner is { } native)
        {
            ServerFailureObserver.Observe(native.Dispose, failures);
        }
        if (failures.Count == SuccessfulCleanupFailureCount && cleanup is not null)
        {
            ServerFailureObserver.Observe(cleanup, failures);
        }
        if (failures.Count == SuccessfulCleanupFailureCount)
        {
            ServerFailureObserver.Observe(lifetime.Dispose, failures);
            ServerFailureObserver.Observe(deadline.Dispose, failures);
        }
        if (failures.Count == SuccessfulCleanupFailureCount)
        {
            ServerFailureObserver.Observe(() => session.OperationReservation?.CompleteAfterJoinedCleanup(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task JoinAsync(NativeTextOnlineSession session, Action<Result<object>> RequirePublicationReceipt)
    {
        var originalPublication = session.Publication.CloseAdmissionAndCaptureOriginal();
        var checkpoint = session.CheckpointWork;
        var originalCheckpoint = checkpoint?.Work.CloseAdmissionAndCaptureOriginal();
        var failures = new List<Exception>();
        if (originalCheckpoint is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var actual = await originalCheckpoint.ConfigureAwait(false);
                _ = checkpoint!.RequireReceipt(actual);
            }, failures).ConfigureAwait(false);
        }
        if (originalPublication is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var actual = await originalPublication.ConfigureAwait(false);
                RequirePublicationReceipt(actual);
            }, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static OnlineTextIndexMaintenanceResult RequireReceipt(Result<object> original, ref byte[]? expectedPublication, Guid commandId)
    {
        var actual = original.Get<OnlineTextIndexMaintenanceResult>();
        if (expectedPublication is null || actual.CommandId != commandId
            || !expectedPublication.AsSpan().SequenceEqual(NativeSerialization.Serialize(actual)))
        {
            throw NativeTextErrors.Corrupt();
        }
        return actual;
    }
}
