using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Retains scoped abort barriers and work admissions until actual source journal settlement.</summary>
internal sealed class PartitionMovementSourceClosures(
    Lock gate, Dictionary<Guid, PartitionMovementSourceEntry> sessions,
    Dictionary<Guid, PartitionMovementPendingCapture> captures,
    Dictionary<PartitionMovementSourceScope, PartitionMovementSourceEntry[]> closedMoves,
    Dictionary<PartitionMovementSourceScope, NativeRequestWorkLease> closedAdmissions,
    NativeRequestWorkOwner workOwner, Action requireOpen)
{
    private const string ClosedDetail = "The partition movement source capability is unavailable.";

    private const int NoRetainedCaptures = 0;

    /// <summary>Closes one scoped move before its authenticated source abort can enter native apply.</summary>
    internal async Task CloseMoveAsync(PartitionRef partition, Guid moveId)
    {
        var scope = new PartitionMovementSourceScope(partition, moveId);
        PartitionMovementPendingCapture[] pending;
        PartitionMovementSourceEntry[] retained;
        lock (gate)
        {
            requireOpen();
            pending = captures.Values.Where(value => value.MoveId == moveId && value.Partition == partition).ToArray();
            if (!closedMoves.TryGetValue(scope, out retained!))
            {
                retained = sessions.Values.Where(value => value.MoveId == moveId && value.Partition == partition).ToArray();
                RegisterClosure(scope, retained);
            }
        }
        var failures = new List<Exception>();
        foreach (var original in pending)
        { await ServerFailureObserver.ObserveAsync(original.StopAsync, failures).ConfigureAwait(false); }
        foreach (var original in pending)
        { await ServerFailureObserver.ObserveAsync(() => original.Completion.Task, failures).ConfigureAwait(false); }
        foreach (var entry in retained)
        { await ServerFailureObserver.ObserveAsync(() => entry.Session.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        lock (gate)
        {
            foreach (var entry in retained)
            { sessions.Remove(entry.Handle.HandleId); }

        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal void RequireMoveJoined(PartitionRef partition, Guid moveId)
    {
        lock (gate)
        {
            requireOpen();
            if (captures.Values.Any(value => value.MoveId == moveId && value.Partition == partition)
                || sessions.Values.Any(value => value.MoveId == moveId && value.Partition == partition))
            { throw Errors.Fail(ErrorCode.OwnershipLost, ClosedDetail); }
        }
    }

    /// <summary>Returns retained native admission only after the actual durable abort journal is retained.</summary>
    internal void ConfirmMoveClosed(PartitionRef partition, Guid moveId)
    {
        PartitionMovementSourceEntry[] retained;
        lock (gate)
        {
            if (!closedMoves.TryGetValue(new(partition, moveId), out retained!))
            { return; }
        }
        var failures = new List<Exception>();
        foreach (var entry in retained)
        { ServerFailureObserver.Observe(entry.ReleaseWork, failures); }
        NativeRequestWorkLease? admission = null;
        lock (gate)
        {
            if (closedAdmissions.TryGetValue(new(partition, moveId), out var retainedAdmission))
            { admission = retainedAdmission; closedAdmissions.Remove(new(partition, moveId)); }
            closedMoves.Remove(new(partition, moveId));
        }
        if (admission is not null)
        {
            try
            { admission.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private void RegisterClosure(PartitionMovementSourceScope scope, PartitionMovementSourceEntry[] retained)
    {
        NativeRequestWorkLease? admission = null;
        try
        {
            if (retained.Length == NoRetainedCaptures)
            { admission = workOwner.Acquire(Guid.NewGuid(), NativeRequestWorkKind.ReadCapability); }
            closedMoves.Add(scope, retained);
            if (admission is not null)
            {
                closedAdmissions.Add(scope, admission);
                admission = null;
            }
        }
        catch (Exception primary)
        {
            closedMoves.Remove(scope);
            var failures = new List<Exception> { primary };
            if (admission is not null)
            {
                try
                { admission.Dispose(); }
                catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
                catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

}
