using System.Security.Cryptography;
using ManagedCode.Communication;

namespace KeyLoad.Server.Search;

internal static class TextOnlineSessionWork
{
    internal static byte[] Open(Guid id, OnlineTextIndexMaintenanceRequest request, MaintenanceSessionWork work)
    {
        if (id == Guid.Empty || request.CommandId == Guid.Empty)
        {
            throw TextErrors.Ownership();
        }
        var budget = work.Budget;
        budget.ChargeBytes(OrleansSerialization.Measure(request));
        budget.ChargeBytes(SHA256.HashSizeInBytes);
        return SHA256.HashData(OrleansSerialization.Serialize(request));
    }

    internal static void Require(TextOnlineSession session, bool disposed, byte[] immutableRequest, Guid id,
        OnlineTextIndexMaintenanceRequest request, string principalId, DateTimeOffset expiry)
    {
        ObjectDisposedException.ThrowIf(disposed, session);
        session.Budget.Check();
        if (id != session.Id || principalId != session.PrincipalId || expiry != session.OriginalExpiry)
        {
            throw TextErrors.Mismatch();
        }
        ChargeRetainedBytes(session, OrleansSerialization.Measure(request));
        ChargeRetainedBytes(session, SHA256.HashSizeInBytes);
        if (!CryptographicOperations.FixedTimeEquals(immutableRequest,
            SHA256.HashData(OrleansSerialization.Serialize(request))))
        {
            throw TextErrors.Mismatch();
        }
    }

    private static void ChargeRetainedBytes(TextOnlineSession session, long bytes)
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

    internal static void Cleanup(TextOnlineSession session, Action? cleanup)
    {
        var failures = new List<Exception>();
        if (session.SourcePin is { } pin)
        {
            ServerFailureObserver.Observe(pin.Dispose, failures);
        }
        if (session.RuntimeOwner is { } execution)
        {
            ServerFailureObserver.Observe(execution.Dispose, failures);
        }
        if (failures.Count == 0 && cleanup is not null)
        {
            ServerFailureObserver.Observe(cleanup, failures);
        }
        if (failures.Count == 0)
        {
            ServerFailureObserver.Observe(() => session.OperationReservation?.CompleteAfterJoinedCleanup(), failures);
        }
        if (failures.Count == 0)
        {
            ServerFailureObserver.Observe(session.Work.DisposeAfterStage, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task JoinAsync(TextOnlineSession session, Action<Result<object>> RequirePublicationReceipt)
    {
        var originalPublication = session.Publication.CloseAdmissionAndCaptureOriginal();
        var checkpoint = session.CheckpointWork;
        var originalCheckpoint = checkpoint?.Work.CloseAdmissionAndCaptureOriginal();
        var failures = new List<Exception>();
        if (originalCheckpoint is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var actual = await originalCheckpoint;
                _ = checkpoint!.RequireReceipt(actual);
            }, failures);
        }
        if (originalPublication is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var actual = await originalPublication;
                RequirePublicationReceipt(actual);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static OnlineTextIndexMaintenanceResult RequireReceipt(Result<object> original, ref byte[]? expectedPublication, Guid commandId)
    {
        var actual = original.Get<OnlineTextIndexMaintenanceResult>();
        if (expectedPublication is null || actual.CommandId != commandId
            || !expectedPublication.AsSpan().SequenceEqual(OrleansSerialization.Serialize(actual)))
        {
            throw TextErrors.Corrupt();
        }
        return actual;
    }
}
