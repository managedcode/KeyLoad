using System.Security.Cryptography;
using ManagedCode.Communication;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineSessionWork
{
    private const int SuccessfulCleanupFailureCount = 0;

    internal static byte[] Open(Guid id, OnlineTextIndexMaintenanceRequest request, NativeMaintenanceSessionWork work)
    {
        if (id == Guid.Empty || request.CommandId == Guid.Empty)
        {
            throw NativeTextErrors.Ownership();
        }
        var budget = work.Budget;
        budget.ChargeBytes(NativeSerialization.Measure(request));
        budget.ChargeBytes(SHA256.HashSizeInBytes);
        return SHA256.HashData(NativeSerialization.Serialize(request));
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

    internal static void Cleanup(NativeTextOnlineSession session, Action? cleanup)
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
            ServerFailureObserver.Observe(() => session.OperationReservation?.CompleteAfterJoinedCleanup(), failures);
        }
        if (failures.Count == SuccessfulCleanupFailureCount)
        {
            ServerFailureObserver.Observe(session.Work.DisposeAfterStage, failures);
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
