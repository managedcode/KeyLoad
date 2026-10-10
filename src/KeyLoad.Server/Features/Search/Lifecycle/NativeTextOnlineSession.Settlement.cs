using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextOnlineSession
{
    internal void RegisterPublication(OnlineTextIndexMaintenanceResult expected)
    {
        Budget.Check();
        if (expectedPublication is not null || expected.CommandId != Request.CommandId)
        { throw NativeTextErrors.Mismatch(); }
        Budget.ChargeBytes(NativeSerialization.Measure(expected));
        expectedPublication = NativeSerialization.Serialize(expected);
    }

    internal OnlineTextPublicationWork RequirePreparedPublication()
    {
        if (expectedPublication is null || Issued is null)
        { throw NativeTextErrors.Ownership(); }
        return Publication;
    }

    internal async Task CloseCommandAdmissionAndJoinAsync()
    {
        var originalPublication = Publication.CloseAdmissionAndCaptureOriginal();
        var checkpoint = CheckpointWork;
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
        commandsJoined = true;
    }

    private void RequirePublicationReceipt(OperationResult original)
    {
        var actual = original.Get<OnlineTextIndexMaintenanceResult>();
        if (expectedPublication is null || actual.CommandId != Request.CommandId
            || !expectedPublication.AsSpan().SequenceEqual(NativeSerialization.Serialize(actual)))
        { throw NativeTextErrors.Corrupt(); }
        JoinedPublicationReceipt = actual;
    }
}
